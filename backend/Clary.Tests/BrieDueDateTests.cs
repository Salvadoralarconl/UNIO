using Clary.Api;
using Xunit;
public class BrieDueDateTests {
 [Theory]
 [InlineData("November 3 this year","UTC-04:00","2027-01-01T02:00:00Z","2026-11-03")]
 [InlineData("November 3 this year","America/New_York","2026-10-11T12:00:00Z","2026-11-03")]
 [InlineData("October 5, 2027.",null,"2026-10-11T12:00:00Z","2027-10-05")]
 [InlineData("October 5 this year","UTC","2026-10-11T12:00:00Z","2026-10-05")]
 [InlineData("tomorrow","UTC-04:00","2026-10-11T02:00:00Z","2026-10-11")]
 [InlineData("today","UTC-04:00","2026-10-11T02:00:00Z","2026-10-10")]
 [InlineData("in five days","UTC-04:00","2026-10-11T02:00:00Z","2026-10-15")]
 [InlineData("in 5 days","UTC","2026-12-29T12:00:00Z","2027-01-03")]
 [InlineData("tomorrow","UTC","2024-02-28T12:00:00Z","2024-02-29")]
 [InlineData("tomorrow","UTC","2025-02-28T12:00:00Z","2025-03-01")]
 [InlineData("in 5 days","America/New_York","2026-03-06T23:30:00Z","2026-03-11")]
 [InlineData("next month 20","UTC","2026-12-31T12:00:00Z","2027-01-20")]
 [InlineData("October20","UTC-04:00","2026-10-11T12:00:00Z","2026-10-20")]
 [InlineData("October 20, 2027",null,"2026-10-11T12:00:00Z","2027-10-20")]
 [InlineData("10-12-2026",null,"2026-10-11T12:00:00Z","2026-10-12")]
 [InlineData("2026-10-12",null,"2026-10-11T12:00:00Z","2026-10-12")]
 public void ResolvesCalendarDates(string text,string? zone,string instant,string expected){var result=BrieDueDates.Resolve(text,zone,DateTimeOffset.Parse(instant));Assert.Equal(DateOnly.Parse(expected),result.Date);}
 [Theory][InlineData("November 3 this year",null,"timeZone")][InlineData("tomorrow",null,"timeZone")][InlineData("October 20",null,"timeZone")][InlineData("next month",null,"dueDay")][InlineData("next month 31","UTC","dueDay")][InlineData("October 1","UTC","dueYear")][InlineData("February 29","UTC","dueYear")][InlineData("sometime soon","UTC","dueDate")][InlineData("in 5000 days","UTC","dueDate")][InlineData("tomorrow","UTC-14:30","timeZone")]
 public void DoesNotGuessMissingCalendarDetails(string text,string? zone,string need){var result=BrieDueDates.Resolve(text,zone,DateTimeOffset.Parse("2026-10-11T12:00:00Z"));Assert.Null(result.Date);Assert.Equal(need,result.Need);}
}
public class BrieNaturalDateClockTests {
 sealed class Clock(DateTimeOffset now):TimeProvider {public DateTimeOffset Now=now;public override DateTimeOffset GetUtcNow()=>Now;}
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch(Title:"Issue",Priority:"Low",Assignee:"unassigned"));}
 [Fact]public async Task PendingTimezoneAcrossMidnightUsesOriginalDateAnchorAndRetry()=>await KnowledgeIsolationFixture.Run(async f=>{var clock=new Clock(DateTimeOffset.Parse("2026-12-31T03:30:00Z"));var repository=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repository,new Parser(),clock);var task=await service.ReplyAsync(f.A,new("Create ticket Issue Low unassigned",Guid.NewGuid()),default);var waiting=await service.ReplyAsync(f.A,new("tomorrow",Guid.NewGuid(),TaskId:task.TaskId),default);Assert.Contains("time zone",waiting.Message);clock.Now=DateTimeOffset.Parse("2027-01-02T12:00:00Z");var request=new BrieMessage("UTC-04:00",Guid.NewGuid(),TaskId:task.TaskId);var done=await service.ReplyAsync(f.A,request,default);Assert.Equal("completed",done.Status);Assert.Contains("12-31-2026",done.Message);var saved=await repository.GetAsync(f.A,done.TicketId!.Value,default);Assert.Equal(new DateOnly(2026,12,31),saved!.DueDate);Assert.Equal(done,await service.ReplyAsync(f.A,request,default));Assert.Single((await repository.ListAsync(f.A,new(),default)).Items);});
}



