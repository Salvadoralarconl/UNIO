using Clary.Api.Identity;
namespace Clary.Api;
public static class AssetConfigurationEndpoints {
 public static void MapAssetConfigurationEndpoints(this WebApplication app){
  const string root="/api/inventory/configuration";
  app.MapGet(root,async(HttpContext http,AssetConfigurationRepository repo,CancellationToken ct)=>await repo.GetAsync(CompanyPolicies.Context(http)!,ct)).WithMetadata(new CompanyPermission(CompanyCapability.OwnInventory));
  async Task<IResult> Type(HttpContext http,Guid? id,AssetTypeInput input,AssetConfigurationRepository repo,CancellationToken ct){try{return Results.Ok(await repo.SaveTypeAsync(CompanyPolicies.Context(http)!,id,input,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}catch(InventoryMissing){return Results.NotFound();}catch(InventoryConflict){return Results.Conflict(new{error="Configuration changed. Reload before editing."});}}
  async Task<IResult> Field(HttpContext http,Guid? id,AssetFieldInput input,AssetConfigurationRepository repo,CancellationToken ct){try{return Results.Ok(await repo.SaveFieldAsync(CompanyPolicies.Context(http)!,id,input,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}catch(InventoryMissing){return Results.NotFound();}catch(InventoryConflict){return Results.Conflict(new{error="Configuration changed. Reload before editing."});}}
  app.MapPost(root+"/asset-types",(HttpContext h,AssetTypeInput i,AssetConfigurationRepository r,CancellationToken ct)=>Type(h,null,i,r,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Settings));
  app.MapPut(root+"/asset-types/{id:guid}",(HttpContext h,Guid id,AssetTypeInput i,AssetConfigurationRepository r,CancellationToken ct)=>Type(h,id,i,r,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Settings));
  app.MapPost(root+"/asset-fields",(HttpContext h,AssetFieldInput i,AssetConfigurationRepository r,CancellationToken ct)=>Field(h,null,i,r,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Settings));
  app.MapPut(root+"/asset-fields/{id:guid}",(HttpContext h,Guid id,AssetFieldInput i,AssetConfigurationRepository r,CancellationToken ct)=>Field(h,id,i,r,ct)).WithMetadata(new CompanyPermission(CompanyCapability.Settings));
 }
}
