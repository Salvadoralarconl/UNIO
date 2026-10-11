using Clary.Api;
using Clary.Api.Identity;
using Npgsql;
using Xunit;
public class CompanyInventoryIsolationTests {
 [Fact] public async Task OtherCompanyIdsAreInvisible()=>await IdentityFixture.Run(async f=>{
  var a=new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);var b=a with{CompanyId=Guid.NewGuid(),UserId=Guid.NewGuid()};await f.Sql($"INSERT INTO companies(id,name) VALUES('{b.CompanyId}','Other')");
  await FixtureCompany.AddActorAsync(f.Database,b);
  var repo=new InventoryRepository(f.Database);var own=await repo.CreateAsync(a,new("SAME","Owned sentinel"),default);var foreign=await repo.CreateAsync(b,new("SAME","Foreign sentinel"),default);
  Assert.Equal(own.Id,Assert.Single(await repo.ListAsync(a,default)).Id);Assert.Null(await repo.GetAsync(a,foreign.Id,default));await Assert.ThrowsAsync<InventoryMissing>(()=>repo.UpdateAsync(a,foreign.Id,new("SAME","Attack",Revision:foreign.Revision),default));Assert.Equal("Foreign sentinel",(await repo.GetAsync(b,foreign.Id,default))!.Computer);
  await f.Login();Assert.Equal(404,(int)(await f.Client.GetAsync("/api/inventory/assets/"+foreign.Id)).StatusCode);Assert.DoesNotContain("Foreign sentinel",await f.Client.GetStringAsync("/api/inventory/assets"));Assert.DoesNotContain("Foreign sentinel",await f.Client.GetStringAsync("/api/inventory/activity"));
  Assert.NotEqual((await new InventoryDocumentation(f.Database).GetAsync(a,default)).DocumentId,(await new InventoryDocumentation(f.Database).GetAsync(b,default)).DocumentId);
 });
 [Fact] public async Task EmployeeOnlySeesOwnAssets()=>await IdentityFixture.Run(async f=>{
  var a=new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);var employee=Guid.NewGuid();var repo=new InventoryRepository(f.Database);
  var own=await repo.CreateAsync(a,new("OWN","Own assigned"),default);var other=await repo.CreateAsync(a,new("OTHER","Other assigned"),default);
  await f.Sql($"INSERT INTO employees(id,company_id,display_name) VALUES('{employee}','{a.CompanyId}','Same name'); UPDATE inventory_assets SET employee_id='{employee}' WHERE id='{own.Id}'; UPDATE memberships SET visibility_scope='AssignedOnly',role='Viewer',employee_id='{employee}' WHERE user_id='{a.UserId}'");
  var person=a with{Role=CompanyRole.Viewer,VisibilityScope=CompanyVisibilityScope.AssignedOnly,EmployeeId=employee};Assert.Equal(own.Id,Assert.Single(await repo.ListAsync(person,default)).Id);Assert.Null(await repo.GetAsync(person,other.Id,default));Assert.Equal(own.Id,Assert.Single(await repo.ActivityAsync(person,50,default)).AssetId);Assert.Empty(await repo.ListAsync(person with{EmployeeId=null},default));
  await f.Login();Assert.Equal(200,(int)(await f.Client.GetAsync("/api/inventory/assets")).StatusCode);Assert.DoesNotContain("Other assigned",await f.Client.GetStringAsync("/api/inventory/assets"));Assert.Equal(404,(int)(await f.Client.GetAsync("/api/inventory/assets/"+other.Id)).StatusCode);Assert.Equal(403,(int)(await f.Client.GetAsync("/api/inventory/documentation")).StatusCode);Assert.Equal(403,(int)(await f.Client.GetAsync("/api/inventory/departments")).StatusCode);
 });
}
