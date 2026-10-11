using Clary.Api;
using Clary.Api.Directories;
using Clary.Api.Identity;
using Xunit;
public class InventoryAuditTests {
 internal static CompanyContext Context=>new(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);
 [Fact] public async Task AssetAndAuditCommitTogether()=>await IdentityFixture.Run(async f=>{
  var asset=await new InventoryRepository(f.Database).CreateAsync(Context,new("A","Computer",NewAssigneeName:"New person"),default);
  Assert.NotNull(asset.EmployeeId);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM employees"));
  var history=await new AuditRepository(f.Database).AssetHistoryAsync(Context,asset.Id,null,50,default);Assert.Single(history.Items);Assert.Equal(Context.UserId,history.Items[0].ActorUserId);Assert.Equal("Bootstrap",history.Items[0].ActorDisplayName);
 });
 [Fact] public async Task StaleAssetDoesNotCreateEmployeeOrAudit()=>await IdentityFixture.Run(async f=>{
  var repo=new InventoryRepository(f.Database);var a=await repo.CreateAsync(Context,new("A","Computer"),default);
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.UpdateAsync(Context,a.Id,new("A","Computer",Revision:99,NewAssigneeName:"Ghost"),default));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM employees"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
 });
 [Fact] public async Task DuplicateAssigneeRequiresExplicitChoice()=>await IdentityFixture.Run(async f=>{
  var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(Context,null,new("Alex"),default);
  var repo=new InventoryRepository(f.Database);await Assert.ThrowsAsync<InventoryConflict>(()=>repo.CreateAsync(Context,new("A","Computer",NewAssigneeName:" alex "),default));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_assets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
  Assert.Equal(e.Id,(await repo.CreateAsync(Context,new("A","Computer",EmployeeId:e.Id),default)).EmployeeId);
 });
 [Fact] public async Task RenamingDepartmentDoesNotRewriteAudit()=>await IdentityFixture.Run(async f=>{
  var dirs=new DirectoryRepository(f.Database);var d=await dirs.SaveDepartmentAsync(Context,null,new("Old","#123456"),default);
  var repo=new InventoryRepository(f.Database);var a=await repo.CreateAsync(Context,new("A","Computer",DepartmentId:d.Id),default);
  var before=await f.Scalar($"SELECT after_snapshot::text FROM inventory_audit WHERE resource_id='{a.Id}'");
  await dirs.SaveDepartmentAsync(Context,d.Id,new("New",d.Color,Revision:d.Revision),default);
  Assert.Equal("New",(await repo.GetAsync(Context,a.Id,default))!.Department);Assert.Equal(before,await f.Scalar($"SELECT after_snapshot::text FROM inventory_audit WHERE resource_id='{a.Id}'"));
 });
 [Fact] public async Task AuditFailureRollsBackAssetAndEmployee()=>await IdentityFixture.Run(async f=>{
  var c=Context;
  await f.Sql("CREATE FUNCTION fail_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test audit failure'; END $$; CREATE TRIGGER fail_audit BEFORE INSERT ON inventory_audit FOR EACH ROW EXECUTE FUNCTION fail_audit()");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>new InventoryRepository(f.Database).CreateAsync(c,new("A","Computer",NewAssigneeName:"Ghost"),default));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM employees"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_assets"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
 });
 [Fact] public async Task AssetHistoryScopesAndPaginatesWithoutMutation()=>await IdentityFixture.Run(async f=>{
  var repo=new InventoryRepository(f.Database);var a=await repo.CreateAsync(Context,new("A","Computer",NewAssigneeName:"Alex"),default);var b=await repo.UpdateAsync(Context,a.Id,new("A","Edited",Revision:a.Revision),default);
  var audit=new AuditRepository(f.Database);var first=await audit.AssetHistoryAsync(Context,a.Id,null,1,default);Assert.Single(first.Items);Assert.NotNull(first.NextCursor);var last=await audit.AssetHistoryAsync(Context,a.Id,first.NextCursor,1,default);Assert.Single(last.Items);Assert.Null(last.NextCursor);Assert.NotEqual(first.Items[0].Id,last.Items[0].Id);
  await Assert.ThrowsAsync<ArgumentException>(()=>audit.AssetHistoryAsync(Context,Guid.NewGuid(),first.NextCursor,1,default));
  await Assert.ThrowsAsync<InventoryMissing>(()=>audit.AssetHistoryAsync(Context with{CompanyId=Guid.NewGuid()},a.Id,null,50,default));
  await Assert.ThrowsAsync<InventoryMissing>(()=>audit.AssetHistoryAsync(Context with{Role=CompanyRole.Viewer,VisibilityScope=CompanyVisibilityScope.AssignedOnly,EmployeeId=Guid.NewGuid()},a.Id,null,50,default));
  Assert.Equal(2,(await audit.AssetHistoryAsync(Context with{Role=CompanyRole.Viewer,VisibilityScope=CompanyVisibilityScope.AssignedOnly,EmployeeId=a.EmployeeId},a.Id,null,50,default)).Items.Count);
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Sql("UPDATE inventory_audit SET action='Tampered'"));await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Sql("DELETE FROM inventory_audit"));
  await f.Login();Assert.Equal(200,(int)(await f.Client.GetAsync($"/api/inventory/assets/{a.Id}/history?limit=1")).StatusCode);
  await f.Sql($"UPDATE memberships SET visibility_scope='AssignedOnly',role='Viewer',employee_id='{a.EmployeeId}' WHERE user_id='{Context.UserId}'");Assert.Equal(200,(int)(await f.Client.GetAsync($"/api/inventory/assets/{a.Id}/history")).StatusCode);
 });
 [Fact] public async Task UniqueAssetConflictRollsBackNewAssigneeAndAudit()=>await IdentityFixture.Run(async f=>{
  var repo=new InventoryRepository(f.Database);await repo.CreateAsync(Context,new("A","Computer"),default);
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.CreateAsync(Context,new("A","Computer",NewAssigneeName:"Ghost"),default));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM employees"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_assets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
 });
 [Fact] public async Task CompanyAndMembershipMutationsAreAuditedInTheirTransaction()=>await IdentityFixture.Run(async f=>{
  var companies=new CompanyRepository(f.Database);await companies.UpdateAsync(Context,new("Renamed",1),default);
  var target=Context with{UserId=Guid.NewGuid(),Role=CompanyRole.Viewer};await FixtureCompany.AddActorAsync(f.Database,target);
  await f.Sql($"UPDATE users SET issuer='test',identity_subject='person' WHERE id='{target.UserId}'");
  await companies.SaveAsync(Context,new("person","test","Editor",true,null,1),true,default);
  Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM inventory_audit"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit WHERE resource_type='membership'"));
  await Assert.ThrowsAsync<CompanyConflict>(()=>companies.UpdateAsync(Context,new("Stale",1),default));Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
 });
}

