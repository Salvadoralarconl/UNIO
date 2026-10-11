using Clary.Api;
using Clary.Api.Identity;
using Xunit;
public class CompanyRepositoryContractTests {
 [Theory]
 [InlineData(typeof(InventoryRepository))]
 [InlineData(typeof(InventoryDepartments))]
 [InlineData(typeof(InventoryDocumentation))]
 [InlineData(typeof(LibraryRepository))]
 [InlineData(typeof(LibraryOperations))]
 [InlineData(typeof(KnowledgeRepository))]
 [InlineData(typeof(UploadService))]
 [InlineData(typeof(SampleLoader))]
 public void BusinessOperationsRequireExplicitCompany(Type type){
  var methods=type.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly).Where(m=>m.Name.EndsWith("Async"));
  Assert.All(methods,m=>Assert.Equal(typeof(CompanyContext),m.GetParameters()[0].ParameterType));
 }
}
