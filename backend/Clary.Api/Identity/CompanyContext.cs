global using Clary.Api.Accounts;
namespace Clary.Api.Identity;
public record CompanyContext(Guid CompanyId, Guid UserId, CompanyRole Role, Guid? EmployeeId, Guid? DepartmentId, CompanyVisibilityScope VisibilityScope=CompanyVisibilityScope.Company,Guid? SessionId=null);
