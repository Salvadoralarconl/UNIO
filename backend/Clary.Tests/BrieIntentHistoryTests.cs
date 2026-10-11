using Clary.Api;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;
public class BrieIntentHistoryTests {
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch());}
 sealed class Router:IBrieIntentRouter {
  public List<BrieIntentContext> Contexts=[];
  public Task<BrieIntentDecision> InterpretAsync(BrieIntentContext c,CancellationToken ct){Contexts.Add(c);return Task.FromResult(new BrieIntentDecision("clarify"));}
 }
 [Fact] public async Task NonExecutingClarificationKeepsUserFactsForTheNextTurn(){
  var router=new Router();await IdentityFixture.Run(async f=>{
   await f.Login();const string facts="What would a directory entry look like for Ari Bell, ari.old@example.test? Do not create it yet.";
   var first=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(facts,Guid.NewGuid()));first.EnsureSuccessStatusCode();
   var answer=(await first.Content.ReadFromJsonAsync<Answer>())!;Assert.NotNull(answer.ConversationId);Assert.Null(answer.TicketTask);
   var next=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Use ari.bell@example.test instead, and please add Ari now.",Guid.NewGuid(),answer.ConversationId));next.EnsureSuccessStatusCode();
   Assert.Contains(facts,router.Contexts[1].UserContext);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));
  },b=>{b.Services.AddSingleton<IBrieIntentRouter>(router);b.Services.AddSingleton<IBrieTicketParser>(new Parser());b.Services.AddSingleton<BrieTickets>();});
 }
 [Theory] [InlineData(false)] [InlineData(true)]
 public async Task FirstLegacyIssueBindingPreservesOnlyContinuingFacts(bool newTopic){
  var router=new Router();await IdentityFixture.Run(async f=>{
   await f.Login();const string facts="Asset type is Accessory and status is Available. Do not create anything yet.";
   var first=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(facts,Guid.NewGuid()));first.EnsureSuccessStatusCode();var a=(await first.Content.ReadFromJsonAsync<Answer>())!;
   await f.Sql($"UPDATE conversations SET turns=jsonb_set(turns,'{{0,answer}}',(turns->0->'answer')-'issueId') WHERE id='{a.ConversationId}'");
   var second=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(newTopic?"New issue: help with a different employee.":"Please use those details for this inventory task.",Guid.NewGuid(),a.ConversationId));second.EnsureSuccessStatusCode();var b=(await second.Content.ReadFromJsonAsync<Answer>())!;
   var third=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Please explain the choices.",Guid.NewGuid(),b.ConversationId));third.EnsureSuccessStatusCode();
   if(newTopic)Assert.DoesNotContain(facts,router.Contexts[2].UserContext);else Assert.Contains(facts,router.Contexts[2].UserContext);
   Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));
  },b=>{b.Services.AddSingleton<IBrieIntentRouter>(router);b.Services.AddSingleton<IBrieTicketParser>(new Parser());b.Services.AddSingleton<BrieTickets>();});
 }
 [Fact] public async Task ClarifiedNewIssueDoesNotLeakPriorPersonFacts(){
  var router=new Router();await IdentityFixture.Run(async f=>{
   await f.Login();const string old="What would a directory entry look like for Ari Bell, ari.old@example.test? Do not create it yet.";
   var first=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(old,Guid.NewGuid()));first.EnsureSuccessStatusCode();var a=(await first.Content.ReadFromJsonAsync<Answer>())!;
   var second=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("New issue: I need help deciding what to do with inventory.",Guid.NewGuid(),a.ConversationId));second.EnsureSuccessStatusCode();var b=(await second.Content.ReadFromJsonAsync<Answer>())!;
   Assert.NotEqual(a.IssueId,b.IssueId);
   var third=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Please explain the available choices.",Guid.NewGuid(),b.ConversationId));third.EnsureSuccessStatusCode();
   Assert.DoesNotContain(old,router.Contexts[2].UserContext);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));
  },b=>{b.Services.AddSingleton<IBrieIntentRouter>(router);b.Services.AddSingleton<IBrieTicketParser>(new Parser());b.Services.AddSingleton<BrieTickets>();});
 }}


