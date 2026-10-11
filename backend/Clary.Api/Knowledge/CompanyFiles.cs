using Clary.Api.Identity;
using Npgsql;
using System.Text.Json;
namespace Clary.Api;
public sealed record OwnedFile(string Path,string Kind);
public sealed class CompanyFiles(NpgsqlDataSource db,IWebHostEnvironment environment){
 public async Task<OwnedFile?> OpenAsync(CompanyContext context,Guid fileId,CancellationToken ct){
  await using var q=CompanyCommand.Create(context,"SELECT storage_path,kind FROM owned_files WHERE company_id=@company AND id=@id AND NOT quarantined",db);q.Parameters.AddWithValue("id",fileId);
  await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
  var relative=r.GetString(0);if(relative.Contains('\\')||relative.Split('/').Any(p=>p is ".." or "." or "")||!relative.StartsWith("uploads/",StringComparison.Ordinal)||System.IO.Path.IsPathRooted(relative))return null;
  var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(environment.ContentRootPath,"uploads"))+System.IO.Path.DirectorySeparatorChar;
  var path=System.IO.Path.GetFullPath(System.IO.Path.Combine(environment.ContentRootPath,relative));
  if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return null;
  // Reject links/reparse points anywhere in the allowlisted path.
  for(var current=new FileInfo(path) as FileSystemInfo;current is not null&&current.FullName.Length>=root.TrimEnd(System.IO.Path.DirectorySeparatorChar).Length;current=current is FileInfo f?f.Directory:((DirectoryInfo)current).Parent)
   if((current.Attributes&FileAttributes.ReparsePoint)!=0)return null;
  return new(path,r.GetString(1));
 }
 internal static async Task RecordAsync(CompanyContext context,NpgsqlConnection conn,NpgsqlTransaction tx,Guid id,string path,string kind,Guid? document,CancellationToken ct){
  await using(var q=CompanyCommand.Create(context,"INSERT INTO owned_files(company_id,id,storage_path,kind) VALUES(@company,@id,@path,@kind)",conn,tx)){q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("path",path);q.Parameters.AddWithValue("kind",kind);await q.ExecuteNonQueryAsync(ct);}
  if(document is Guid doc){await using var q=CompanyCommand.Create(context,"INSERT INTO document_files(company_id,document_id,file_id) VALUES(@company,@doc,@id)",conn,tx);q.Parameters.AddWithValue("doc",doc);q.Parameters.AddWithValue("id",id);await q.ExecuteNonQueryAsync(ct);}
 }
 internal static async Task LinkImagesAsync(CompanyContext context,NpgsqlConnection conn,NpgsqlTransaction tx,Guid document,string content,CancellationToken ct){
  // Callers supply validated rich content; inspect the same decoded, rendered nodes as RichDocument.
  using var rich=JsonDocument.Parse(content,new JsonDocumentOptions{MaxDepth=40});
  var images=new HashSet<Guid>();
  void Visit(JsonElement node){
   var type=node.GetProperty("type").GetString();
   if(type=="img"){images.Add(Guid.ParseExact(node.GetProperty("src").GetString()![20..],"D"));return;}
   if(type is "text" or "inventory" or "br" or "hr")return;
   if(node.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())Visit(child);
  }
  foreach(var node in rich.RootElement.EnumerateArray())Visit(node);
  foreach(var id in images){
   await using var q=CompanyCommand.Create(context,"INSERT INTO document_files(company_id,document_id,file_id) SELECT @company,@doc,id FROM owned_files WHERE company_id=@company AND id=@id AND kind='wiki_image' AND NOT quarantined ON CONFLICT DO NOTHING RETURNING file_id",conn,tx);q.Parameters.AddWithValue("doc",document);q.Parameters.AddWithValue("id",id);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyResourceMissing();
  }
 }
 internal static async Task CopyLinksAsync(CompanyContext context,NpgsqlConnection conn,NpgsqlTransaction tx,Guid from,Guid to,CancellationToken ct){
  await using var q=CompanyCommand.Create(context,"INSERT INTO document_files(company_id,document_id,file_id) SELECT company_id,@to,file_id FROM document_files WHERE company_id=@company AND document_id=@from ON CONFLICT DO NOTHING",conn,tx);q.Parameters.AddWithValue("to",to);q.Parameters.AddWithValue("from",from);await q.ExecuteNonQueryAsync(ct);
 }
}
