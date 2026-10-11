using Clary.Api;
using Xunit;
public class TicketReportTests {
 [Fact] public async Task ReportsCountDistinctTicketsAndKeepSharedWorkSeparate()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new TicketRepository(f.Database);var due=new DateOnly(2026,10,1);
  var named=await repo.SaveAsync(c,null,new("Personal work",null,"Support",due,AssigneeIds:[c.UserId]),default);await repo.SaveAsync(c,null,new("Shared work",null,"Support",due,"All"),default);
  var result=await repo.ReportAsync(c,"User workload",new(Today:new(2026,10,10)),default);Assert.Equal(2,result.Total);Assert.Contains(result.Items,r=>r.Values["User"]=="Everyone (shared)"&&r.Values["Open tickets"]=="1");Assert.Contains(result.Items,r=>r.Id==c.UserId.ToString()&&r.Values["Open tickets"]=="1");
  await repo.ActionAsync(c,named.Id,new("approve",1),default);await repo.ActionAsync(c,named.Id,new("complete",2),default);var overdue=await repo.ReportAsync(c,"Tickets",new(Overdue:true,Today:new(2026,10,10)),default);Assert.Equal(1,overdue.Total);Assert.Equal("Shared work",Assert.Single(overdue.Items).Values["Ticket"]);
 });
}
