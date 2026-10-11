using Clary.Api;
using Clary.Api.Directories;
using Clary.Api.Identity;
using System.Net;
using System.Net.Http.Json;
using Xunit;
public class EquipmentTests {
 [Fact] public async Task RenamedCustomTypesKeepKitAllocationStable()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var config=new AssetConfigurationRepository(f.Database);var type=await config.SaveTypeAsync(c,null,new("Tools","Wrench"),default);
  var kits=new EquipmentRepository(f.Database);var kit=await kits.SaveKitAsync(c,null,new("Tools kit",null,true,[new(type.Code,null,1)]),default);
  Assert.Equal(type.Id,Assert.Single(kit.Lines).AssetTypeId);Assert.Equal(type.Code,kit.Lines[0].Category);
  var inv=new InventoryRepository(f.Database);var asset=await inv.CreateAsync(c,new("TOOL","Tool",AssetTypeId:type.Id),default);Assert.True((await kits.GetKitAsync(c,kit.Id,default))!.CanFulfill);
  var employee=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);var request=await kits.CreateRequestAsync(c,new("Pending tools",employee.Id,null,kit.Id,null,null),default);
  // A pre-feature request has only the stable category code and arbitrary historical metadata.
  await f.Sql($"UPDATE equipment_requests SET lines=jsonb_build_array((lines->0)-'assetTypeId'||jsonb_build_object('historicalKey','keep')) WHERE id='{request.Id}'");var snapshot=await f.Scalar($"SELECT lines::text FROM equipment_requests WHERE id='{request.Id}'");
  await config.SaveTypeAsync(c,type.Id,new("Renamed tools","Wrench",Active:false,Revision:1),default);
  await inv.UpdateAsync(c,asset.Id,new("TOOL","Tool",Category:type.Code,Revision:1),default);
  var updated=await kits.SaveKitAsync(c,kit.Id,new("Renamed kit",null,true,kit.Lines,1),default);Assert.Equal(type.Id,updated.Lines[0].AssetTypeId);
  await Assert.ThrowsAsync<ArgumentException>(()=>kits.SaveKitAsync(c,null,new("Archived type",null,true,kit.Lines),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>kits.SaveKitAsync(c,kit.Id,new("Changed archived line",null,true,[kit.Lines[0] with{Quantity=2}],2),default));
  var assigned=await kits.ActionAsync(c,request.Id,new("approve-and-assign",1),default);Assert.Equal(asset.Id,Assert.Single(assigned.AssetIds));Assert.Equal(snapshot,await f.Scalar($"SELECT lines::text FROM equipment_requests WHERE id='{request.Id}'"));
 });
 [Fact] public async Task KitDeleteRequiresDeletePermissionRevisionAndPreservesLinkedRequests()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var k=await repo.SaveKitAsync(c,null,new("Delete me",null,true,[new("Computer",null,1)]),default);await f.Login();
  Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Delete,$"/api/equipment/kits/{k.Id}",new{revision=99})).StatusCode);
  await f.Sql($"UPDATE memberships SET role='Editor' WHERE user_id='{c.UserId}'");Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Delete,$"/api/equipment/kits/{k.Id}",new{revision=1})).StatusCode);await Assert.ThrowsAsync<CompanyDenied>(()=>repo.DeleteKitAsync(c,k.Id,new(1),default));
  await f.Sql($"UPDATE memberships SET role='Administrator' WHERE user_id='{c.UserId}'");Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Delete,$"/api/equipment/kits/{k.Id}",new{revision=1})).StatusCode);Assert.Null(await repo.GetKitAsync(c,k.Id,default));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit WHERE action='Deleted' AND resource_type='equipment-kit'"));
  var linked=await repo.SaveKitAsync(c,null,new("Keep history",null,true,[new("Computer",null,1)]),default);var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);var request=await repo.CreateRequestAsync(c,new("History",e.Id,null,linked.Id,null,null),default);
  Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Delete,$"/api/equipment/kits/{linked.Id}",new{revision=1})).StatusCode);Assert.NotNull(await repo.GetRequestAsync(c,request.Id,default));Assert.NotNull(await repo.GetKitAsync(c,linked.Id,default));
 });
 [Fact] public async Task KitDeleteTenantCsrfAndAuditRollback()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var kit=await repo.SaveKitAsync(c,null,new("Private",null,true,[new("Computer",null,1)]),default);
  var foreign=c with{CompanyId=Guid.NewGuid(),UserId=Guid.NewGuid()};await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign.CompanyId}','Other')");await FixtureCompany.AddActorAsync(f.Database,foreign);
  await Assert.ThrowsAsync<InventoryMissing>(()=>repo.DeleteKitAsync(foreign,kit.Id,new(1),default));await f.Login();using var withoutCsrf=new HttpRequestMessage(HttpMethod.Delete,$"/api/equipment/kits/{kit.Id}"){Content=JsonContent.Create(new{revision=1})};Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.SendAsync(withoutCsrf)).StatusCode);
  await f.Sql("CREATE FUNCTION fail_kit_delete_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action='Deleted' AND NEW.resource_type='equipment-kit' THEN RAISE EXCEPTION 'forced audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_kit_delete_audit BEFORE INSERT ON inventory_audit FOR EACH ROW EXECUTE FUNCTION fail_kit_delete_audit();");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>repo.DeleteKitAsync(c,kit.Id,new(1),default));Assert.NotNull(await repo.GetKitAsync(c,kit.Id,default));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_audit WHERE action='Deleted'"));
 });
 static async Task Blocked(IdentityFixture f,int pid,int count){for(var i=0;i<200;i++){await using var q=f.Database.CreateCommand("WITH RECURSIVE waits AS (SELECT pid FROM pg_stat_activity WHERE @pid=ANY(pg_blocking_pids(pid)) UNION SELECT a.pid FROM pg_stat_activity a JOIN waits w ON w.pid=ANY(pg_blocking_pids(a.pid))) SELECT count(DISTINCT pid) FROM waits");q.Parameters.AddWithValue("pid",pid);if((long)(await q.ExecuteScalarAsync())!>=count)return;await Task.Delay(10);}Assert.Fail("Equipment mutation must reach the production company lock.");}
 [Fact] public async Task InventoryEditAndEquipmentAllocationContendForTheSameAsset()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var dirs=new DirectoryRepository(f.Database);var e=await dirs.SaveEmployeeAsync(c,null,new("Request employee"),default);var other=await dirs.SaveEmployeeAsync(c,null,new("Inventory employee"),default);var repo=new EquipmentRepository(f.Database);var inv=new InventoryRepository(f.Database);var asset=await inv.CreateAsync(c,new("LAST","PC"),default);var kit=await repo.SaveKitAsync(c,null,new("One",null,true,[new("Computer",null,1)]),default);var request=await repo.CreateRequestAsync(c,new("Request",e.Id,null,kit.Id,null,null),default);
  await using var conn=await f.Database.OpenConnectionAsync();await using var tx=await conn.BeginTransactionAsync();await using(var q=new Npgsql.NpgsqlCommand("SELECT id FROM companies FOR UPDATE",conn,tx))await q.ExecuteScalarAsync();
  async Task<string> Assign(){try{await repo.ActionAsync(c,request.Id,new("approve-and-assign",1),default);return "equipment";}catch(InventoryConflict){return "conflict";}}
  async Task<string> Edit(){try{await inv.UpdateAsync(c,asset.Id,new("LAST","PC",Status:"Assigned",Revision:1,EmployeeId:other.Id),default);return "inventory";}catch(InventoryConflict){return "conflict";}}
  var assignment=Assign();var editing=Edit();await Blocked(f,conn.ProcessID,2);await tx.CommitAsync();var results=await Task.WhenAll(assignment,editing);var winner=Assert.Single(results,x=>x!="conflict");Assert.Single(results,x=>x=="conflict");
  var current=(await inv.GetAsync(c,asset.Id,default))!;Assert.Equal(winner=="equipment"?e.Id:other.Id,current.EmployeeId);Assert.Equal(2,current.Revision);
  var after=(await repo.GetRequestAsync(c,request.Id,default))!;Assert.Equal(winner=="equipment"?"Ready":"Pending",after.State);Assert.Equal(winner=="equipment"?1:0,after.AssetIds.Count);
 });
 [Fact] public async Task RevokedSessionAfterCompanyLockWaitCannotAllocate()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);var k=await repo.SaveKitAsync(c,null,new("One",null,true,[new("Computer",null,1)]),default);await new InventoryRepository(f.Database).CreateAsync(c,new("PC","PC"),default);var request=await repo.CreateRequestAsync(c,new("Request",e.Id,null,k.Id,null,null),default);
  await f.Login();var session=(Guid)(await f.Scalar("SELECT id FROM account_sessions LIMIT 1"))!;var browser=c with{SessionId=session};
  await using var conn=await f.Database.OpenConnectionAsync();await using var tx=await conn.BeginTransactionAsync();await using(var q=new Npgsql.NpgsqlCommand("SELECT id FROM companies FOR UPDATE",conn,tx))await q.ExecuteScalarAsync();
  var mutation=repo.ActionAsync(browser,request.Id,new("approve-and-assign",1),default);await Blocked(f,conn.ProcessID,1);
  await using(var q=new Npgsql.NpgsqlCommand("UPDATE account_sessions SET revoked_at=clock_timestamp() WHERE id=@id",conn,tx)){q.Parameters.AddWithValue("id",session);await q.ExecuteNonQueryAsync();}await tx.CommitAsync();await Assert.ThrowsAsync<CompanyDenied>(()=>mutation);
  Assert.Equal("Pending",(await repo.GetRequestAsync(c,request.Id,default))!.State);Assert.Equal("Pending",(await repo.GetTicketAsync(c,request.TicketId,default))!.State);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_request_assets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_assets WHERE status='Available' AND revision=1"));
 });
 [Fact] public async Task ArchivedEmployeeOrDepartmentBeforeAssignmentRollsBack()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var dirs=new DirectoryRepository(f.Database);var e=await dirs.SaveEmployeeAsync(c,null,new("Employee"),default);var d=await dirs.SaveDepartmentAsync(c,null,new("IT","#123456"),default);var repo=new EquipmentRepository(f.Database);var k=await repo.SaveKitAsync(c,null,new("One",d.Id,true,[new("Computer",null,1)]),default);await new InventoryRepository(f.Database).CreateAsync(c,new("PC","PC"),default);var request=await repo.CreateRequestAsync(c,new("Request",e.Id,d.Id,k.Id,null,null),default);
  var archived=await dirs.SaveEmployeeAsync(c,e.Id,new("Employee",Active:false,Revision:e.Revision),default);await Assert.ThrowsAsync<ArgumentException>(()=>repo.ActionAsync(c,request.Id,new("approve-and-assign",1),default));
  await dirs.SaveEmployeeAsync(c,e.Id,new("Employee",Active:true,Revision:archived.Revision),default);await dirs.SaveDepartmentAsync(c,d.Id,new("IT","#123456",Active:false,Revision:d.Revision),default);await Assert.ThrowsAsync<ArgumentException>(()=>repo.ActionAsync(c,request.Id,new("approve-and-assign",1),default));
  Assert.Equal("Pending",(await repo.GetRequestAsync(c,request.Id,default))!.State);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_request_assets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_assets WHERE status='Available' AND revision=1"));
 });
 [Fact] public async Task RevisionsArchivedKitsDepartmentsSessionsAndTenantForeignKeys()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var dirs=new DirectoryRepository(f.Database);
  var e=await dirs.SaveEmployeeAsync(c,null,new("Employee"),default);var d=await dirs.SaveDepartmentAsync(c,null,new("IT","#123456"),default);
  var k=await repo.SaveKitAsync(c,null,new("Scoped",d.Id,true,[new("Computer",null,1)]),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateRequestAsync(c,new("Mismatch",e.Id,null,k.Id,null,null),default));
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.SaveKitAsync(c,k.Id,new("Stale",d.Id,true,k.Lines,99),default));
  await Assert.ThrowsAsync<CompanyDenied>(()=>repo.SaveKitAsync(c with{SessionId=Guid.NewGuid()},null,new("Invalid session",null,true,k.Lines),default));
  var a=await repo.CreateRequestAsync(c,new("Approved",e.Id,d.Id,k.Id,null,null),default);var approved=await repo.ActionAsync(c,a.Id,new("approve",1),default);Assert.Equal("Approved",approved.State);
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.ActionAsync(c,a.Id,new("approve",approved.Revision),default));
  var cancelled=await repo.ActionAsync(c,a.Id,new("cancel",approved.Revision),default);Assert.Equal("Cancelled",cancelled.State);
  await repo.SaveKitAsync(c,k.Id,new("Scoped",d.Id,false,k.Lines,k.Revision),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateRequestAsync(c,new("Archived",e.Id,d.Id,k.Id,null,null),default));
  var other=Guid.NewGuid();var employee=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{other}','Other'); INSERT INTO employees(id,company_id,display_name) VALUES('{employee}','{other}','Foreign')");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Sql($"UPDATE equipment_requests SET employee_id='{employee}' WHERE id='{a.Id}'"));
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateRequestAsync(c,new("Foreign",employee,d.Id,k.Id,null,null),default));
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Sql($"DELETE FROM inventory_audit WHERE resource_id='{k.Id}'"));
 });
 [Fact] public async Task DuplicateLinesAndOverlappingStockAreHandled()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveKitAsync(c,null,new("Duplicate",null,true,[new("Computer"," laptop ",1),new("Computer","LAPTOP",1)]),default));
  var inv=new InventoryRepository(f.Database);await inv.CreateAsync(c,new("PC1","PC",CustomCategory:" laptop "),default);
  var k=await repo.SaveKitAsync(c,null,new("Overlap",null,true,[new("Computer",null,1),new("Computer","LAPTOP",1)]),default);
  Assert.False(k.CanFulfill);Assert.Equal(0,k.FulfillableCount);Assert.All(k.Availability,a=>Assert.Equal(1,a.Available));
  await inv.CreateAsync(c,new("PC2","PC",CustomCategory:"Desktop"),default);
  Assert.True((await repo.GetKitAsync(c,k.Id,default))!.CanFulfill);Assert.Equal(1,(await repo.GetKitAsync(c,k.Id,default))!.FulfillableCount);
  var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);
  var ready=await repo.CreateRequestAsync(c,new("Immediate",e.Id,null,k.Id,null,null,true),default);Assert.Equal(2,ready.AssetIds.Count);
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.CreateRequestAsync(c,new("No stock",e.Id,null,k.Id,null,null,true),default));
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_requests"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_tickets"));
 });
 [Fact] public async Task ConcurrentLastAssetAndStaleActionsAreProtected()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);
  var k=await repo.SaveKitAsync(c,null,new("One",null,true,[new("Computer",null,1)]),default);
  await new InventoryRepository(f.Database).CreateAsync(c,new("LAST","PC"),default);
  var a=await repo.CreateRequestAsync(c,new("A",e.Id,null,k.Id,null,null),default);var b=await repo.CreateRequestAsync(c,new("B",e.Id,null,k.Id,null,null),default);
  async Task<EquipmentRequest?> Attempt(EquipmentRequest request){try{return await repo.ActionAsync(c,request.Id,new("approve-and-assign",1),default);}catch(InventoryConflict){return null;}}
  var results=await Task.WhenAll(Attempt(a),Attempt(b));var winner=Assert.Single(results,r=>r is not null)!;
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM equipment_request_assets"));
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.ActionAsync(c,winner.Id,new("complete",1),default));
  var loser=await repo.GetRequestAsync(c,results[0] is null?a.Id:b.Id,default);Assert.Equal("Pending",loser!.State);
  var cancelled=await repo.ActionAsync(c,loser.Id,new("cancel",1),default);Assert.Equal("Cancelled",(await repo.GetTicketAsync(c,cancelled.TicketId,default))!.State);
 });
 [Fact] public async Task AuditFailureRollsBackAllocationAndCreation()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);var k=await repo.SaveKitAsync(c,null,new("One",null,true,[new("Computer",null,1)]),default);await new InventoryRepository(f.Database).CreateAsync(c,new("PC","PC"),default);
  await f.Sql("CREATE FUNCTION fail_equipment_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.resource_type='equipment-request' THEN RAISE EXCEPTION 'forced audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_equipment_audit BEFORE INSERT ON inventory_audit FOR EACH ROW EXECUTE FUNCTION fail_equipment_audit();");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>repo.CreateRequestAsync(c,new("Immediate",e.Id,null,k.Id,null,null,true),default));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_requests"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_tickets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_assets WHERE status='Available' AND employee_id IS NULL"));
  await f.Sql("DROP TRIGGER fail_equipment_audit ON inventory_audit");
  var pending=await repo.CreateRequestAsync(c,new("Pending",e.Id,null,k.Id,null,null),default);
  await f.Sql("CREATE OR REPLACE FUNCTION fail_equipment_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.resource_type='asset' THEN RAISE EXCEPTION 'forced asset audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_equipment_audit BEFORE INSERT ON inventory_audit FOR EACH ROW EXECUTE FUNCTION fail_equipment_audit();");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>repo.ActionAsync(c,pending.Id,new("approve-and-assign",1),default));
  Assert.Equal("Pending",(await repo.GetRequestAsync(c,pending.Id,default))!.State);Assert.Equal("Pending",(await repo.GetTicketAsync(c,pending.TicketId,default))!.State);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM equipment_request_assets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_activity"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_assets WHERE status='Available' AND revision=1"));
 });
 [Fact] public async Task HttpTenantCapabilitiesCsrfAndDirectoryIdsAreEnforced()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);var k=await repo.SaveKitAsync(c,null,new("Private",null,true,[new("Computer",null,1)]),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateRequestAsync(c,new("Wrong identity",c.UserId,null,k.Id,null,null),default));
  var request=await repo.CreateRequestAsync(c,new("Request",e.Id,null,k.Id,null,null),default);
  var foreign=c with{CompanyId=Guid.NewGuid(),UserId=Guid.NewGuid()};await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign.CompanyId}','Other')");await FixtureCompany.AddActorAsync(f.Database,foreign);
  Assert.Null(await repo.GetKitAsync(foreign,k.Id,default));Assert.Null(await repo.GetRequestAsync(foreign,request.Id,default));Assert.Null(await repo.GetTicketAsync(foreign,request.TicketId,default));
  await f.Login();Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PostAsJsonAsync("/api/equipment/requests",new EquipmentRequestInput("CSRF",e.Id,null,k.Id,null,null))).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,$"/api/equipment/requests/{request.Id}/actions",new EquipmentAction("assign",1))).StatusCode);
  await f.Sql($"UPDATE memberships SET role='Viewer' WHERE user_id='{c.UserId}'");
  Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/equipment/kits")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/equipment/kits",new EquipmentKitInput("Denied",null,true,[new("Computer",null,1)]))).StatusCode);
  await Assert.ThrowsAsync<CompanyDenied>(()=>repo.SaveKitAsync(c,null,new("Revoked",null,true,[new("Computer",null,1)]),default));
  await f.Sql($"UPDATE memberships SET visibility_scope='AssignedOnly' WHERE user_id='{c.UserId}'");
  foreach(var path in new[]{"/api/equipment/kits","/api/equipment/requests","/api/equipment/tickets/"+request.TicketId})Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync(path)).StatusCode);
 });
 [Fact] public async Task AtomicAllocationSnapshotsAndTicketStates()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new EquipmentRepository(f.Database);
  var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Employee"),default);
  var kit=await repo.SaveKitAsync(c,null,new("Starter",null,true,[new("Computer",null,1)]),default);
  var request=await repo.CreateRequestAsync(c,new("Onboarding",e.Id,null,kit.Id,null,null),default);
  await repo.SaveKitAsync(c,kit.Id,new("Edited",null,true,[new("Printer",null,1)],kit.Revision),default);
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.ActionAsync(c,request.Id,new("approve-and-assign",request.Revision),default));
  Assert.Equal("Pending",(await repo.GetRequestAsync(c,request.Id,default))!.State);
  var asset=await new InventoryRepository(f.Database).CreateAsync(c,new("PC","PC"),default);
  var ready=await repo.ActionAsync(c,request.Id,new("approve-and-assign",request.Revision),default);
  Assert.Equal("Computer",Assert.Single(ready.Lines).Category);Assert.Equal(asset.Id,Assert.Single(ready.AssetIds));
  Assert.Equal("Ready",(await repo.GetTicketAsync(c,ready.TicketId,default))!.State);
  var done=await repo.ActionAsync(c,ready.Id,new("complete",ready.Revision),default);
  Assert.Equal("Completed",(await repo.GetTicketAsync(c,done.TicketId,default))!.State);
 });
}



