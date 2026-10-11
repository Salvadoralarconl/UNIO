using Clary.Api.Accounts;
namespace Clary.Api.LocalIdentity;
public enum CredentialTokenPurpose { Setup, Reset }
public record LocalProfile(string? Username,string? FirstName,string? LastName,bool PasswordSet);
public record LoginInput(string Username,string Password);
public static class LocalSessionPolicy
{
 public static TimeSpan Lifetime=>TimeSpan.FromHours(8);
}
public record CredentialSetupInput(string Token,string Username,string FirstName,string LastName,string Password,string ConfirmPassword);
public record PasswordChangeInput(string CurrentPassword,string Password,string ConfirmPassword);
public record CredentialLink(Guid UserId,DateTimeOffset ExpiresAt,string Link);
public record LocalAccountInput(string FirstName,string LastName,string? Email,[property:System.Text.Json.Serialization.JsonRequired] CompanyRole Role,[property:System.Text.Json.Serialization.JsonRequired] bool Enabled,int? Revision);
public record LocalCompanyCreateInput(string Name,SubscriptionInput Subscription,LocalAccountInput Administrator);
public record ProvisionedAccount(AccountDto Account,CredentialLink? Setup);
public record ProvisionedCompany(CompanyAdminDto Company,ProvisionedAccount Administrator);
