namespace Clary.Api.Accounts;
public enum CompanyRole { Administrator, Editor, Viewer }
public enum CompanyVisibilityScope { Company, AssignedOnly }
public record OperatorContext(Guid UserId,Guid? SessionId=null);
public enum PlanKind { Ten, Twenty, Fifty, Hundred, Custom, Unlimited }
public record SubscriptionInput([property:System.Text.Json.Serialization.JsonRequired] PlanKind Kind,int? CustomLimit,int? Revision);
public record SeatUsage(int Enabled,int Pending,int? Limit,int? Remaining,bool OverLimit);
public record AccountDto(Guid Id,string DisplayName,string? Email,CompanyRole Role,bool Enabled,int Revision,DateTimeOffset? LastActivity,bool Online,Clary.Api.LocalIdentity.LocalProfile? LocalProfile=null);
public record CompanyAdminDto(Guid Id,string Name,bool Active,int Revision,PlanKind Plan,int? Limit,SeatUsage Seats,int OnlineUsers,DateTimeOffset? LastActivity,bool NeedsAdministrator);
