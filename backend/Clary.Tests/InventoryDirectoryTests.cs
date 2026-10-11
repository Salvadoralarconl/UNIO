using Clary.Api;
using Clary.Api.Directories;
using Xunit;
public class InventoryDirectoryTests {
 [Fact] public async Task ForeignDirectoryIdsAreRejected()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var other=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{other}','Other')");
  var foreign=c with{CompanyId=other,UserId=Guid.NewGuid()};await FixtureCompany.AddActorAsync(f.Database,foreign);
  var dirs=new DirectoryRepository(f.Database);var e=await dirs.SaveEmployeeAsync(foreign,null,new("Foreign"),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>new InventoryRepository(f.Database).CreateAsync(c,new("A","Computer",EmployeeId:e.Id),default));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_assets"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
 });
 [Fact] public async Task InactiveUnchangedAndExplicitClearAreSupported()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var dirs=new DirectoryRepository(f.Database);var e=await dirs.SaveEmployeeAsync(c,null,new("Alex"),default);
  var repo=new InventoryRepository(f.Database);var a=await repo.CreateAsync(c,new("A","Computer",EmployeeId:e.Id),default);
  await dirs.SaveEmployeeAsync(c,e.Id,new("Alex",Active:false,Revision:e.Revision),default);
  var b=await repo.UpdateAsync(c,a.Id,new("A","Edited",Revision:a.Revision,EmployeeId:e.Id),default);Assert.Equal(e.Id,b.EmployeeId);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateAsync(c,new("B","Computer",EmployeeId:e.Id),default));
  var cleared=await repo.UpdateAsync(c,a.Id,new("A","Edited",Revision:b.Revision,ClearEmployee:true),default);Assert.Null(cleared.EmployeeId);Assert.Null(cleared.Assignee);
 });
 [Fact] public async Task TextOnlyAssignmentsAreRejectedAndLegacySnapshotsPreserved()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var repo=new InventoryRepository(f.Database);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateAsync(c,new("A","Computer",Assignee:"New person"),default));
  var a=await repo.CreateAsync(c,new("A","Computer"),default);await f.Sql($"UPDATE inventory_assets SET assignee='Legacy person',department='Legacy department',location='Legacy location' WHERE id='{a.Id}'");
  var b=await repo.UpdateAsync(c,a.Id,new("A","Edited",Revision:a.Revision),default);Assert.Equal("Legacy person",b.Assignee);Assert.Equal("Legacy department",b.Department);Assert.Equal("Legacy location",b.Location);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.UpdateAsync(c,a.Id,new("A","Edited",Department:"Changed",Revision:b.Revision),default));
  var e=await new DirectoryRepository(f.Database).SaveEmployeeAsync(c,null,new("Alex"),default);var linked=await repo.UpdateAsync(c,a.Id,new("A","Edited",EmployeeId:e.Id,Revision:b.Revision),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.UpdateAsync(c,a.Id,new("A","Edited",Assignee:"Changed",Revision:linked.Revision),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.UpdateAsync(c,a.Id,new("A","Edited",ClearEmployee:true,EmployeeId:e.Id,Revision:linked.Revision),default));
 });
 [Fact] public async Task ResolvingMappingReviewAndDirectoryReferencesIsAtomic()=>await IdentityFixture.Run(async f=>{
  var c=InventoryAuditTests.Context;var dirs=new DirectoryRepository(f.Database);var d=await dirs.SaveDepartmentAsync(c,null,new("IT","#123456"),default);var l=await dirs.SaveLocationAsync(c,null,new("HQ"),default);var e=await dirs.SaveEmployeeAsync(c,null,new("Alex"),default);
  var repo=new InventoryRepository(f.Database);var a=await repo.CreateAsync(c,new("A","Computer"),default);await f.Sql($"INSERT INTO directory_mapping_review(company_id,asset_id,kind,legacy_text,reason) VALUES('{c.CompanyId}','{a.Id}','employee','Legacy','Test'),('{c.CompanyId}','{a.Id}','department','Legacy','Test'),('{c.CompanyId}','{a.Id}','location','Legacy','Test')");
  await Assert.ThrowsAsync<InventoryConflict>(()=>repo.UpdateAsync(c,a.Id,new("A","Computer",Revision:99,EmployeeId:e.Id,DepartmentId:d.Id,LocationId:l.Id),default));Assert.Equal(3L,await f.Scalar("SELECT count(*) FROM directory_mapping_review"));
  var b=await repo.UpdateAsync(c,a.Id,new("A","Computer",Revision:a.Revision,EmployeeId:e.Id,DepartmentId:d.Id,LocationId:l.Id),default);Assert.Equal(e.Id,b.EmployeeId);Assert.Equal(d.Id,b.DepartmentId);Assert.Equal(l.Id,b.LocationId);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM directory_mapping_review"));
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateAsync(c,new("B","Computer",DepartmentId:Guid.NewGuid()),default));await Assert.ThrowsAsync<ArgumentException>(()=>repo.CreateAsync(c,new("B","Computer",LocationId:Guid.NewGuid()),default));
 });
 [Fact] public async Task UnresolvedLongLegacyTextCanBeCarriedUnchanged()=>await IdentityFixture.Run(async f=>{
  var repo=new InventoryRepository(f.Database);var a=await repo.CreateAsync(InventoryAuditTests.Context,new("A","Computer"),default);var legacy=new string('x',250);await f.Sql($"UPDATE inventory_assets SET assignee='{legacy}' WHERE id='{a.Id}'");
  var b=await repo.UpdateAsync(InventoryAuditTests.Context,a.Id,new("A","Edited",Assignee:legacy,Revision:a.Revision),default);Assert.Equal(legacy,b.Assignee);Assert.Null(b.EmployeeId);
 });
}
