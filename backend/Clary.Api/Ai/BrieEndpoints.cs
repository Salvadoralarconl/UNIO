using Clary.Api.Identity;
namespace Clary.Api;
public static class BrieEndpoints
{
 public static void MapBrieEndpoints(this WebApplication app){
  app.MapGet("/api/brie/capabilities",async(HttpContext http,Npgsql.NpgsqlDataSource db,CancellationToken ct)=>{
   await using var q=db.CreateCommand("SELECT to_regclass('brie_ticket_tasks') IS NOT NULL AND to_regclass('brie_ticket_receipts') IS NOT NULL");
   var ready=(bool)(await q.ExecuteScalarAsync(ct))!;
   var context=CompanyPolicies.Context(http);var write=ready&&AccountPermissions.Allows(context,CompanyCapability.Write);var settings=write&&AccountPermissions.Allows(context,CompanyCapability.Settings);var available=new List<string>{"documented_answers"};if(write)available.AddRange(["ticket_create","support_ticket_cancel","asset_create","asset_assign"]);if(settings)available.Add("employee_directory_create");
   return Results.Ok(new{tickets=write,available,implemented=new[]{"documented_answers","ticket_create","support_ticket_cancel","asset_create","asset_assign","employee_directory_create"},unsupported=new[]{"bulk_actions","account_changes","document_writes","general_record_editing"}});
  }).WithMetadata(new CompanyPermission(CompanyCapability.Read));
  app.MapPost("/api/brie/messages",async(HttpContext http,BrieMessage input,ConversationStore conversations,CancellationToken ct)=>{
   try {
    if(http.RequestServices.GetService<IBrieIntentRouter>() is {} intentRouter)return Results.Ok(await new BrieOrchestrator(intentRouter,http.RequestServices.GetRequiredService<BrieTickets>(),conversations,http.RequestServices.GetRequiredService<ConversationRepository>(),http.RequestServices.GetService<TimeProvider>()).ReplyAsync(CompanyPolicies.Context(http)!,input,ct));
    if(input.TaskId is null&&BrieTickets.StartsTask(input.Question)&&System.Text.RegularExpressions.Regex.IsMatch(input.Question,@"\b(?:and|also|then)\s+(?:please\s+)?(?:create|add|register|open|file|assign|cancel)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
     return Results.Ok(new Answer("clarify",["Which task should I handle first? Please choose one ticket or inventory action."],[],[],"Brie",null,ConversationId:input.ConversationId));
    if(input.TaskId is Guid pending){
     var pause=System.Text.RegularExpressions.Regex.Match(input.Question,@"^\s*(?:leave that for now|pause (?:that|this)(?: task)?|set that aside)[.!;]?\s*(?<issue>.*)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
     if(pause.Success){
      await http.RequestServices.GetRequiredService<BrieTickets>().GetAsync(CompanyPolicies.Context(http)!,pending,ct);
      var issue=pause.Groups["issue"].Value.Trim();
      var answer=issue.Length==0?new Answer("clarify",["What would you like help with?"],[],[],"Brie",null,ConversationId:input.ConversationId):await conversations.ReplyAsync(CompanyPolicies.Context(http)!,new(issue,input.ConversationId),ct);
      return Results.Ok(answer with{TicketTask=new(pending,"paused","The unfinished task is paused. Pausing did not create or change a business record.")});
     }
    }
    if(input.TaskId is null&&System.Text.RegularExpressions.Regex.IsMatch(input.Question,@"^\s*(?:(?:can|could|would)\s+(?:you|u)\s+)?(?:please\s+)?(?:show|list|find|export|email|delete)\b.*\b(?:assets?|docks?|inventory|tickets?|employees?|CSV)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
     return Results.Ok(new Answer("clarify",[],[],[],"Brie",null,"Record search, export, email and deletion are not available through Brie. Use the relevant Inventory or Tickets module for its available operations.",input.ConversationId));
    if(input.TaskId is not null||BrieTickets.StartsTask(input.Question)){
     var reply=await http.RequestServices.GetRequiredService<BrieTickets>().ReplyAsync(CompanyPolicies.Context(http)!,input,ct);
     return Results.Ok(new Answer("action",[],[],[],"Brie",null,reply.Message,input.ConversationId){TicketTask=reply});
    }
    return Results.Ok(await conversations.ReplyAsync(CompanyPolicies.Context(http)!,new(input.Question,input.ConversationId),ct));
   }catch(CompanyResourceMissing){return Results.NotFound(new{error="This task or record is no longer accessible."});}
   catch(CompanyDenied){return Results.StatusCode(403);}
   catch(InventoryConflict ex){return Results.Conflict(new{error=ex.Message});}
   catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
   catch(Exception ex)when(ex is not OperationCanceledException){app.Logger.LogWarning("Brie request failed: {ExceptionType}",ex.GetType().Name);return Results.Problem("Brie could not complete this request. Retry with the same request ID; do not assume the action succeeded.",statusCode:503);}
  }).WithMetadata(new CompanyPermission(CompanyCapability.Read));
  app.MapGet("/api/brie/tickets/{id:guid}",async(HttpContext http,Guid id,CancellationToken ct)=>{
   try{return Results.Ok(await http.RequestServices.GetRequiredService<BrieTickets>().GetAsync(CompanyPolicies.Context(http)!,id,ct));}
   catch(CompanyResourceMissing){return Results.NotFound();}
  }).WithMetadata(new CompanyPermission(CompanyCapability.Write));
 }
}



