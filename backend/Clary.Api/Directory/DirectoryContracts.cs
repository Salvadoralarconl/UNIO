namespace Clary.Api.Directories;
public record DepartmentDto(Guid Id,string Name,string Color,bool Active,int Revision);
public record EmployeeDto(Guid Id,string DisplayName,string? Email,Guid? DepartmentId,bool Active,int Revision);
public record LocationDto(Guid Id,string Name,Guid? ParentId,bool Active,int Revision);
public record DirectoryPage<T>(IReadOnlyList<T> Items,string? NextCursor);
public record DepartmentInput(string Name,string Color,bool Active=true,int? Revision=null);
public record EmployeeInput(string DisplayName,string? Email=null,Guid? DepartmentId=null,bool Active=true,int? Revision=null);
public record LocationInput(string Name,Guid? ParentId=null,bool Active=true,int? Revision=null);
public record DirectoryMappingReview(Guid AssetId,string Kind,string LegacyText,string Reason);
