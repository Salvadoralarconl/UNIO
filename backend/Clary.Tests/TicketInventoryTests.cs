using Clary.Api;
using Clary.Api.Directories;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
public class TicketInventoryTests {
 [Fact] public async Task RepairReturnsToRecipientAndRetirementClearsAssignment()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Recipient"),default);var asset=await new InventoryRepository(f.Database).CreateAsync(c,new("REPAIR-01","Laptop",Status:"Assigned",EmployeeId:person.Id),default);await f.Login();
  async Task<TicketDto> Create(string type,string outcome){var response=await f.Send(HttpMethod.Post,"/api/tickets",new{title=type,type,dueDate="2026-10-14",recipientEmployeeId=person.Id,assetLinks=new[]{new{assetId=asset.Id,outcome}}});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<TicketDto>())!;}
  async Task<TicketDto> Act(TicketDto ticket,string action){var response=await f.Send(HttpMethod.Post,$"/api/tickets/{ticket.Id}/actions",new{action,revision=ticket.Revision});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<TicketDto>())!;}
  var repair=await Act(await Create("Repair","Repair"),"approve");repair=await Act(repair,"start");Assert.Equal("In repair",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));await Act(repair,"complete");Assert.Equal("Assigned",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));Assert.Equal(person.Id,await f.Scalar($"SELECT employee_id FROM inventory_assets WHERE id='{asset.Id}'"));
  var retirement=await Act(await Create("Retirement","Retire"),"approve");await Act(retirement,"complete");Assert.Equal("Retired",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));Assert.Equal(DBNull.Value,await f.Scalar($"SELECT employee_id FROM inventory_assets WHERE id='{asset.Id}'"));
 });
 [Fact] public async Task KitEquipmentIsChosenAndReservedAtCreationBeforeCompletion()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var asset=await new InventoryRepository(f.Database).CreateAsync(c,new("PICK-01","Dell Latitude"),default);
  var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Recipient"),default);
  var kit=await new EquipmentRepository(f.Database).SaveKitAsync(c,null,new("Laptop kit",null,true,[new("Computer",null,1)]),default);await f.Login();
  var response=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Collect laptop",type="Equipment",dueDate="2026-10-14",recipientEmployeeId=person.Id,kitId=kit.Id});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var ticket=(await response.Content.ReadFromJsonAsync<TicketDto>())!;Assert.Equal(asset.Id,Assert.Single(ticket.Assets).Id);
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM ticket_asset_reservations"));Assert.Equal("Available",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));
  var duplicate=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Other recipient",type="Equipment",dueDate="2026-10-14",recipientEmployeeId=person.Id,kitId=kit.Id});Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_tickets"));
 });
 [Fact] public async Task FulfillmentReservesThenAssignsAndCancellationReleasesOnlyItsOwnWork()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var inventory=new InventoryRepository(f.Database);var asset=await inventory.CreateAsync(c,new("T-01","Laptop"),default);var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Recipient"),default);await f.Login();
  async Task<Guid> Create(){var r=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Handover",type="Equipment",dueDate="2026-10-14",recipientEmployeeId=person.Id,assetLinks=new[]{new{assetId=asset.Id,outcome="Assign"}}});Assert.Equal(HttpStatusCode.OK,r.StatusCode);return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
  var id=await Create();Assert.Equal("Available",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM ticket_asset_reservations"));
  var competing=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Competing",type="Equipment",dueDate="2026-10-14",recipientEmployeeId=person.Id,assetLinks=new[]{new{assetId=asset.Id,outcome="Assign"}}});Assert.Equal(HttpStatusCode.Conflict,competing.StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="approve",revision=1})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="complete",revision=2})).StatusCode);
  Assert.Equal("Assigned",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));Assert.Equal(person.Id,await f.Scalar($"SELECT employee_id FROM inventory_assets WHERE id='{asset.Id}'"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM ticket_asset_reservations"));
  Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="complete",revision=2})).StatusCode);
 });
 [Fact] public async Task CancellationReleasesReservationAndReferenceTicketNeverChangesAsset()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var asset=await new InventoryRepository(f.Database).CreateAsync(c,new("R-01","Monitor",Category:"Monitor"),default);var person=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Person"),default);await f.Login();
  var create=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Canceled handover",type="Equipment",dueDate="2026-10-14",recipientEmployeeId=person.Id,assetLinks=new[]{new{assetId=asset.Id,outcome="Assign"}}});Assert.Equal(HttpStatusCode.OK,create.StatusCode);var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="cancel",revision=1})).StatusCode);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM ticket_asset_reservations"));Assert.Equal("Available",await f.Scalar($"SELECT status FROM inventory_assets WHERE id='{asset.Id}'"));
  var reference=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Check monitor",type="Support",dueDate="2026-10-14",assetLinks=new[]{new{assetId=asset.Id,outcome="Reference"}}});Assert.Equal(HttpStatusCode.OK,reference.StatusCode);id=(await reference.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="approve",revision=1});Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="complete",revision=2})).StatusCode);Assert.Equal(1,await f.Scalar($"SELECT revision FROM inventory_assets WHERE id='{asset.Id}'"));
 });
}
