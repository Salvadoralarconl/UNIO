using Clary.Api;
using Xunit;
public class InventoryDocumentationAllTypesTests {
 [Fact] public async Task ProvisionsEveryTypeAndCustomLivePageWithoutDuplicates()=>await IdentityFixture.Run(async f=>{
  var docs=new InventoryDocumentation(f.Database);
  await docs.EnsureAllAsync(InventoryAuditTests.Context,default);
  var custom=await new AssetConfigurationRepository(f.Database).SaveTypeAsync(InventoryAuditTests.Context,null,new("Phones","Box"),default);
  await docs.EnsureAllAsync(InventoryAuditTests.Context,default);
  await docs.EnsureAllAsync(InventoryAuditTests.Context,default);
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_documentation"));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM library_folders WHERE name='Inventory'"));
  await Assert.ThrowsAsync<ArgumentException>(()=>docs.LinkAsync(InventoryAuditTests.Context,null,true,default,custom.Code));
  await new InventoryRepository(f.Database).CreateAsync(InventoryAuditTests.Context,new("PHONE-1","Test phone",Category:custom.Code,AssetTypeId:custom.Id),default);
  await docs.EnsureAllAsync(InventoryAuditTests.Context,default);
  await docs.EnsureAllAsync(InventoryAuditTests.Context,default);
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_documentation"));
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM library_folders WHERE name='Phones'"));
  var state=await docs.GetAsync(InventoryAuditTests.Context,default,custom.Code);
  Assert.NotNull(state.DocumentId);
  Assert.Contains(custom.Code,(await new LibraryOperations(f.Database).ContentAsync(InventoryAuditTests.Context,state.DocumentId!.Value,default))!);
 });
}

