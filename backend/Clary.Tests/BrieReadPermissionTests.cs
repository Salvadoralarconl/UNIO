using Clary.Api;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;
public class BrieReadPermissionTests {
 sealed class Router:IBrieIntentRouter {public Task<BrieIntentDecision> InterpretAsync(BrieIntentContext c,CancellationToken ct)=>Task.FromResult(c.Message.StartsWith("Create")?new BrieIntentDecision("create_ticket","Create a ticket"):new BrieIntentDecision("documented_help"));}
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string s,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch());}
 [Fact]public async Task ViewerCanAskAndResumeDocumentationButCannotCreateOrInspectTasks()=>await IdentityFixture.Run(async f=>{
  await f.Login();var taskRequest=new BrieMessage("Create a ticket",Guid.NewGuid());var task=(await (await f.Send(HttpMethod.Post,"/api/brie/messages",taskRequest)).Content.ReadFromJsonAsync<Answer>())!.TicketTask!;
  await f.Sql("UPDATE memberships SET role='Viewer'");
  var response=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("The camera preview is black",Guid.NewGuid()));Assert.Equal(HttpStatusCode.OK,response.StatusCode);var answer=(await response.Content.ReadFromJsonAsync<Answer>())!;Assert.NotNull(answer.ConversationId);Assert.Null(answer.TicketTask);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("It still fails",Guid.NewGuid(),answer.ConversationId))).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Create a ticket",Guid.NewGuid()))).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/brie/messages",taskRequest)).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("hello",Guid.NewGuid(),TaskId:task.TaskId))).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync($"/api/brie/tickets/{task.TaskId}")).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,$"/api/conversations/{answer.ConversationId}/action",new {operation="rename",name="No edit"})).StatusCode);
  await f.Sql("UPDATE memberships SET active=false");Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("It still fails",Guid.NewGuid(),answer.ConversationId))).StatusCode);
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM brie_ticket_receipts"));
 },b=>{b.Services.AddSingleton<IBrieIntentRouter>(new Router());b.Services.AddSingleton<IBrieTicketParser>(new Parser());b.Services.AddSingleton<BrieTickets>();});
}
