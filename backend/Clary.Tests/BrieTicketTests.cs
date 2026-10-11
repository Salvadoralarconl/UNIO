using Clary.Api;
using Clary.Api.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

public class BrieTicketTests
{
 sealed class Parser(BrieTicketPatch patch):IBrieTicketParser { public int Calls;public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct){Calls++;return Task.FromResult(patch);} }
 static BrieMessage Message(string text,Guid? task=null,string? token=null)=>new(text,Guid.NewGuid(),TaskId:task,ConfirmationToken:token);
 [Fact] public async Task MissingFieldsResumeWithoutMutationAndRetrySurvivesServiceRestart()=>await KnowledgeIsolationFixture.Run(async f=>{
  var parser=new Parser(new(Title:"Printer issue"));var tickets=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,tickets,parser);
  var first=await service.ReplyAsync(f.A,Message("Create a support ticket titled Printer issue"),default);Assert.Contains("due date",first.Message);
  Assert.Empty((await tickets.ListAsync(f.A,new(),default)).Items);
  var second=await service.ReplyAsync(f.A,Message("2026-11-01",first.TaskId),default);Assert.Contains("priority",second.Message);
  var third=await service.ReplyAsync(f.A,Message("High",first.TaskId),default);Assert.Contains("receive",third.Message);
  Assert.Empty((await tickets.ListAsync(f.A,new(),default)).Items);
  var request=Message("unassigned",first.TaskId);var created=await service.ReplyAsync(f.A,request,default);
  Assert.Equal("completed",created.Status);var row=Assert.Single((await tickets.ListAsync(f.A,new(),default)).Items);Assert.Equal("Printer issue",row.Title);Assert.Equal("High",row.Priority);Assert.Equal(new DateOnly(2026,11,1),row.DueDate);
  var replay=await new BrieTickets(f.Db,tickets,parser).ReplyAsync(f.A,request,default);Assert.Equal(created,replay);Assert.Single((await tickets.ListAsync(f.A,new(),default)).Items);Assert.Equal(1,parser.Calls);
  await Assert.ThrowsAsync<InventoryConflict>(()=>service.ReplyAsync(f.A,request with{Question="different"},default));
 });
 [Fact] public async Task AmbiguousPersonRequiresActualAllowedId()=>await KnowledgeIsolationFixture.Run(async f=>{
  var other=f.A with{UserId=Guid.NewGuid(),Role=CompanyRole.Editor};await FixtureCompany.AddActorAsync(f.Db,other);
  var parser=new Parser(new("Issue",DueDate:"2026-11-01",Priority:"Normal",Assignee:"Fixture actor"));var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,parser);
  var first=await service.ReplyAsync(f.A,Message("Create ticket Issue, due 2026-11-01, Normal, assign Fixture actor"),default);Assert.Equal(2,first.Choices!.Count);Assert.Empty((await repo.ListAsync(f.A,new(),default)).Items);
  var foreign=await service.ReplyAsync(f.A,Message(f.B.UserId.ToString(),first.TaskId),default);Assert.Equal("clarify",foreign.Status);Assert.Empty((await repo.ListAsync(f.A,new(),default)).Items);
  var created=await service.ReplyAsync(f.A,Message(other.UserId.ToString(),first.TaskId),default);Assert.Equal(other.UserId,Assert.Single((await repo.GetAsync(f.A,created.TicketId!.Value,default))!.Assignees).Id);
 });
 [Fact] public async Task ConcurrentDuplicateCreatesOnlyOneTicket()=>await KnowledgeIsolationFixture.Run(async f=>{
  var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new Parser(new("Issue",DueDate:"2026-11-01",Priority:"Normal",Assignee:"unassigned")));
  var input=Message("Create ticket Issue, due 2026-11-01, Normal, unassigned");var replies=await Task.WhenAll(service.ReplyAsync(f.A,input,default),service.ReplyAsync(f.A,input,default));Assert.Equal(replies[0].TicketId,replies[1].TicketId);Assert.Single((await repo.ListAsync(f.A,new(),default)).Items);
 });
 [Fact] public async Task TaskAndReceiptCannotCrossCompanyOrUserAndRevocationDeniesRetry()=>await KnowledgeIsolationFixture.Run(async f=>{
  var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new Parser(new(Title:"Issue")));var input=Message("Create ticket Issue");var first=await service.ReplyAsync(f.A,input,default);
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>service.ReplyAsync(f.B,Message("2026-11-01",first.TaskId),default));
  var other=f.A with{UserId=Guid.NewGuid(),Role=CompanyRole.Editor};await FixtureCompany.AddActorAsync(f.Db,other);await Assert.ThrowsAsync<CompanyResourceMissing>(()=>service.GetAsync(other,first.TaskId,default));
  await f.Sql($"UPDATE memberships SET role='Viewer' WHERE user_id='{f.A.UserId}'");await Assert.ThrowsAsync<CompanyDenied>(()=>service.ReplyAsync(f.A,input,default));
 });
 [Fact] public async Task CancellationRequiresMatchingExplicitConfirmationAndRejectsStaleRevision()=>await KnowledgeIsolationFixture.Run(async f=>{
  var repo=new TicketRepository(f.Db);var ticket=await repo.SaveAsync(f.A,null,new("Cancel fixture",null,"Support",new(2026,11,1)),default);
  var service=new BrieTickets(f.Db,repo,new Parser(new(Target:ticket.Id.ToString())));
  var preview=await service.ReplyAsync(f.A,Message("Cancel ticket "+ticket.Id),default);Assert.Equal("confirmation",preview.Status);Assert.Equal("Pending",(await repo.GetAsync(f.A,ticket.Id,default))!.Status);
  var forged=await service.ReplyAsync(f.A,Message("Confirm",preview.TaskId,"wrong-token"),default);Assert.Equal("confirmation",forged.Status);
  await repo.ActionAsync(f.A,ticket.Id,new("priority",ticket.Revision,Priority:"High"),default);
  var stale=await service.ReplyAsync(f.A,Message("Confirm",preview.TaskId,forged.ConfirmationToken),default);Assert.Equal("confirmation",stale.Status);Assert.NotEqual(forged.ConfirmationToken,stale.ConfirmationToken);
  var request=Message("Confirm",preview.TaskId,stale.ConfirmationToken);var done=await service.ReplyAsync(f.A,request,default);Assert.Equal("completed",done.Status);Assert.Equal("Canceled",(await repo.GetAsync(f.A,ticket.Id,default))!.Status);
  Assert.Equal(done,await service.ReplyAsync(f.A,request,default));
 });
 [Fact] public async Task MutationFailureRollsBackReceiptAndSafeRetryWorks()=>await KnowledgeIsolationFixture.Run(async f=>{
  var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new Parser(new("Failure fixture",DueDate:"2026-11-01",Priority:"Normal",Assignee:"unassigned")));
  await f.Sql("CREATE FUNCTION reject_fixture() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'fixture failure'; END $$; CREATE TRIGGER reject_fixture BEFORE INSERT ON equipment_tickets FOR EACH ROW EXECUTE FUNCTION reject_fixture()");
  var request=Message("Create ticket Failure fixture, due 2026-11-01, Normal, unassigned");await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>service.ReplyAsync(f.A,request,default));
  await using(var q=f.Db.CreateCommand("SELECT count(*) FROM brie_ticket_receipts"))Assert.Equal(0L,await q.ExecuteScalarAsync());
  await f.Sql("DROP TRIGGER reject_fixture ON equipment_tickets");var result=await service.ReplyAsync(f.A,request,default);Assert.Equal("completed",result.Status);Assert.Single((await repo.ListAsync(f.A,new(),default)).Items);
  await using var audit=f.Db.CreateCommand("SELECT after_snapshot::text FROM inventory_audit WHERE resource_type='brie-ticket-task'");var raw=(string)(await audit.ExecuteScalarAsync())!;Assert.DoesNotContain("Failure fixture",raw);Assert.DoesNotContain("confirmationToken",raw);
 });
 [Fact] public async Task HttpRequiresAuthenticationCsrfAndExistingWritePermission()=>await IdentityFixture.Run(async f=>{
  var request=Message("Create ticket Issue, due 2026-11-01, Normal, unassigned");
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.PostAsJsonAsync("/api/brie/messages",request)).StatusCode);
  await f.Login();Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PostAsJsonAsync("/api/brie/messages",request)).StatusCode);
  var response=await f.Send(HttpMethod.Post,"/api/brie/messages",request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var answer=await response.Content.ReadFromJsonAsync<Answer>();Assert.Equal("completed",answer!.TicketTask!.Status);
  Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/tickets/"+answer.TicketTask.TicketId)).StatusCode);
  await f.Sql("UPDATE memberships SET role='Viewer'");Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/brie/messages",request)).StatusCode);
 },builder=>{builder.Services.AddSingleton<IBrieTicketParser>(new Parser(new("Issue",DueDate:"2026-11-01",Priority:"Normal",Assignee:"unassigned")));builder.Services.AddSingleton<BrieTickets>();});
}
