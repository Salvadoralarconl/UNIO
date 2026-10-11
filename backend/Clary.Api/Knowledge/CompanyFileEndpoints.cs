using Clary.Api.Identity;
using Npgsql;
namespace Clary.Api;
public static class CompanyFileEndpoints {
 public static void MapCompanyFileEndpoints(this WebApplication app){
app.MapPost("/api/library/images",async(HttpContext http,HttpRequest request,IWebHostEnvironment environment,NpgsqlDataSource db,CancellationToken ct)=>{
 if(!request.HasFormContentType)return Results.BadRequest(new{error="Choose an image file."});
 var form=await request.ReadFormAsync(ct);var file=form.Files.GetFile("file");if(file is null||file.Length<1||file.Length>5*1024*1024)return Results.BadRequest(new{error="Choose an image up to 5 MB."});
 using var stream=new MemoryStream();await file.CopyToAsync(stream,ct);var bytes=stream.ToArray();if(WikiImages.ContentType(bytes) is null)return Results.BadRequest(new{error="Use a PNG, JPEG, GIF, or WebP image."});
 var id=Guid.NewGuid();var folder=Path.Combine(environment.ContentRootPath,"uploads","images");Directory.CreateDirectory(folder);await File.WriteAllBytesAsync(Path.Combine(folder,id+".bin"),bytes,ct);try{await using var conn=await db.OpenConnectionAsync(ct);await using var tx=await conn.BeginTransactionAsync(ct);await AccountPermissions.RevalidateAsync(conn,tx,CompanyPolicies.Context(http)!,CompanyCapability.Write,ct);await CompanyFiles.RecordAsync(CompanyPolicies.Context(http)!,conn,tx,id,"uploads/images/"+id+".bin","wiki_image",null,ct);await tx.CommitAsync(ct);}catch{File.Delete(Path.Combine(folder,id+".bin"));throw;}return Results.Ok(new{src="/api/library/images/"+id});
}).WithMetadata(new CompanyPermission(CompanyCapability.Write)).DisableAntiforgery();
app.MapGet("/api/library/images/{id:guid}",async(HttpContext http,Guid id,CompanyFiles files,CancellationToken ct)=>{var file=await files.OpenAsync(CompanyPolicies.Context(http)!,id,ct);if(file is null||file.Kind!="wiki_image")return Results.NotFound();var bytes=await File.ReadAllBytesAsync(file.Path,ct);var type=WikiImages.ContentType(bytes);return type is null?Results.NotFound():Results.File(bytes,type);}).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapGet("/api/files/{id:guid}",async(HttpContext http,Guid id,CompanyFiles files,CancellationToken ct)=>{var file=await files.OpenAsync(CompanyPolicies.Context(http)!,id,ct);return file is null?Results.NotFound():Results.File(file.Path,"application/octet-stream",Path.GetFileName(file.Path));}).WithMetadata(new CompanyPermission(CompanyCapability.Read));

 }
}
