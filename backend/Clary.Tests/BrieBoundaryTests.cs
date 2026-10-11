using Clary.Api;
using Clary.Api.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;
public class BrieBoundaryTests {
 sealed class Parser(BrieTicketPatch patch):IBrieTicketParser {public int Calls;public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct){Calls++;return Task.FromResult(patch);}}
 [Fact] public async Task InvalidMissingFieldReplyKeepsPriorFactsWithoutPrematureCreation()=>await KnowledgeIsolationFixture.Run(async f=>{
  var parser=new Parser(new(Title:"Boundary fixture"));var repository=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repository,parser);
  async Task<BrieTicketResult> Send(string text,Guid? task=null)=>await service.ReplyAsync(f.A,new(text,Guid.NewGuid(),TaskId:task),default);
  var first=await Send("Create ticket Boundary fixture");var invalid=await Send("tomorrow",first.TaskId);Assert.Equal("clarify",invalid.Status);Assert.Contains("time zone",invalid.Message);
  var date=await Send("2026-10-12",first.TaskId);Assert.Contains("priority",date.Message);var priority=await Send("critical",first.TaskId);Assert.Contains("priority",priority.Message);Assert.Empty((await repository.ListAsync(f.A,new(),default)).Items);
  await Send("Low",first.TaskId);var done=await Send("unassigned",first.TaskId);var ticket=await repository.GetAsync(f.A,done.TicketId!.Value,default);Assert.Equal("Boundary fixture",ticket!.Title);Assert.Equal(new DateOnly(2026,10,12),ticket.DueDate);Assert.Equal("Low",ticket.Priority);Assert.Equal(1,parser.Calls);
 });
 [Fact] public async Task ReceiptFailureAfterBusinessWriteReturnsFailureAndRollsBackEverything()=>await IdentityFixture.Run(async f=>{
  await f.Login();await f.Sql("CREATE FUNCTION reject_receipt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'private fixture diagnostic'; END $$; CREATE TRIGGER reject_receipt BEFORE INSERT ON brie_ticket_receipts FOR EACH ROW EXECUTE FUNCTION reject_receipt()");
  var request=new BrieMessage("Create ticket Boundary fixture due 2026-10-12 Low unassigned",Guid.NewGuid());var failed=await f.Send(HttpMethod.Post,"/api/brie/messages",request);Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.DoesNotContain("private fixture diagnostic",await failed.Content.ReadAsStringAsync());
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_tickets WHERE title='Boundary fixture'"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_receipts"));
  await f.Sql("DROP TRIGGER reject_receipt ON brie_ticket_receipts");var completed=await f.Send(HttpMethod.Post,"/api/brie/messages",request);Assert.Equal(HttpStatusCode.OK,completed.StatusCode);var answer=await completed.Content.ReadFromJsonAsync<Answer>();Assert.Equal("completed",answer!.TicketTask!.Status);var replay=await f.Send(HttpMethod.Post,"/api/brie/messages",request);Assert.Equal(answer.TicketTask.TicketId,(await replay.Content.ReadFromJsonAsync<Answer>())!.TicketTask!.TicketId);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_tickets WHERE title='Boundary fixture'"));
 },b=>{b.Services.AddSingleton<IBrieTicketParser>(new Parser(new("Boundary fixture",DueDate:"2026-10-12",Priority:"Low",Assignee:"unassigned")));b.Services.AddSingleton<BrieTickets>();});
}

