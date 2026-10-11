using Clary.Api.Identity;
namespace Clary.Api;
public static class EquipmentEndpoints {
 public static async Task<IResult> Handle(Func<Task<object?>> work){try{return await work() is {} value?Results.Ok(value):Results.NotFound(new{error="This equipment resource no longer exists."});}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}catch(InventoryConflict e){return Results.Conflict(new{error=e.Message});}catch(InventoryMissing){return Results.NotFound(new{error="This equipment resource no longer exists."});}catch(CompanyDenied){return Results.StatusCode(403);}}
 public static void MapEquipmentEndpoints(this WebApplication app){
  app.MapGet("/api/equipment/kits",(HttpContext h,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.ListKitsAsync(CompanyPolicies.Context(h)!,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read));
  app.MapGet("/api/equipment/kits/{id:guid}",(HttpContext h,Guid id,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.GetKitAsync(CompanyPolicies.Context(h)!,id,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read));
  app.MapPost("/api/equipment/kits",(HttpContext h,EquipmentKitInput input,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.SaveKitAsync(CompanyPolicies.Context(h)!,null,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Write));
  app.MapPut("/api/equipment/kits/{id:guid}",(HttpContext h,Guid id,EquipmentKitInput input,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.SaveKitAsync(CompanyPolicies.Context(h)!,id,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Write));
  app.MapDelete("/api/equipment/kits/{id:guid}",(HttpContext h,Guid id,[Microsoft.AspNetCore.Mvc.FromBody] EquipmentKitDeletion input,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.DeleteKitAsync(CompanyPolicies.Context(h)!,id,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Delete));
  app.MapGet("/api/equipment/requests",(HttpContext h,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.ListRequestsAsync(CompanyPolicies.Context(h)!,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read));
  app.MapGet("/api/equipment/requests/{id:guid}",(HttpContext h,Guid id,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.GetRequestAsync(CompanyPolicies.Context(h)!,id,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read));
  app.MapPost("/api/equipment/requests",(HttpContext h,EquipmentRequestInput input,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.CreateRequestAsync(CompanyPolicies.Context(h)!,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Write));
  app.MapPost("/api/equipment/requests/{id:guid}/actions",(HttpContext h,Guid id,EquipmentAction input,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.ActionAsync(CompanyPolicies.Context(h)!,id,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Write));
  app.MapGet("/api/equipment/tickets/{id:guid}",(HttpContext h,Guid id,EquipmentRepository r,CancellationToken ct)=>Handle(async()=>await r.GetTicketAsync(CompanyPolicies.Context(h)!,id,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read));
 }
}

