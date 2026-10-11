using Clary.Api.Identity;
namespace Clary.Api;
public static class CompanyConversationEndpoints {
 public static void MapCompanyConversationEndpoints(this WebApplication app){
 var runtime=app.Services.GetRequiredService<RuntimeConfiguration>();
app.MapPost("/api/conversations/{id:guid}/action",async(HttpContext http,Guid id,ChatAction action,ConversationRepository repository,ConversationStore store,CancellationToken ct)=>{try{await repository.ActionAsync(CompanyPolicies.Context(http)!,id,action,ct);if(action.Operation=="delete")store.Forget(CompanyPolicies.Context(http)!,id);return Results.Ok(new{status="ok"});}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).WithMetadata(new CompanyPermission(CompanyCapability.Write,true));
app.MapGet("/api/pinned-folders",async(HttpContext http,ConversationRepository repository,CancellationToken ct)=>await repository.PinnedFoldersAsync(CompanyPolicies.Context(http)!,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapPost("/api/pinned-folders",async(HttpContext http,ChatProjectRequest request,ConversationRepository repository,CancellationToken ct)=>{try{return Results.Ok(await repository.CreatePinnedFolderAsync(CompanyPolicies.Context(http)!,request.Name,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapPost("/api/pinned-folders/{id:guid}/action",async(HttpContext http,Guid id,ChatAction action,ConversationRepository repository,CancellationToken ct)=>{try{await repository.PinnedFolderActionAsync(CompanyPolicies.Context(http)!,id,action,ct);return Results.Ok(new{status="ok"});}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).WithMetadata(new CompanyPermission(CompanyCapability.Write,true));
app.MapGet("/api/chat-projects",async(HttpContext http,ConversationRepository repository,CancellationToken ct)=>await repository.ProjectsAsync(CompanyPolicies.Context(http)!,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapPost("/api/chat-projects",async(HttpContext http,ChatProjectRequest request,ConversationRepository repository,CancellationToken ct)=>{try{return Results.Ok(await repository.CreateProjectAsync(CompanyPolicies.Context(http)!,request.Name,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapPost("/api/conversations/{id:guid}/share",async(HttpContext http,Guid id,ConversationRepository repository,CancellationToken ct)=>{try{return Results.Ok(new{token=await repository.ShareAsync(CompanyPolicies.Context(http)!,id,ct)});}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).WithMetadata(new CompanyPermission(CompanyCapability.Write));
app.MapGet("/api/shared/conversations/{id:guid}",async(HttpContext http,Guid id,ConversationRepository repository,CancellationToken ct)=>{var result=await repository.SharedAsync(CompanyPolicies.Context(http)!,id,ct);return result is null?Results.NotFound():Results.Ok(result);}).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapGet("/api/conversations",async(HttpContext http,ConversationRepository repository,CancellationToken ct)=>await repository.ListAsync(CompanyPolicies.Context(http)!,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapGet("/api/conversations/{id:guid}",async(HttpContext http,Guid id,ConversationRepository repository,CancellationToken ct)=> {
    var turns=await repository.LoadAsync(CompanyPolicies.Context(http)!,id,ct);return turns is null?Results.NotFound():Results.Ok(turns);
}).WithMetadata(new CompanyPermission(CompanyCapability.Read));
app.MapPost("/api/questions",async(HttpContext http,QuestionRequest request,ConversationStore service,CancellationToken ct)=> {
    if(string.IsNullOrWhiteSpace(request.Question)||request.Question.Length>4000)return Results.BadRequest(new{error="Enter a question between 1 and 4,000 characters."});
    if(runtime.Provider!="Demo"&&!runtime.Health().LiveReady)return Results.Problem(runtime.Health().ConfigurationError,statusCode:503);
    try { return Results.Ok(await service.ReplyAsync(CompanyPolicies.Context(http)!,request,ct)); }
    catch(ConversationConflict ex) {return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex) {return Results.BadRequest(new{error=ex.Message});}
    catch(Exception ex) when(ex is not OperationCanceledException and not CompanyResourceMissing and not CompanyDenied) {
        // Log type only: exception bodies can contain provider data or connection credentials.
        app.Logger.LogWarning("Troubleshooting failed: exceptionType={ExceptionType}",ex.GetType().Name);
        var message=ex.Message is OllamaAnswerProvider.Truncated or OllamaAnswerProvider.Unreadable or OllamaAnswerProvider.Incomplete
            ?ex.Message:"Troubleshooting could not complete. Check index readiness, embedding compatibility, and the selected AI provider. No generated guidance was displayed.";
        return Results.Problem(message,statusCode:503);
    }
}).WithMetadata(new CompanyPermission(CompanyCapability.Write));

 }
}
