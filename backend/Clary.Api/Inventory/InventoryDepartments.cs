using Clary.Api.Identity;
using Clary.Api.Directories;
using Npgsql;
namespace Clary.Api;
public sealed record InventoryDepartment(string Name,string Color);
public sealed class InventoryDepartments(NpgsqlDataSource db)
{
 readonly DirectoryRepository directory=new(db);
 public async Task<IReadOnlyList<InventoryDepartment>> ListAsync(CompanyContext context,CancellationToken ct)
 {
  var result=new List<InventoryDepartment>();string? cursor=null;
  do{var page=await directory.ListDepartmentsAsync(context,null,cursor,100,ct);result.AddRange(page.Items.Select(x=>new InventoryDepartment(x.Name,x.Color)));cursor=page.NextCursor;}while(cursor is not null);
  return result;
 }
 public async Task<InventoryDepartment> SaveAsync(CompanyContext context,string? name,string? color,bool update,CancellationToken ct)
 {
  DepartmentDto? current=null;
  if(update){string? cursor=null;do{var page=await directory.ListDepartmentsAsync(context,name?.Trim(),cursor,100,ct);current=page.Items.FirstOrDefault(x=>string.Equals(x.Name,name?.Trim(),StringComparison.OrdinalIgnoreCase));cursor=page.NextCursor;}while(current is null&&cursor is not null);if(current is null)throw new InventoryMissing();}
  try{var saved=await directory.SaveDepartmentAsync(context,current?.Id,new(name!,color!,current?.Active??true,current?.Revision),ct);return new(saved.Name,saved.Color);}
  catch(CompanyConflict){throw new InventoryConflict("This department already exists or changed.");}
  catch(CompanyResourceMissing){throw new InventoryMissing();}
 }
}
