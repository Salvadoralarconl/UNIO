using Clary.Api;
using Xunit;
public class BrieDateEntryAnchorTests {
 sealed class Clock:TimeProvider {public DateTimeOffset Now=DateTimeOffset.Parse("2026-12-31T23:59:59Z");public override DateTimeOffset GetUtcNow()=>Now;}
 sealed class Parser(Clock clock):IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct){clock.Now=DateTimeOffset.Parse("2027-01-01T00:00:02Z");return Task.FromResult(new BrieTicketPatch(Title:"Issue",DueDate:"tomorrow",Priority:"Low",Assignee:"unassigned"));}}
 [Fact]public async Task ModelDelayCannotMoveTheDateEntryAnchor()=>await KnowledgeIsolationFixture.Run(async f=>{var clock=new Clock();var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new Parser(clock),clock);var first=await service.ReplyAsync(f.A,new("Create ticket Issue due tomorrow Low unassigned",Guid.NewGuid()),default);Assert.Contains("time zone",first.Message);var done=await service.ReplyAsync(f.A,new("UTC",Guid.NewGuid(),TaskId:first.TaskId),default);Assert.Equal(new DateOnly(2027,1,1),(await repo.GetAsync(f.A,done.TicketId!.Value,default))!.DueDate);});
}
