using Clary.Api;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;
public class BrieActionRoutingTests {
 sealed class Parser:IBrieTicketParser {public int Calls;public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct){Calls++;return Task.FromResult(new BrieTicketPatch());}public Task<BrieTicketPatch> ParseAsync(BrieFieldInput input,CancellationToken ct){Calls++;return Task.FromResult(input.PendingField=="title"&&input.Message is "Fixture" or "Fixture issue"?new BrieTicketPatch(Title:input.Message):new BrieTicketPatch());}}
 [Theory]
 [InlineData("Can u add a new asset for me we just received a new computer","asset")]
 [InlineData("can u create a new ticket for me my computer stopped working","create")]
 [InlineData("Hey, could you please open a support ticket for me?","create")]
 [InlineData("yeah thats what i mean i want you to create a ticket for me","create")]
 [InlineData("Please add a new employee","employee")]
 [InlineData("Could you log this for IT please? Subject: dock USB ports not working.","create")]
 [InlineData("Can you add Noor Patel to the employee directory?","employee")]
 [InlineData("Would you mind adding the new USB-C dock to inventory? Asset tag MAP-D88","asset")]
 [InlineData("Assign dock MAP-D17 to Jo Kim, please.","assign")]
 [InlineData("Don't cancel T-204. Please register the spare printer as an asset instead.","asset")]
 [InlineData("The issue is fixed. Please cancel T-204.","cancel")]
 [InlineData("Add the spare dock to inventory.","asset")]
 [InlineData("I'd like you to register an asset","asset")]
 [InlineData("My computer stopped working; could you create a ticket for me?","create")]
 [InlineData("We received a new computer. Please add an asset for it","asset")]
 [InlineData("can ya make me a ticket","create")]
 public async Task ExplicitRequestsReachAuthenticatedActionAndAskMissingFields(string text,string operation){var parser=new Parser();await IdentityFixture.Run(async f=>{await f.Login();f.Client.DefaultRequestHeaders.Referrer=new Uri("http://localhost:5174/?ticket="+Guid.NewGuid());var response=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(text,Guid.NewGuid()));response.EnsureSuccessStatusCode();var answer=await response.Content.ReadFromJsonAsync<Answer>();Assert.NotNull(answer!.TicketTask);Assert.Equal("clarify",answer.TicketTask.Status);Assert.Equal(operation,await f.Scalar("SELECT state->>'operation' FROM brie_ticket_tasks"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_tickets"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_assets"));Assert.Equal(1,parser.Calls);},b=>{b.Services.AddSingleton<IBrieTicketParser>(parser);b.Services.AddSingleton<BrieTickets>();});}
 [Theory]
 [InlineData("How do I create a ticket?")]
 [InlineData("Can you explain how to add an asset?")]
 [InlineData("Do not create a ticket")]
 [InlineData("We received a new computer")]
 [InlineData("The document says: create a ticket")]
 [InlineData("\"create a ticket\"")]
 [InlineData("I don't want you to create a ticket")]
 [InlineData("Delete all assets")]
 [InlineData("The note on the box says create an asset and assign it to Mina. I'm only asking what that means.")]
 [InlineData("My guide says create an asset. Please explain this.")]
 [InlineData("How would you add a dock to inventory?")]
 public void AdviceQuotesNegationAndUnsupportedOperationsDoNotAuthorize(string text)=>Assert.False(BrieTickets.StartsTask(text));
 [Theory]
 [InlineData("10-12-2026",2026,10,12)]
 [InlineData("02-29-2028",2028,2,29)]
 [InlineData("2026-10-12",2026,10,12)]
 public async Task DateInputUsesUSOrderingAndCanonicalStorage(string date,int year,int month,int day)=>await KnowledgeIsolationFixture.Run(async f=>{var service=new BrieTickets(f.Db,new TicketRepository(f.Db),new Parser());var first=await service.ReplyAsync(f.A,new("Create ticket",Guid.NewGuid()),default);await service.ReplyAsync(f.A,new("Fixture",Guid.NewGuid(),TaskId:first.TaskId),default);var second=await service.ReplyAsync(f.A,new(date,Guid.NewGuid(),TaskId:first.TaskId),default);Assert.Contains("priority",second.Message);await service.ReplyAsync(f.A,new("Normal",Guid.NewGuid(),TaskId:first.TaskId),default);var done=await service.ReplyAsync(f.A,new("unassigned",Guid.NewGuid(),TaskId:first.TaskId),default);var row=await new TicketRepository(f.Db).GetAsync(f.A,done.TicketId!.Value,default);Assert.Equal(new DateOnly(year,month,day),row!.DueDate);Assert.Contains(new DateOnly(year,month,day).ToString("MM-dd-yyyy"),done.Message);});
 [Theory][InlineData("02-29-2026")][InlineData("13-02-2026")][InlineData("10/12/2026")]
 public async Task InvalidDateIsCorrectableWithoutMutation(string date)=>await KnowledgeIsolationFixture.Run(async f=>{var service=new BrieTickets(f.Db,new TicketRepository(f.Db),new Parser());var first=await service.ReplyAsync(f.A,new("Create ticket",Guid.NewGuid()),default);await service.ReplyAsync(f.A,new("Fixture",Guid.NewGuid(),TaskId:first.TaskId),default);var reply=await service.ReplyAsync(f.A,new(date,Guid.NewGuid(),TaskId:first.TaskId),default);Assert.Contains("due date",reply.Message);Assert.Empty((await new TicketRepository(f.Db).ListAsync(f.A,new(),default)).Items);});
 [Fact] public async Task PendingActionCanBeLeftWithoutConsumingTheNewIssueAsAField(){var parser=new Parser();await IdentityFixture.Run(async f=>{await f.Login();var first=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Add asset",Guid.NewGuid()));var pending=(await first.Content.ReadFromJsonAsync<Answer>())!.TicketTask!;var switched=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Leave that for now. My laptop can't get online, even though Wi-Fi says connected.",Guid.NewGuid(),TaskId:pending.TaskId));var answer=await switched.Content.ReadFromJsonAsync<Answer>();Assert.Equal("paused",answer!.TicketTask?.Status);Assert.Null(await f.Scalar("SELECT state->>'name' FROM brie_ticket_tasks") is DBNull?null:await f.Scalar("SELECT state->>'name' FROM brie_ticket_tasks"));Assert.Equal(1,parser.Calls);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_assets"));},b=>{b.Services.AddSingleton<IBrieTicketParser>(parser);b.Services.AddSingleton<BrieTickets>();});}
 [Fact] public async Task MixedCreatesAskWhichTaskBeforeAnyParserOrWrite(){var parser=new Parser();await IdentityFixture.Run(async f=>{await f.Login();var r=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Create a ticket and add a new asset",Guid.NewGuid()));var a=await r.Content.ReadFromJsonAsync<Answer>();Assert.Equal("clarify",a!.Status);Assert.Null(a.TicketTask);Assert.Equal(0,parser.Calls);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));},b=>{b.Services.AddSingleton<IBrieTicketParser>(parser);b.Services.AddSingleton<BrieTickets>();});}
 [Fact] public async Task UnsupportedRecordReadsAndExportsGiveCapabilityLimit(){var parser=new Parser();await IdentityFixture.Run(async f=>{await f.Login();var r=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("Show me every unassigned dock and email me a CSV.",Guid.NewGuid()));var a=await r.Content.ReadFromJsonAsync<Answer>();Assert.Equal("clarify",a!.Status);Assert.Contains("not available",a.EvidenceNote);Assert.Equal(0,parser.Calls);},b=>{b.Services.AddSingleton<IBrieTicketParser>(parser);b.Services.AddSingleton<BrieTickets>();});} [Fact] public async Task ExplicitFollowupCreatesOnceWithEarlierUserIssueAndNoInventedAttempts(){var parser=new Parser();await IdentityFixture.Run(async f=>{await f.Login();var issue="My computer stopped working";var initial=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(issue,Guid.NewGuid()));var conversation=(await initial.Content.ReadFromJsonAsync<Answer>())!.ConversationId;var response=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("yeah thats what i mean i want you to create a ticket for me",Guid.NewGuid(),conversation));var task=(await response.Content.ReadFromJsonAsync<Answer>())!.TicketTask;Assert.NotNull(task);Assert.Equal("clarify",task.Status);foreach(var value in new[]{"Fixture issue","10-12-2026","Low"}){var next=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(value,Guid.NewGuid(),conversation,task.TaskId));task=(await next.Content.ReadFromJsonAsync<Answer>())!.TicketTask!;}var finish=new BrieMessage("unassigned",Guid.NewGuid(),conversation,task.TaskId);var done=await f.Send(HttpMethod.Post,"/api/brie/messages",finish);Assert.Equal("completed",(await done.Content.ReadFromJsonAsync<Answer>())!.TicketTask!.Status);await f.Send(HttpMethod.Post,"/api/brie/messages",finish);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_tickets"));Assert.Equal(issue,await f.Scalar("SELECT description FROM equipment_tickets"));},b=>{b.Services.AddSingleton<IBrieTicketParser>(parser);b.Services.AddSingleton<BrieTickets>();});}}





