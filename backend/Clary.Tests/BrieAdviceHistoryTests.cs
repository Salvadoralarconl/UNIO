using Clary.Api;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;
public class BrieAdviceHistoryTests {
 sealed class Router:IBrieIntentRouter {public Task<BrieIntentDecision> InterpretAsync(BrieIntentContext c,CancellationToken ct)=>Task.FromResult(new BrieIntentDecision("create_ticket",c.Message));}
 [Fact]public async Task AdviceCannotStartTaskAndUserIssueRemainsInConversation()=>await IdentityFixture.Run(async f=>{await f.Login();const string question="What should I put in a ticket about my USB printer showing offline?";var r=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(question,Guid.NewGuid()));var a=(await r.Content.ReadFromJsonAsync<Answer>())!;Assert.Null(a.TicketTask);Assert.NotNull(a.ConversationId);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));},b=>{b.Services.AddSingleton<IBrieIntentRouter>(new Router());b.Services.AddSingleton<BrieTickets>();b.Services.AddSingleton<IBrieTicketParser>(new Parser());});
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch());}
}
