using Clary.Api;
using Xunit;
public class BrieNaturalDateFlowTests {
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string message,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch(Title:"Issue"));}
 [Theory][InlineData("tomorrow","time zone")][InlineData("in five days","time zone")][InlineData("October 20","time zone")][InlineData("next month","day")]
 public async Task NaturalDateAsksOnlyMissingCalendarInformation(string phrase,string expected)=>await KnowledgeIsolationFixture.Run(async f=>{var service=new BrieTickets(f.Db,new TicketRepository(f.Db),new Parser());var start=await service.ReplyAsync(f.A,new("Create ticket Issue",Guid.NewGuid()),default);var next=await service.ReplyAsync(f.A,new(phrase,Guid.NewGuid(),TaskId:start.TaskId),default);Assert.Contains(expected,next.Message,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Please use MM-DD-YYYY",next.Message);});
}
