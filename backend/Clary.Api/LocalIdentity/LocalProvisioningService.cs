using Clary.Api.Identity;
using Npgsql;
namespace Clary.Api.LocalIdentity;
public record LocalAdministratorInput(LocalAccountInput? Account,int? Revision,Guid? UserId=null,int? AccountRevision=null);
public sealed class LocalProvisioningService(NpgsqlDataSource database,CompanyAccountRepository accounts,LocalCredentialRepository credentials,LocalCredentialTokens tokens)
{
 private static AccountInput Input(LocalAccountInput input)
 {LocalCredentialRepository.ValidateNames(input.FirstName,input.LastName);return new(LocalCredentialRepository.Issuer,Guid.NewGuid().ToString("D"),input.FirstName+" "+input.LastName,input.Email,input.Role,input.Enabled,input.Revision);}
 private async Task<CredentialLink?> Provision(NpgsqlConnection c,NpgsqlTransaction tx,AccountDto account,LocalAccountInput input,Guid actor,Guid company,CancellationToken ct)
 {
  await credentials.CreateUnsetAsync(c,tx,account.Id,ct);
  await using(var q=new NpgsqlCommand("UPDATE local_credentials SET first_name=@first,last_name=@last WHERE user_id=@user",c,tx)){q.Parameters.AddWithValue("first",input.FirstName);q.Parameters.AddWithValue("last",input.LastName);q.Parameters.AddWithValue("user",account.Id);await q.ExecuteNonQueryAsync(ct);}
  if(!account.Enabled)return null;
  var link=await tokens.IssueAsync(c,tx,account.Id,CredentialTokenPurpose.Setup,ct);await AccountAuditRepository.AppendAsync(c,tx,actor,company,"CredentialSetupIssued",account.Id,null,new{link.UserId,link.ExpiresAt},ct);return link;
 }
 public async Task<ProvisionedAccount> CreateAsync(AccountActor actor,Guid company,LocalAccountInput input,CancellationToken ct)
 {
  CredentialLink? setup=null;var result=await accounts.CreateAccountAsync(actor,company,Input(input),ct,async(c,tx,a,token)=>{setup=await Provision(c,tx,a,input,actor.UserId,company,token);await FreshActor(c,tx,actor,a.Id,token);});return new(result,setup!);
 }
 public async Task<ProvisionedCompany> CreateCompanyAsync(OperatorContext owner,LocalCompanyCreateInput input,CancellationToken ct)
 {
  if(input.Administrator is null)throw new ArgumentException("Administrator is required.");CredentialLink? setup=null;AccountDto? admin=null;var identity=Input(input.Administrator);var company=await accounts.CreateAsync(owner,new(input.Name,input.Subscription,identity),ct,async(c,tx,a,token)=>{admin=a;Guid company;await using(var q=new NpgsqlCommand("SELECT company_id FROM memberships WHERE user_id=@user",c,tx)){q.Parameters.AddWithValue("user",a.Id);company=(Guid)(await q.ExecuteScalarAsync(token))!;}setup=await Provision(c,tx,a,input.Administrator,owner.UserId,company,token);admin=await CompanyAccountRepository.AccountAsync(c,tx,company,a.Id,await SeatAccounting.TimeAsync(c,tx,token),token);await FreshActor(c,tx,AccountActor.For(owner),a.Id,token);});return new(company,new(admin!,setup!));
 }
 public Task<AccountDto> UpdateAsync(AccountActor actor,Guid company,Guid user,LocalAccountInput input,CancellationToken ct)
 {
  return accounts.UpdateAccountAsync(actor,company,user,Input(input),ct,async(c,tx,a,token)=>{await using var q=new NpgsqlCommand("INSERT INTO local_credentials(user_id,first_name,last_name) VALUES(@user,@first,@last) ON CONFLICT(user_id) DO UPDATE SET first_name=EXCLUDED.first_name,last_name=EXCLUDED.last_name,revision=local_credentials.revision+1",c,tx);q.Parameters.AddWithValue("first",input.FirstName);q.Parameters.AddWithValue("last",input.LastName);q.Parameters.AddWithValue("user",user);await q.ExecuteNonQueryAsync(token);await FreshActor(c,tx,actor,user,token);});
 }
 public async Task<object> AdministratorAsync(OperatorContext owner,Guid company,LocalAdministratorInput input,CancellationToken ct)
 {
  if(input.UserId is not null){if(input.Account is not null)throw new ArgumentException("Select one account.");return await accounts.AdministratorAsync(owner,company,new(null,input.Revision,input.UserId,input.AccountRevision),ct);}
  if(input.Account is null)throw new ArgumentException("Administrator is required.");CredentialLink? link=null;AccountDto? account=null;
  var result=await accounts.AdministratorAsync(owner,company,new(Input(input.Account),input.Revision),ct,async(c,tx,a,token)=>{link=await Provision(c,tx,a,input.Account,owner.UserId,company,token);account=await CompanyAccountRepository.AccountAsync(c,tx,company,a.Id,await SeatAccounting.TimeAsync(c,tx,token),token);await FreshActor(c,tx,AccountActor.For(owner),a.Id,token);});return new ProvisionedCompany(result,new(account!,link!));
 }
 public async Task<CredentialLink> IssueAsync(AccountActor actor,Guid company,Guid user,RevisionInput input,CancellationToken ct)
 {
  CompanyAccountRepository.Revision(input.Revision);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await CompanyAccountRepository.LockAsync(c,tx,actor,company,ct);await CompanyAccountRepository.LockTargetAsync(c,tx,company,user,ct);if(user==actor.UserId)throw new CompanyDenied();await CompanyAccountRepository.ProtectOwnerAsync(c,tx,user,ct);var account=await CompanyAccountRepository.AccountAsync(c,tx,company,user,await SeatAccounting.TimeAsync(c,tx,ct),ct);
  if(actor.Operator is null&&account.Role==CompanyRole.Administrator)throw new CompanyDenied();if(account.Revision!=input.Revision)throw new CompanyConflict();if(!account.Enabled)throw new CompanyDenied();
  var profile=await credentials.ProfileAsync(c,tx,user,ct);if(profile is null)await credentials.CreateUnsetAsync(c,tx,user,ct);
  var purpose=profile?.PasswordSet==true?CredentialTokenPurpose.Reset:CredentialTokenPurpose.Setup;
  await CompanyAccountRepository.RevokeUserAsync(c,tx,user,ct);var link=await tokens.IssueAsync(c,tx,user,purpose,ct);await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"Credential"+purpose+"Issued",user,null,new{link.UserId,link.ExpiresAt},ct);await FreshActor(c,tx,actor,user,ct);await tx.CommitAsync(ct);return link;
 }
 private static async Task FreshActor(NpgsqlConnection c,NpgsqlTransaction tx,AccountActor actor,Guid target,CancellationToken ct)
 {
  var session=actor.Operator?.SessionId??actor.Company?.SessionId;if(session is null)return;
  // Initial authority validation already locked this session. Recheck its clock after credential/token waits.
  // An allowed own profile edit intentionally revoked its own session earlier in the same transaction.
  await using var q=new NpgsqlCommand("SELECT id FROM account_sessions WHERE id=@id AND user_id=@user AND expires_at>clock_timestamp() AND (revoked_at IS NULL OR @self)",c,tx);q.Parameters.AddWithValue("id",session.Value);q.Parameters.AddWithValue("user",actor.UserId);q.Parameters.AddWithValue("self",actor.UserId==target);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();
 }

}
