using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api;
public sealed record LibraryFolder(Guid Id,string Name,Guid? ParentId);
public sealed record DocumentPlacement(Guid DocumentId,Guid FolderId);
public sealed record LibrarySnapshot(IReadOnlyList<LibraryFolder> Folders,IReadOnlyList<DocumentPlacement> Placements);
public sealed record CreateFolderRequest(string Name,Guid? ParentId);
public sealed record MoveDocumentRequest(Guid? FolderId);
public sealed class LibraryRepository(NpgsqlDataSource db){
 public async Task<LibrarySnapshot> ListAsync(CompanyContext context,CancellationToken ct){
  var folders=new List<LibraryFolder>();var placements=new List<DocumentPlacement>();
  await using(var cmd=CompanyCommand.Create(context,"SELECT id,name,parent_id FROM library_folders WHERE company_id=@company ORDER BY lower(name),id",db)){
   await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))folders.Add(new(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetGuid(2)));
  }
  await using(var cmd=CompanyCommand.Create(context,"SELECT document_id,folder_id FROM document_folders WHERE company_id=@company",db)){
   await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))placements.Add(new(r.GetGuid(0),r.GetGuid(1)));
  }
  return new(folders,placements);
 }
 public async Task<LibraryFolder> CreateAsync(CompanyContext context,string name,Guid? parent,CancellationToken ct){
  name=name?.Trim()??"";if(name.Length is <1 or >100)throw new ArgumentException("Use a folder name between 1 and 100 characters.");
  var folder=new LibraryFolder(Guid.NewGuid(),name,parent);
  await using var cmd=CompanyCommand.Create(context,"INSERT INTO library_folders(company_id,id,name,parent_id) VALUES(@company,@id,@name,@parent)",db);
  cmd.Parameters.AddWithValue("id",folder.Id);cmd.Parameters.AddWithValue("name",name);cmd.Parameters.AddWithValue("parent",NpgsqlDbType.Uuid,(object?)parent??DBNull.Value);
  try{await AccountPermissions.ExecuteAsync(db,cmd,context,CompanyCapability.Write,ct);}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new CompanyResourceMissing();}
  return folder;
 }
 public async Task MoveDocumentAsync(CompanyContext context,Guid document,Guid? folder,CancellationToken ct){
  await using var cmd=CompanyCommand.Create(context,folder is null?"DELETE FROM document_folders WHERE company_id=@company AND document_id=@document":"INSERT INTO document_folders(company_id,document_id,folder_id) VALUES(@company,@document,@folder) ON CONFLICT(document_id) DO UPDATE SET folder_id=excluded.folder_id WHERE document_folders.company_id=@company",db);
  cmd.Parameters.AddWithValue("document",document);if(folder is not null)cmd.Parameters.AddWithValue("folder",folder.Value);
  // A placement changes organization only, never document approval or indexed evidence.
  await using var exists=CompanyCommand.Create(context,"SELECT EXISTS(SELECT 1 FROM documents WHERE company_id=@company AND id=@id)",db);exists.Parameters.AddWithValue("id",document);
  if(!(bool)(await exists.ExecuteScalarAsync(ct))!)throw new CompanyResourceMissing();
  try{await AccountPermissions.ExecuteAsync(db,cmd,context,CompanyCapability.Write,ct);}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new CompanyResourceMissing();}
 }
}

