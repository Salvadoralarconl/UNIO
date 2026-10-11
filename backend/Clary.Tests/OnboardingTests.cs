using Clary.Api;
using Clary.Api.Directories;
using Xunit;
public class OnboardingTests {
 [Fact] public async Task OnboardingUsesOneTicketAndExplicitAvailableAssetsWithAudit()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);
  var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("New starter"),default);
  var location=await new DirectoryRepository(f.Database).SaveLocationAsync(c,null,new("North office"),default);
  var kit=await repo.SaveKitAsync(c,null,new("Starter kit",null,true,[new("Computer",null,1)]),default);
  var inv=new InventoryRepository(f.Database);var a=await inv.CreateAsync(c,new("L1","Chosen laptop"),default);
  var other=await inv.CreateAsync(c,new("L2","Other laptop"),default);
  var r=await repo.CreateRequestAsync(c,new("Onboarding",person.Id,null,kit.Id,new DateOnly(2026,10,20),null,Purpose:"Onboarding",LocationId:location.Id),default);
  Assert.Equal("Onboarding",r.Purpose);Assert.Equal(location.Id,r.LocationId);Assert.Equal("Pending",(await repo.GetTicketAsync(c,r.TicketId,default))!.State);
  r=await repo.ActionAsync(c,r.Id,new("approve-and-assign",r.Revision,[a.Id]),default);
  Assert.Equal(a.Id,Assert.Single(r.AssetIds));var assigned=(await inv.GetAsync(c,a.Id,default))!;Assert.Equal(person.Id,assigned.EmployeeId);Assert.Equal(location.Id,assigned.LocationId);
  Assert.Equal("Available",(await inv.GetAsync(c,other.Id,default))!.Status);
  r=await repo.ActionAsync(c,r.Id,new("complete",r.Revision),default);
  Assert.Equal("Completed",(await repo.GetTicketAsync(c,r.TicketId,default))!.State);
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_tickets"));
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit WHERE action='Assigned' AND resource_type='asset'"));
 });
 [Fact] public async Task WrongOrUnavailableManualSelectionRollsBack()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("New starter"),default);
  var kit=await repo.SaveKitAsync(c,null,new("Starter kit",null,true,[new("Computer",null,1)]),default);
  var printer=await new InventoryRepository(f.Database).CreateAsync(c,new("P1","Printer",Category:"Printer"),default);
  var r=await repo.CreateRequestAsync(c,new("Onboarding",person.Id,null,kit.Id,new DateOnly(2026,10,20),null,Purpose:"Onboarding"),default);
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.ActionAsync(c,r.Id,new("approve-and-assign",r.Revision,[printer.Id]),default));
  Assert.Equal("Pending",(await repo.GetRequestAsync(c,r.Id,default))!.State);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_request_assets"));
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateRequestAsync(c,new("Onboarding",person.Id,null,kit.Id,null,null,Purpose:"Onboarding"),default));
 });
}
