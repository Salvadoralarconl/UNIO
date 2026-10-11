using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
using System.Security.Cryptography;
using System.Text;
namespace Clary.Api;
public sealed record FolderAction(string Operation,Guid? DestinationId,string? Name);
public sealed record FolderActionResult(Guid? FolderId,int PreservedDocuments=0);
public sealed record NewWikiPage(string Title,string Text,Guid? FolderId,string? Content=null);
public sealed class LibraryOperations(NpgsqlDataSource db){
 public async Task<FolderActionResult> ApplyAsync(CompanyContext context,Guid id,FolderAction action,CancellationToken ct){
  if(action.Operation is not ("rename" or "move" or "copy" or "delete"))throw new ArgumentException("Unknown folder action.");
  await using var connection=await db.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);await AccountPermissions.RevalidateAsync(connection,tx,context,action.Operation=="delete"?CompanyCapability.Delete:CompanyCapability.Write,ct);
  // Serialize tree mutations so two simultaneous moves cannot create a cycle.
  await using(var cmd=CompanyCommand.Create(context,"SELECT pg_advisory_xact_lock(hashtextextended(@company::text||':library-tree',0))",connection,tx))await cmd.ExecuteNonQueryAsync(ct);
  var folders=new List<LibraryFolder>();
  await using(var cmd=CompanyCommand.Create(context,"SELECT id,name,parent_id FROM library_folders WHERE company_id=@company",connection,tx)){await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))folders.Add(new(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetGuid(2)));}
  var original=folders.Find(f=>f.Id==id)??throw new CompanyResourceMissing();
  if(action.DestinationId is Guid destination&&!folders.Any(f=>f.Id==destination))throw new CompanyResourceMissing();
  var subtree=new List<LibraryFolder>{original};var seen=new HashSet<Guid>{id};
  for(var i=0;i<subtree.Count;i++)foreach(var child in folders.Where(f=>f.ParentId==subtree[i].Id))if(seen.Add(child.Id))subtree.Add(child);
  if(action.Operation is "move" or "copy"&&action.DestinationId is Guid target&&seen.Contains(target))throw new ArgumentException("Choose a destination outside this folder and its subfolders.");
  NpgsqlCommand Command(string sql)=>CompanyCommand.Create(context,sql,connection,tx);
  static void Uuid(NpgsqlCommand c,string key,Guid? value)=>c.Parameters.AddWithValue(key,NpgsqlDbType.Uuid,(object?)value??DBNull.Value);
  Guid? result=id;var preserved=0;
  if(action.Operation=="rename"){
   var name=action.Name?.Trim()??"";if(name.Length is <1 or >100)throw new ArgumentException("Use a folder name between 1 and 100 characters.");
   await using var cmd=Command("UPDATE library_folders SET name=@name WHERE company_id=@company AND id=@id");Uuid(cmd,"id",id);cmd.Parameters.AddWithValue("name",name);await cmd.ExecuteNonQueryAsync(ct);
  }else if(action.Operation=="move"){
   await using var cmd=Command("UPDATE library_folders SET parent_id=@parent WHERE company_id=@company AND id=@id");Uuid(cmd,"id",id);Uuid(cmd,"parent",action.DestinationId);await cmd.ExecuteNonQueryAsync(ct);
  }else if(action.Operation=="delete"){
   var ids=subtree.Select(f=>f.Id).ToArray();
   await using(var cmd=Command("DELETE FROM document_folders WHERE company_id=@company AND folder_id=ANY(@ids)")){cmd.Parameters.AddWithValue("ids",ids);preserved=await cmd.ExecuteNonQueryAsync(ct);}
   // Remove descendants before their parents. Documents and evidence stay intact at Library root.
   foreach(var node in subtree.AsEnumerable().Reverse()){await using var cmd=Command("DELETE FROM library_folders WHERE company_id=@company AND id=@id");Uuid(cmd,"id",node.Id);await cmd.ExecuteNonQueryAsync(ct);}
   result=original.ParentId;
  }else{
   if(subtree.Count>1000)throw new ArgumentException("Copy up to 1,000 folders at a time.");
   var mapping=subtree.ToDictionary(f=>f.Id,_=>Guid.NewGuid());result=mapping[id];
   foreach(var node in subtree){
    await using(var cmd=Command("INSERT INTO library_folders(company_id,id,name,parent_id) VALUES(@company,@id,@name,@parent)")){Uuid(cmd,"id",mapping[node.Id]);cmd.Parameters.AddWithValue("name",node.Id==id?(node.Name.Length>93?node.Name[..93]:node.Name)+" (copy)":node.Name);Uuid(cmd,"parent",node.Id==id?action.DestinationId:mapping[node.ParentId!.Value]);await cmd.ExecuteNonQueryAsync(ct);}
    var docs=new List<Guid>();await using(var cmd=Command("SELECT document_id FROM document_folders WHERE company_id=@company AND folder_id=@id")){Uuid(cmd,"id",node.Id);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))docs.Add(r.GetGuid(0));}
    foreach(var doc in docs){
     var newId=Guid.NewGuid();
     await using(var cmd=Command("""
       INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,supported_models,rich_content)
       SELECT @company,@new,title,version,'draft','pending',CASE WHEN filename LIKE 'uploads/%' THEN filename ELSE @filename END,content_hash,canonical_text,supported_models,rich_content FROM documents WHERE company_id=@company AND id=@old
       """)){Uuid(cmd,"new",newId);Uuid(cmd,"old",doc);cmd.Parameters.AddWithValue("filename","wiki/"+newId+".md");await cmd.ExecuteNonQueryAsync(ct);}
     await CompanyFiles.CopyLinksAsync(context,connection,tx,doc,newId,ct);
     await using(var cmd=Command("INSERT INTO document_folders(company_id,document_id,folder_id) VALUES(@company,@document,@folder)")){Uuid(cmd,"document",newId);Uuid(cmd,"folder",mapping[node.Id]);await cmd.ExecuteNonQueryAsync(ct);}
    }
   }
  }
  await tx.CommitAsync(ct);return new(result,preserved);
 }
 public async Task<FolderActionResult> FileAsync(CompanyContext context,Guid id,FolderAction action,CancellationToken ct){
  if(action.Operation is not ("rename" or "move" or "copy" or "delete"))throw new ArgumentException("Unknown file action.");
  await using var connection=await db.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);await AccountPermissions.RevalidateAsync(connection,tx,context,action.Operation=="delete"?CompanyCapability.Delete:CompanyCapability.Write,ct);
  await using(var check=CompanyCommand.Create(context,"SELECT id FROM documents WHERE company_id=@company AND id=@id FOR UPDATE",connection,tx)){check.Parameters.AddWithValue("id",id);if(await check.ExecuteScalarAsync(ct) is null)throw new CompanyResourceMissing();}
  NpgsqlCommand Command(string sql){var c=CompanyCommand.Create(context,sql,connection,tx);c.Parameters.AddWithValue("id",id);return c;}
  if(action.Operation=="rename"){
   var name=action.Name?.Trim()??"";if(name.Length is <1 or >200)throw new ArgumentException("Use a file title between 1 and 200 characters.");
   await using var c=Command("UPDATE documents SET title=@name WHERE company_id=@company AND id=@id");c.Parameters.AddWithValue("name",name);await c.ExecuteNonQueryAsync(ct);
  }else if(action.Operation=="delete"){
   await using(var c=Command("DELETE FROM passages WHERE company_id=@company AND revision_id=@id"))await c.ExecuteNonQueryAsync(ct);
   await using(var c=Command("DELETE FROM documents WHERE company_id=@company AND id=@id"))await c.ExecuteNonQueryAsync(ct);
  }else{
   if(action.DestinationId is Guid folder){await using var check=CompanyCommand.Create(context,"SELECT id FROM library_folders WHERE company_id=@company AND id=@folder FOR KEY SHARE",connection,tx);check.Parameters.AddWithValue("folder",folder);if(await check.ExecuteScalarAsync(ct) is null)throw new CompanyResourceMissing();}
   var target=id;
   if(action.Operation=="copy"){
    target=Guid.NewGuid();await using var c=Command("INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,supported_models,rich_content) SELECT @company,@new,title||' (copy)',version,'draft','pending',CASE WHEN filename LIKE 'uploads/%' THEN filename ELSE @filename END,content_hash,canonical_text,supported_models,rich_content FROM documents WHERE company_id=@company AND id=@id");c.Parameters.AddWithValue("new",target);c.Parameters.AddWithValue("filename","wiki/"+target+".md");await c.ExecuteNonQueryAsync(ct);await CompanyFiles.CopyLinksAsync(context,connection,tx,id,target,ct);
   }
   await using(var c=CompanyCommand.Create(context,"DELETE FROM document_folders WHERE company_id=@company AND document_id=@target",connection,tx)){c.Parameters.AddWithValue("target",target);await c.ExecuteNonQueryAsync(ct);}
   if(action.DestinationId is Guid destination){await using var c=CompanyCommand.Create(context,"INSERT INTO document_folders(company_id,document_id,folder_id) VALUES(@company,@target,@folder)",connection,tx);c.Parameters.AddWithValue("target",target);c.Parameters.AddWithValue("folder",destination);await c.ExecuteNonQueryAsync(ct);}
  }
  await tx.CommitAsync(ct);return new(action.DestinationId);
 }
 public async Task<DocumentInfo> EditAsync(CompanyContext context,Guid id,NewWikiPage page,CancellationToken ct){
  var rich=page.Content is null?throw new ArgumentException("Document content is required."):RichDocument.Read(page.Content);
  var title=page.Title?.Trim()??"";if(title.Length is <1 or >200)throw new ArgumentException("Use a title up to 200 characters.");
  await using var conn=await db.OpenConnectionAsync(ct);await using var tx=await conn.BeginTransactionAsync(ct);await AccountPermissions.RevalidateAsync(conn,tx,context,CompanyCapability.Write,ct);
  if(page.FolderId is Guid destination){await using var check=CompanyCommand.Create(context,"SELECT 1 FROM library_folders WHERE company_id=@company AND id=@id FOR KEY SHARE",conn,tx);check.Parameters.AddWithValue("id",destination);if(await check.ExecuteScalarAsync(ct) is null)throw new CompanyResourceMissing();}
  string version;string[] models;
  await using(var c=CompanyCommand.Create(context,"SELECT version,indexing,supported_models FROM documents WHERE company_id=@company AND id=@id AND NOT archived FOR UPDATE",conn,tx)){c.Parameters.AddWithValue("id",id);await using var r=await c.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyResourceMissing();if(r.GetString(1)=="indexing")throw new ArgumentException("Wait for indexing to finish before saving edits.");version=r.GetString(0);models=r.GetFieldValue<string[]>(2);}
  var next=Guid.NewGuid();var nextVersion=decimal.TryParse(version,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var v)?(v+0.1m).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture):version+" edited";
  await using(var c=CompanyCommand.Create(context,"INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,rich_content,supported_models) VALUES(@company,@id,@title,@version,'draft','pending',@file,@hash,@text,@content,@models)",conn,tx)){c.Parameters.AddWithValue("id",next);c.Parameters.AddWithValue("title",title);c.Parameters.AddWithValue("version",nextVersion);c.Parameters.AddWithValue("file","wiki/"+next+".md");c.Parameters.AddWithValue("hash",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rich.Text))));c.Parameters.AddWithValue("text",rich.Text);c.Parameters.AddWithValue("content",rich.Content);c.Parameters.AddWithValue("models",EpsonModels.Extract(rich.Text));await c.ExecuteNonQueryAsync(ct);}
  await CompanyFiles.LinkImagesAsync(context,conn,tx,next,rich.Content,ct);
  await using(var c=CompanyCommand.Create(context,"UPDATE document_folders SET document_id=@next WHERE company_id=@company AND document_id=@id",conn,tx)){c.Parameters.AddWithValue("next",next);c.Parameters.AddWithValue("id",id);await c.ExecuteNonQueryAsync(ct);}
  // Preserve the immutable old text for existing citations, while removing it from new AI searches.
  await using(var c=CompanyCommand.Create(context,"UPDATE documents SET archived=true,approval='revoked',replaced_by=@next WHERE company_id=@company AND id=@id",conn,tx)){c.Parameters.AddWithValue("id",id);c.Parameters.AddWithValue("next",next);await c.ExecuteNonQueryAsync(ct);}
  await tx.CommitAsync(ct);return new(next,title,nextVersion,"draft","pending",null,true,true);
 }
 public async Task<Guid?> ResolveAsync(CompanyContext context,Guid id,CancellationToken ct){await using var c=CompanyCommand.Create(context,"WITH RECURSIVE revisions AS (SELECT id,replaced_by,archived,ARRAY[id] AS path FROM documents WHERE company_id=@company AND id=@id UNION ALL SELECT d.id,d.replaced_by,d.archived,r.path||d.id FROM documents d JOIN revisions r ON d.id=r.replaced_by WHERE d.company_id=@company AND NOT d.id=ANY(r.path) AND cardinality(r.path)<100) SELECT id FROM revisions WHERE NOT archived LIMIT 1",db);c.Parameters.AddWithValue("id",id);return await c.ExecuteScalarAsync(ct) is Guid found?found:null;}
 public async Task<string?> ContentAsync(CompanyContext context,Guid id,CancellationToken ct){await using var c=CompanyCommand.Create(context,"SELECT rich_content FROM documents WHERE company_id=@company AND id=@id",db);c.Parameters.AddWithValue("id",id);return await c.ExecuteScalarAsync(ct) as string;}
 public async Task<DocumentInfo> NewPageAsync(CompanyContext context,NewWikiPage page,CancellationToken ct){
  var title=page.Title?.Trim()??"";var text=page.Text??"";if(page.Content is not null)text=RichDocument.Read(page.Content).Text;
  if(title.Length is <1 or >200||string.IsNullOrWhiteSpace(text)||text.Length>250000)throw new ArgumentException("Use a title up to 200 characters and nonempty page text up to 250,000 characters.");
  var id=Guid.NewGuid();await using var connection=await db.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);await AccountPermissions.RevalidateAsync(connection,tx,context,CompanyCapability.Write,ct);
  await using(var cmd=CompanyCommand.Create(context,"INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,rich_content,supported_models) VALUES(@company,@id,@title,'1.0','draft','pending',@filename,@hash,@text,@content,@models)",connection,tx)){
   cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("title",title);cmd.Parameters.AddWithValue("filename","wiki/"+id+".md");cmd.Parameters.AddWithValue("hash",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));cmd.Parameters.AddWithValue("text",text);cmd.Parameters.AddWithValue("content",NpgsqlDbType.Text,(object?)page.Content??DBNull.Value);cmd.Parameters.AddWithValue("models",EpsonModels.Extract(text));await cmd.ExecuteNonQueryAsync(ct);
  }
  if(page.Content is not null)await CompanyFiles.LinkImagesAsync(context,connection,tx,id,page.Content,ct);
  if(page.FolderId is Guid folder){await using var cmd=CompanyCommand.Create(context,"INSERT INTO document_folders(company_id,document_id,folder_id) VALUES(@company,@id,@folder)",connection,tx);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("folder",folder);try{await cmd.ExecuteNonQueryAsync(ct);}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new CompanyResourceMissing();}}
  await tx.CommitAsync(ct);return new(id,title,"1.0","draft","pending",null,true,true);
 }
}


