using Clary.Api.Identity;
namespace Clary.Api.Directories;
public static class DirectoryEndpoints
{
    static async Task<IResult> Run<T>(Func<Task<T>> run){try{return Results.Ok(await run());}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}catch(CompanyResourceMissing){return Results.NotFound();}catch(CompanyConflict){return Results.Conflict(new{error="The directory entry changed or conflicts with another entry."});}}
    public static void MapDirectoryEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/api/directory");
        group.MapGet("/employees",(HttpContext h,DirectoryRepository r,string? query,string? cursor,int? limit,CancellationToken ct)=>Run(()=>r.ListEmployeesAsync(CompanyPolicies.Context(h)!,query,cursor,limit??50,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read)).RequireAuthorization(CompanyPolicies.Read);
        group.MapGet("/departments",(HttpContext h,DirectoryRepository r,string? query,string? cursor,int? limit,CancellationToken ct)=>Run(()=>r.ListDepartmentsAsync(CompanyPolicies.Context(h)!,query,cursor,limit??50,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read)).RequireAuthorization(CompanyPolicies.Read);
        group.MapGet("/locations",(HttpContext h,DirectoryRepository r,string? query,string? cursor,int? limit,CancellationToken ct)=>Run(()=>r.ListLocationsAsync(CompanyPolicies.Context(h)!,query,cursor,limit??50,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read)).RequireAuthorization(CompanyPolicies.Read);
        group.MapGet("/mapping-review",(HttpContext h,DirectoryRepository r,CancellationToken ct)=>Run(()=>r.MappingReviewAsync(CompanyPolicies.Context(h)!,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Read)).RequireAuthorization(CompanyPolicies.Read);
        group.MapPost("/employees",(HttpContext h,DirectoryRepository r,EmployeeInput input,CancellationToken ct)=>Run(()=>r.SaveEmployeeAsync(CompanyPolicies.Context(h)!,null,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Write);
        group.MapPost("/departments",(HttpContext h,DirectoryRepository r,DepartmentInput input,CancellationToken ct)=>Run(()=>r.SaveDepartmentAsync(CompanyPolicies.Context(h)!,null,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Write);
        group.MapPost("/locations",(HttpContext h,DirectoryRepository r,LocationInput input,CancellationToken ct)=>Run(()=>r.SaveLocationAsync(CompanyPolicies.Context(h)!,null,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Write);
        group.MapPut("/employees/{id:guid}",(HttpContext h,DirectoryRepository r,Guid id,EmployeeInput input,CancellationToken ct)=>Run(()=>r.SaveEmployeeAsync(CompanyPolicies.Context(h)!,id,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Write);
        group.MapPut("/departments/{id:guid}",(HttpContext h,DirectoryRepository r,Guid id,DepartmentInput input,CancellationToken ct)=>Run(()=>r.SaveDepartmentAsync(CompanyPolicies.Context(h)!,id,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Write);
        group.MapPut("/locations/{id:guid}",(HttpContext h,DirectoryRepository r,Guid id,LocationInput input,CancellationToken ct)=>Run(()=>r.SaveLocationAsync(CompanyPolicies.Context(h)!,id,input,ct))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Write);
    }
}
