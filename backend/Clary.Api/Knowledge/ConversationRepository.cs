using Clary.Api.Identity;
using System.Text.Json;
using Npgsql;
namespace Clary.Api;
public sealed class ConversationConflict():Exception("This conversation changed while your answer was being prepared. Retry your question to use the latest saved messages.");
public sealed record SavedTurn(string Question,Answer Answer);
public sealed record SavedConversationSummary(Guid Id,string Title,DateTime UpdatedAt,bool Pinned=false,bool Archived=false,Guid? ProjectId=null,Guid? PinnedFolderId=null);
public sealed class ConversationRepository(NpgsqlDataSource database) {
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<IReadOnlyList<SavedTurn>?> LoadAsync(CompanyContext context,Guid id,CancellationToken ct) {
        await using var cmd=CompanyCommand.Create(context,"SELECT turns::text FROM conversations WHERE company_id=@company AND id=@id",database);cmd.Parameters.AddWithValue("id",id);
        var value=await cmd.ExecuteScalarAsync(ct) as string;
        return value is null?null:JsonSerializer.Deserialize<SavedTurn[]>(value,Json)!;
    }
    // Null expected history creates only; existing conversations require an exact DB CAS.
    public async Task SaveAsync(CompanyContext context,Guid id,IReadOnlyList<SavedTurn> turns,CancellationToken ct,IReadOnlyList<SavedTurn>? expectedTurns=null) {
        var sql=expectedTurns is null
            ?"INSERT INTO conversations(company_id,id,title,turns) VALUES(@company,@id,@title,@turns::jsonb) ON CONFLICT(id) DO NOTHING"
            :"UPDATE conversations SET turns=@turns::jsonb,updated_at=now() WHERE company_id=@company AND id=@id AND turns=@expected::jsonb";
        await using var cmd=CompanyCommand.Create(context,sql,database);
        cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("turns",JsonSerializer.Serialize(turns,Json));
        if(expectedTurns is null)cmd.Parameters.AddWithValue("title",turns[0].Question);
        else cmd.Parameters.AddWithValue("expected",JsonSerializer.Serialize(expectedTurns,Json));
        // App-generated question/answer history is part of documented help; editing chat metadata retains its separate write policy.
        if(await AccountPermissions.ExecuteAsync(database,cmd,context,CompanyCapability.Read,ct)>0)return;
        if(await LoadAsync(context,id,ct) is null)throw new CompanyResourceMissing();
        throw new ConversationConflict();
    }
    public async Task<IReadOnlyList<SavedConversationSummary>> ListAsync(CompanyContext context,CancellationToken ct) {
        await using var cmd=CompanyCommand.Create(context,"SELECT id,title,updated_at,pinned,archived,project_id,pinned_folder_id FROM conversations WHERE company_id=@company ORDER BY updated_at DESC,id",database);
        await using var reader=await cmd.ExecuteReaderAsync(ct);var result=new List<SavedConversationSummary>();
        while(await reader.ReadAsync(ct))result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetDateTime(2),reader.GetBoolean(3),reader.GetBoolean(4),reader.IsDBNull(5)?null:reader.GetGuid(5),reader.IsDBNull(6)?null:reader.GetGuid(6)));
        return result;
    }

    public async Task ActionAsync(CompanyContext context,Guid id,ChatAction action,CancellationToken ct){
      var sql=action.Operation switch{"rename"=>"UPDATE conversations SET title=@name WHERE company_id=@company AND id=@id","pin"=>"UPDATE conversations SET pinned=true,archived=false WHERE company_id=@company AND id=@id","unpin"=>"UPDATE conversations SET pinned=false WHERE company_id=@company AND id=@id","archive"=>"UPDATE conversations SET archived=true,pinned=false WHERE company_id=@company AND id=@id","unarchive"=>"UPDATE conversations SET archived=false WHERE company_id=@company AND id=@id","folder"=>"UPDATE conversations SET pinned_folder_id=@folder,pinned=true,archived=false WHERE company_id=@company AND id=@id","move"=>"UPDATE conversations SET project_id=@project WHERE company_id=@company AND id=@id","delete"=>"DELETE FROM conversations WHERE company_id=@company AND id=@id",_=>throw new ArgumentException("Unknown chat action.")};
      if(action.Operation=="rename"&&(string.IsNullOrWhiteSpace(action.Name)||action.Name.Trim().Length>200))throw new ArgumentException("Enter a chat name up to 200 characters.");
      await using var q=CompanyCommand.Create(context,sql,database);q.Parameters.AddWithValue("id",id);if(action.Operation=="rename")q.Parameters.AddWithValue("name",action.Name!.Trim());if(action.Operation=="move")q.Parameters.AddWithValue("project",NpgsqlTypes.NpgsqlDbType.Uuid,(object?)action.ProjectId??DBNull.Value);
      if(action.Operation=="folder")q.Parameters.AddWithValue("folder",NpgsqlTypes.NpgsqlDbType.Uuid,(object?)action.FolderId??DBNull.Value);
      try{if(await AccountPermissions.ExecuteAsync(database,q,context,action.Operation=="delete"?CompanyCapability.Delete:CompanyCapability.Write,ct)==0)throw new CompanyResourceMissing();}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new CompanyResourceMissing();}
    }
    public async Task<IReadOnlyList<ChatProject>> ProjectsAsync(CompanyContext context,CancellationToken ct){await using var q=CompanyCommand.Create(context,"SELECT id,name FROM chat_projects WHERE company_id=@company ORDER BY name,id",database);await using var r=await q.ExecuteReaderAsync(ct);var list=new List<ChatProject>();while(await r.ReadAsync(ct))list.Add(new(r.GetGuid(0),r.GetString(1)));return list;}
    public async Task<ChatProject> CreateProjectAsync(CompanyContext context,string name,CancellationToken ct){name=name.Trim();if(name.Length is <1 or >100)throw new ArgumentException("Enter a project name up to 100 characters.");var id=Guid.NewGuid();await using var q=CompanyCommand.Create(context,"INSERT INTO chat_projects(company_id,id,name) VALUES(@company,@id,@name)",database);q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("name",name);await AccountPermissions.ExecuteAsync(database,q,context,CompanyCapability.Write,ct);return new(id,name);}
    public async Task<IReadOnlyList<ChatProject>> PinnedFoldersAsync(CompanyContext context,CancellationToken ct){await using var q=CompanyCommand.Create(context,"SELECT id,name FROM pinned_chat_folders WHERE company_id=@company ORDER BY name,id",database);await using var r=await q.ExecuteReaderAsync(ct);var list=new List<ChatProject>();while(await r.ReadAsync(ct))list.Add(new(r.GetGuid(0),r.GetString(1)));return list;}
    public async Task<ChatProject> CreatePinnedFolderAsync(CompanyContext context,string? name,CancellationToken ct){name=ValidateFolderName(name);var id=Guid.NewGuid();await using var q=CompanyCommand.Create(context,"INSERT INTO pinned_chat_folders(company_id,id,name) VALUES(@company,@id,@name)",database);q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("name",name);await AccountPermissions.ExecuteAsync(database,q,context,CompanyCapability.Write,ct);return new(id,name);}
    public async Task PinnedFolderActionAsync(CompanyContext context,Guid id,ChatAction action,CancellationToken ct){var sql=action.Operation switch{"rename"=>"UPDATE pinned_chat_folders SET name=@name WHERE company_id=@company AND id=@id","delete"=>"DELETE FROM pinned_chat_folders WHERE company_id=@company AND id=@id",_=>throw new ArgumentException("Unknown folder action.")};await using var q=CompanyCommand.Create(context,sql,database);q.Parameters.AddWithValue("id",id);if(action.Operation=="rename")q.Parameters.AddWithValue("name",ValidateFolderName(action.Name));if(await AccountPermissions.ExecuteAsync(database,q,context,action.Operation=="delete"?CompanyCapability.Delete:CompanyCapability.Write,ct)==0)throw new CompanyResourceMissing();}
    private static string ValidateFolderName(string? name){name=name?.Trim();if(string.IsNullOrEmpty(name)||name.Length>100)throw new ArgumentException("Enter a folder name up to 100 characters.");return name;}
    public async Task<Guid> ShareAsync(CompanyContext context,Guid id,CancellationToken ct){var token=Guid.NewGuid();await using var q=CompanyCommand.Create(context,"INSERT INTO conversation_shares(company_id,id,title,turns) SELECT @company,@token,title,turns FROM conversations WHERE company_id=@company AND id=@id",database);q.Parameters.AddWithValue("token",token);q.Parameters.AddWithValue("id",id);if(await AccountPermissions.ExecuteAsync(database,q,context,CompanyCapability.Write,ct)==0)throw new CompanyResourceMissing();return token;}
    public async Task<object?> SharedAsync(CompanyContext context,Guid id,CancellationToken ct){await using var q=CompanyCommand.Create(context,"SELECT title,turns::text FROM conversation_shares WHERE company_id=@company AND id=@id",database);q.Parameters.AddWithValue("id",id);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new {title=r.GetString(0),turns=JsonSerializer.Deserialize<SavedTurn[]>(r.GetString(1),Json)}:null;}
}

public sealed record ChatAction(string Operation,string? Name=null,Guid? ProjectId=null,Guid? FolderId=null);
public sealed record ChatProject(Guid Id,string Name);
