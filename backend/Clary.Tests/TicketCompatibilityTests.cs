using Clary.Api;
using Clary.Api.Directories;
using Xunit;
public class TicketCompatibilityTests {
 [Fact] public async Task LegacyAssignUsesReservedKitAndEditorCanComplete()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var equipment=new EquipmentRepository(f.Database);var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Recipient"),default);var asset=await new InventoryRepository(f.Database).CreateAsync(c,new("C-02","Laptop"),default);var kit=await equipment.SaveKitAsync(c,null,new("Starter",null,true,[new("Computer",null,1)]),default);
  var request=await equipment.CreateRequestAsync(c,new("Welcome",person.Id,null,kit.Id,new(2026,10,14),null),default);
  await new TicketRepository(f.Database).PrepareAsync(c,request.TicketId,default);
  await f.Sql($"UPDATE memberships SET role='Editor' WHERE user_id='{c.UserId}'");
  var assigned=await equipment.ActionAsync(c,request.Id,new("approve-and-assign",request.Revision),default);Assert.Equal(asset.Id,Assert.Single(assigned.AssetIds));
  // Historical assignment can finish unchanged, without asserting cancellation ownership.
  await f.Sql($"UPDATE ticket_asset_links SET owned_revision=NULL WHERE ticket_id='{request.TicketId}'");
  var complete=await equipment.ActionAsync(c,request.Id,new("complete",assigned.Revision),default);Assert.Equal("Completed",complete.State);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM ticket_asset_reservations"));
 });
 [Fact] public async Task LegacyRequestsShareCanonicalTicketAndCancellationReleasesAssignedInventory()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var equipment=new EquipmentRepository(f.Database);var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Recipient"),default);var asset=await new InventoryRepository(f.Database).CreateAsync(c,new("C-01","Laptop"),default);var kit=await equipment.SaveKitAsync(c,null,new("Starter",null,true,[new("Computer",null,1)]),default);
  var request=await equipment.CreateRequestAsync(c,new("Welcome",person.Id,null,kit.Id,new(2026,10,14),null,true,"Onboarding"),default);var tickets=new TicketRepository(f.Database);var ticket=(await tickets.GetAsync(c,request.TicketId,default))!;Assert.Equal("Onboarding",ticket.Type);Assert.Equal(person.Id,ticket.RecipientEmployeeId);Assert.Equal("Assigned",ticket.Status);Assert.Single(ticket.Assets);
  var canceled=await equipment.ActionAsync(c,request.Id,new("cancel",request.Revision),default);Assert.Equal("Cancelled",canceled.State);Assert.Equal("Canceled",(await tickets.GetAsync(c,ticket.Id,default))!.Status);Assert.Equal("Available",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));Assert.Equal(DBNull.Value,await f.Scalar($"SELECT employee_id FROM inventory_assets WHERE id='{asset.Id}'"));
 });
}
