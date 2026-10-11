using Clary.Api.Directories;
using Clary.Api.Identity;
namespace Clary.Api;
public static class CompanyContentEndpoints
{
 public static void MapCompanyContentEndpoints(this WebApplication app)
 {
 var runtime=app.Services.GetRequiredService<RuntimeConfiguration>();
app.MapGet("/api/documents",async(HttpContext http,KnowledgeRepository repository,CancellationToken ct)=>await repository.DocumentsAsync(CompanyPolicies.Context(http)!,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapCompanyFileEndpoints();
app.MapInventoryEndpoints();
app.MapAssetConfigurationEndpoints();
app.MapEquipmentEndpoints();app.MapTicketEndpoints();
app.MapDirectoryEndpoints();
app.MapGet("/api/library",async(HttpContext http,LibraryRepository repository,CancellationToken ct)=>await repository.ListAsync(CompanyPolicies.Context(http)!,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapPost("/api/library/folders/{id:guid}/action",async(HttpContext http,Guid id,FolderAction action,LibraryOperations operations,CancellationToken ct)=>{
    try{return Results.Ok(await operations.ApplyAsync(CompanyPolicies.Context(http)!,id,action,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write,true));
app.MapPost("/api/library/files/{id:guid}/action",async(HttpContext http,Guid id,FolderAction action,LibraryOperations operations,CancellationToken ct)=>{
    try{return Results.Ok(await operations.FileAsync(CompanyPolicies.Context(http)!,id,action,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write,true));
app.MapGet("/api/library/resolve/{id:guid}",async(HttpContext http,Guid id,LibraryOperations operations,CancellationToken ct)=>{var found=await operations.ResolveAsync(CompanyPolicies.Context(http)!,id,ct);return found is null?Results.NotFound(new{error="This linked document is no longer available."}):Results.Ok(new{id=found});}).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapPost("/api/library/pages/{id:guid}",async(HttpContext http,Guid id,NewWikiPage page,LibraryOperations operations,CancellationToken ct)=>{
    try{return Results.Ok(await operations.EditAsync(CompanyPolicies.Context(http)!,id,page,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapPost("/api/library/pages",async(HttpContext http,NewWikiPage page,LibraryOperations operations,CancellationToken ct)=>{
    try{return Results.Ok(await operations.NewPageAsync(CompanyPolicies.Context(http)!,page,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapPost("/api/library/folders",async(HttpContext http,CreateFolderRequest request,LibraryRepository repository,CancellationToken ct)=>{
    try{return Results.Ok(await repository.CreateAsync(CompanyPolicies.Context(http)!,request.Name,request.ParentId,ct));}
    catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapPost("/api/documents/{id:guid}/folder",async(HttpContext http,Guid id,MoveDocumentRequest request,LibraryRepository repository,CancellationToken ct)=>{
    try{await repository.MoveDocumentAsync(CompanyPolicies.Context(http)!,id,request.FolderId,ct);return Results.Ok(new{status="moved"});}
    catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapCompanyConversationEndpoints();app.MapBrieEndpoints();
app.MapPost("/api/documents/upload",async(HttpContext http,HttpRequest request,UploadService uploads,CancellationToken ct)=> {
    try {
        var form=await request.ReadFormAsync(ct);
        var file=form.Files.GetFile("file");
        if(file is null)return Results.BadRequest(new{error="Choose a file to upload."});
        return Results.Ok(await uploads.UploadAsync(CompanyPolicies.Context(http)!,file,form["title"],form["version"],ct));
    } catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(Exception ex)when(ex is not OperationCanceledException and not CompanyResourceMissing and not CompanyDenied){return Results.Problem("Upload failed. Check the file size, format, and local database.",statusCode:400);}
}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapPost("/api/documents/{id:guid}/approve",async(HttpContext http,Guid id,UploadService uploads,CancellationToken ct)=> {
    if(runtime.Provider!="Demo"&&!runtime.Health().LiveReady)return Results.Problem(runtime.Health().ConfigurationError,statusCode:503);
    try {await uploads.ApproveAndIndexAsync(CompanyPolicies.Context(http)!,id,ct);return Results.Ok(new{status="ready"});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(Exception ex)when(ex is not OperationCanceledException and not CompanyResourceMissing and not CompanyDenied){return Results.Problem("Indexing failed. The document is excluded from search until indexing succeeds. Check the selected embedding provider and retry.",statusCode:503);}
}).WithMetadata(new CompanyPermission(CompanyCapability.Approve));
app.MapGet("/api/documents/{id:guid}/text",async(HttpContext http,Guid id,UploadService uploads,LibraryOperations operations,CancellationToken ct)=> {
    var text=await uploads.TextAsync(CompanyPolicies.Context(http)!,id,ct);return text is null?Results.NotFound():Results.Ok(new{text,content=await operations.ContentAsync(CompanyPolicies.Context(http)!,id,ct)});
}).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapGet("/api/passages/{id:guid}",async(HttpContext http,Guid id,IKnowledgeRepository repository,CancellationToken ct)=> {
    var passage=await repository.GetApprovedPassageAsync(id,ct);
    return passage is null?Results.NotFound():Results.Ok(passage);
}).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapPost("/api/samples/index",async(HttpContext http,SampleLoader loader,CancellationToken ct)=> {
    if(runtime.Provider!="Demo"&&!runtime.Health().LiveReady)return Results.Problem(runtime.Health().ConfigurationError,statusCode:503);
    try {await loader.LoadAsync(CompanyPolicies.Context(http)!,ct);return Results.Ok(new{status="ready"});}
    catch(Exception ex) when(ex is not OperationCanceledException and not CompanyDenied) {return Results.Problem("Sample indexing failed. Check the database, selected AI provider, installed models or API quota, then retry.",statusCode:503);}
}).WithMetadata(new CompanyPermission(CompanyCapability.Index));

 }
}


