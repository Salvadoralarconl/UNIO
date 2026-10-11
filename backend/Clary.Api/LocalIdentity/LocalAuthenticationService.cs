using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Identity;
using Npgsql;
namespace Clary.Api.LocalIdentity;

public record SetupDetails(string Purpose,string? Username,string? FirstName,string? LastName,DateTimeOffset ExpiresAt);
public record LocalLogin(ClaimsPrincipal Principal,DateTimeOffset ExpiresAt);
public sealed class LocalAuthenticationService(NpgsqlDataSource database,LocalPasswordService passwords,LocalCredentialRepository credentials,LocalCredentialTokens tokens)
{
 private readonly string dummy=passwords.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
 internal static byte[] TokenHash(string token)
 {if(token is not {Length:64}||token.Any(ch=>!char.IsAsciiHexDigit(ch)))throw new ArgumentException("Invalid or expired link.");return SHA256.HashData(Encoding.UTF8.GetBytes(token));}
 // Resolve persisted membership first, then serialize company -> user -> credential. No browser identity participates.
 internal static async Task<Guid?> LockUser(NpgsqlConnection c,NpgsqlTransaction tx,Guid user,CancellationToken ct)
 {
  Guid? company;await using(var q=new NpgsqlCommand("SELECT company_id FROM memberships WHERE user_id=@user",c,tx)){q.Parameters.AddWithValue("user",user);company=await q.ExecuteScalarAsync(ct) as Guid?;}
  if(company is Guid id){await using var q=new NpgsqlCommand("SELECT id FROM companies WHERE id=@company AND active FOR UPDATE",c,tx);q.Parameters.AddWithValue("company",id);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  await using(var q=new NpgsqlCommand("SELECT id FROM users WHERE id=@user AND active FOR UPDATE",c,tx)){q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  await using(var q=new NpgsqlCommand("SELECT company_id,active FROM memberships WHERE user_id=@user FOR UPDATE",c,tx)){q.Parameters.AddWithValue("user",user);await using var r=await q.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){if(!r.GetBoolean(1)||company!=r.GetGuid(0))throw new CompanyDenied();}else if(company is not null)throw new CompanyDenied();}
  if(company is null){await using var q=new NpgsqlCommand("SELECT user_id FROM operator_owner WHERE user_id=@user",c,tx);q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  await using(var q=new NpgsqlCommand("SELECT user_id FROM local_credentials WHERE user_id=@user FOR UPDATE",c,tx)){q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  return company;
 }
 public async Task<LocalLogin?> LoginAsync(LoginInput input,CancellationToken ct)
 {
  string normalized;try{normalized=LocalCredentialRepository.NormalizeUsername(input.Username);}catch(ArgumentException){passwords.Verify(dummy,input.Password??"");return null;}
  if(input.Password is null||input.Password.Length>128){passwords.Verify(dummy,"");return null;}
  Guid user;await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  await using(var q=new NpgsqlCommand("SELECT user_id FROM local_credentials WHERE normalized_username=@name",c,tx)){q.Parameters.AddWithValue("name",normalized);if(await q.ExecuteScalarAsync(ct) is not Guid found){passwords.Verify(dummy,input.Password);await Event(c,tx,"LoginFailed",ct);await tx.CommitAsync(ct);return null;}user=found;}
  Guid? company;try{company=await LockUser(c,tx,user,ct);}catch(CompanyDenied){passwords.Verify(dummy,input.Password);return null;}
  string? hash;bool locked;await using(var q=new NpgsqlCommand("SELECT password_hash,locked_until>clock_timestamp() FROM local_credentials WHERE user_id=@user AND normalized_username=@name",c,tx)){q.Parameters.AddWithValue("user",user);q.Parameters.AddWithValue("name",normalized);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)){passwords.Verify(dummy,input.Password);return null;}hash=r.IsDBNull(0)?null:r.GetString(0);locked=!r.IsDBNull(1)&&r.GetBoolean(1);}
  var verified=passwords.Verify(locked||hash is null?dummy:hash,input.Password);
  if(locked||hash is null||verified==PasswordVerificationResult.Failed)
  {
   if(!locked&&hash is not null){await using var q=new NpgsqlCommand("UPDATE local_credentials SET failed_attempts=CASE WHEN locked_until<=clock_timestamp() THEN 1 ELSE LEAST(5,failed_attempts+1) END,locked_until=CASE WHEN (CASE WHEN locked_until<=clock_timestamp() THEN 1 ELSE failed_attempts+1 END)>=5 THEN clock_timestamp()+interval '15 minutes' ELSE NULL END WHERE user_id=@user",c,tx);q.Parameters.AddWithValue("user",user);await q.ExecuteNonQueryAsync(ct);}
   await Event(c,tx,locked?"LockedOut":"LoginFailed",ct);await tx.CommitAsync(ct);return null;
  }
  await using(var q=new NpgsqlCommand("UPDATE local_credentials SET failed_attempts=0,locked_until=NULL,password_hash=COALESCE(@hash,password_hash) WHERE user_id=@user",c,tx)){q.Parameters.AddWithValue("user",user);q.Parameters.AddWithValue("hash",NpgsqlTypes.NpgsqlDbType.Text,verified==PasswordVerificationResult.SuccessRehashNeeded?passwords.Hash(input.Password):DBNull.Value);await q.ExecuteNonQueryAsync(ct);}
  var claims=new List<Claim>();await using(var q=new NpgsqlCommand("SELECT issuer,identity_subject,display_name FROM users WHERE id=@user",c,tx)){q.Parameters.AddWithValue("user",user);await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);claims.Add(new("iss",r.GetString(0)));claims.Add(new("sub",r.GetString(1)));claims.Add(new("name",r.GetString(2)));}
  if(company is Guid selected)claims.Add(new("clary_company",selected.ToString()));
  var session=Guid.NewGuid();DateTimeOffset expiry;await using(var q=new NpgsqlCommand("INSERT INTO account_sessions(id,user_id,expires_at) VALUES(@id,@user,clock_timestamp()+@lifetime) RETURNING expires_at",c,tx)){q.Parameters.AddWithValue("lifetime",LocalSessionPolicy.Lifetime);q.Parameters.AddWithValue("id",session);q.Parameters.AddWithValue("user",user);expiry=new((DateTime)(await q.ExecuteScalarAsync(ct))!);}
  claims.Add(new(SessionPresence.ClaimType,session.ToString()));await tx.CommitAsync(ct);return new(new(new ClaimsIdentity(claims,IdentityEndpoints.CookieScheme)),expiry);
 }
 internal static async Task Event(NpgsqlConnection c,NpgsqlTransaction tx,string value,CancellationToken ct){await using var q=new NpgsqlCommand("INSERT INTO local_auth_security_events(event) VALUES(@event)",c,tx);q.Parameters.AddWithValue("event",value);await q.ExecuteNonQueryAsync(ct);}
 private async Task<(Guid User,CredentialTokenPurpose Purpose,SetupDetails Details)> Resolve(NpgsqlConnection c,NpgsqlTransaction tx,string token,CancellationToken ct)
 {
  var hash=TokenHash(token);Guid user;await using(var q=new NpgsqlCommand("SELECT user_id FROM local_credential_tokens WHERE token_hash=@hash",c,tx)){q.Parameters.AddWithValue("hash",hash);if(await q.ExecuteScalarAsync(ct) is not Guid id)throw new ArgumentException("Invalid or expired link.");user=id;}
  await LockUser(c,tx,user,ct);
  await using(var q=new NpgsqlCommand("SELECT id FROM local_credential_tokens WHERE token_hash=@hash FOR UPDATE",c,tx)){q.Parameters.AddWithValue("hash",hash);await q.ExecuteScalarAsync(ct);}
  await using(var q=new NpgsqlCommand("SELECT t.purpose,l.username,l.first_name,l.last_name,t.expires_at FROM local_credential_tokens t JOIN local_credentials l ON l.user_id=t.user_id WHERE t.token_hash=@hash AND t.credential_revision=l.revision AND t.revoked_at IS NULL AND t.consumed_at IS NULL AND t.expires_at>clock_timestamp() AND ((t.purpose='Setup' AND l.password_hash IS NULL) OR (t.purpose='Reset' AND l.password_hash IS NOT NULL))",c,tx)){q.Parameters.AddWithValue("hash",hash);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new ArgumentException("Invalid or expired link.");var purpose=Enum.Parse<CredentialTokenPurpose>(r.GetString(0));return(user,purpose,new(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),new(r.GetDateTime(4))));}
 }
 public async Task<SetupDetails> DetailsAsync(string token,CancellationToken ct)
 {
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await using(var q=new NpgsqlCommand("SELECT 1 FROM local_credential_tokens WHERE token_hash=@hash",c,tx)){q.Parameters.AddWithValue("hash",TokenHash(token));if(await q.ExecuteScalarAsync(ct) is not null){var result=await Resolve(c,tx,token,ct);return result.Details;}}
  var invite=await LockInvitation(c,tx,token,ct);return new("Invitation",null,null,null,invite.Invitation.ExpiresAt);
 }
 public async Task SetupAsync(CredentialSetupInput input,CancellationToken ct)
 {
  if(!passwords.Validate(input.Password,input.ConfirmPassword))throw new ArgumentException("Password must contain 15 to 128 characters and confirmation must match.");var hash=passwords.Hash(input.Password);
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);var result=await Resolve(c,tx,input.Token,ct);
  var user=await tokens.ConsumeAsync(c,tx,input.Token,result.Purpose,ct);if(user is null)throw new ArgumentException("Invalid or expired link.");
  var profile=result.Details;await credentials.SetAsync(c,tx,user.Value,result.Purpose==CredentialTokenPurpose.Reset?profile.Username!:input.Username,result.Purpose==CredentialTokenPurpose.Reset?profile.FirstName!:input.FirstName,result.Purpose==CredentialTokenPurpose.Reset?profile.LastName!:input.LastName,hash,ct);
  await tokens.RevokeAsync(c,tx,user.Value,ct);await CompanyAccountRepository.RevokeUserAsync(c,tx,user.Value,ct);await AccountAuditRepository.AppendAsync(c,tx,user.Value,null,"LocalPasswordSet",user.Value,null,new{purpose=result.Purpose.ToString()},ct);
  // Session revocation and unique username writes can wait after token consumption.
  await using(var fresh=new NpgsqlCommand("SELECT 1 FROM local_credential_tokens WHERE token_hash=@hash AND consumed_at IS NOT NULL AND expires_at>clock_timestamp()",c,tx)){fresh.Parameters.AddWithValue("hash",TokenHash(input.Token));if(await fresh.ExecuteScalarAsync(ct) is null)throw new ArgumentException("Invalid or expired link.");}
  await tx.CommitAsync(ct);
 }
 public async Task ChangeAsync(ClaimsPrincipal principal,PasswordChangeInput input,CancellationToken ct)
 {
  if(!passwords.Validate(input.Password,input.ConfirmPassword))throw new ArgumentException("Password must contain 15 to 128 characters and confirmation must match.");
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);Guid user;await using(var q=new NpgsqlCommand("SELECT id FROM users WHERE issuer=@issuer AND identity_subject=@subject",c,tx)){q.Parameters.AddWithValue("issuer",principal.FindFirstValue("iss")!);q.Parameters.AddWithValue("subject",principal.FindFirstValue("sub")!);if(await q.ExecuteScalarAsync(ct) is not Guid id)throw new CompanyDenied();user=id;}
  await LockUser(c,tx,user,ct);await SessionPresence.RevalidateAsync(c,tx,user,SessionPresence.Id(principal),ct);
  string? old;await using(var q=new NpgsqlCommand("SELECT password_hash FROM local_credentials WHERE user_id=@user",c,tx)){q.Parameters.AddWithValue("user",user);old=await q.ExecuteScalarAsync(ct) as string;}
  if(input.CurrentPassword is null||input.CurrentPassword.Length>128||passwords.Verify(old??dummy,input.CurrentPassword)==PasswordVerificationResult.Failed)throw new ArgumentException("Current password is invalid.");
  var profile=(await credentials.ProfileAsync(c,tx,user,ct))!;await credentials.SetAsync(c,tx,user,profile.Username!,profile.FirstName!,profile.LastName!,passwords.Hash(input.Password),ct);await tokens.RevokeAsync(c,tx,user,ct);await CompanyAccountRepository.RevokeUserAsync(c,tx,user,ct);await AccountAuditRepository.AppendAsync(c,tx,user,null,"LocalPasswordChanged",user,null,new{freshLoginRequired=true},ct);
  // Initial revalidation locked and accepted this acting session. It cannot be externally revoked
  // while we hold that lock; our own intentional revocation must not bypass final expiry.
  var actingSession=SessionPresence.Id(principal)??throw new CompanyDenied();
  await using(var fresh=new NpgsqlCommand("SELECT id FROM account_sessions WHERE id=@session AND user_id=@user AND expires_at>clock_timestamp()",c,tx)){fresh.Parameters.AddWithValue("session",actingSession);fresh.Parameters.AddWithValue("user",user);if(await fresh.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  await tx.CommitAsync(ct);
 }
 private static async Task<(Guid Company,InvitationDto Invitation)> LockInvitation(NpgsqlConnection c,NpgsqlTransaction tx,string token,CancellationToken ct)
 {
  var hash=TokenHash(token);Guid company,id;
  await using(var q=new NpgsqlCommand("SELECT company_id,id FROM account_invitations WHERE token_hash=@hash",c,tx)){q.Parameters.AddWithValue("hash",hash);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new ArgumentException("Invalid or expired link.");company=r.GetGuid(0);id=r.GetGuid(1);}
  await using(var q=new NpgsqlCommand("SELECT id FROM companies WHERE id=@company AND active FOR UPDATE",c,tx)){q.Parameters.AddWithValue("company",company);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  await using(var q=new NpgsqlCommand("SELECT id FROM account_invitations WHERE id=@id FOR UPDATE",c,tx)){q.Parameters.AddWithValue("id",id);await q.ExecuteScalarAsync(ct);}
  var invitation=await InvitationService.ReadAsync(c,tx,company,id,await SeatAccounting.TimeAsync(c,tx,ct),ct);
  // Provider-bound historical invitations cannot establish a new local identity.
  if(invitation.Status!="Pending"||invitation.Issuer is not null||invitation.Role is not (CompanyRole.Editor or CompanyRole.Viewer))throw new ArgumentException("Invalid or expired link.");return(company,invitation);
 }
 public async Task CompleteInvitationAsync(CredentialSetupInput input,CancellationToken ct)
 {
  LocalCredentialRepository.NormalizeUsername(input.Username);LocalCredentialRepository.ValidateNames(input.FirstName,input.LastName);
  if(!passwords.Validate(input.Password,input.ConfirmPassword))throw new ArgumentException("Invalid password.");var passwordHash=passwords.Hash(input.Password);
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);var invite=await LockInvitation(c,tx,input.Token,ct);
  await using(var q=new NpgsqlCommand("SELECT 1 FROM users u JOIN memberships m ON m.user_id=u.id WHERE lower(u.email)=@email AND m.company_id=@company",c,tx)){q.Parameters.AddWithValue("email",invite.Invitation.Email);q.Parameters.AddWithValue("company",invite.Company);if(await q.ExecuteScalarAsync(ct) is not null)throw new CompanyConflict();}
  var user=await credentials.CreateIdentityAsync(c,tx,input.FirstName,input.LastName,invite.Invitation.Email,ct);await credentials.CreateUnsetAsync(c,tx,user,ct);await credentials.SetAsync(c,tx,user,input.Username,input.FirstName,input.LastName,passwordHash,ct);
  // Unique username insertion may wait. Recheck the reservation using fresh database time after it.
  var time=await SeatAccounting.TimeAsync(c,tx,ct);var fresh=await InvitationService.ReadAsync(c,tx,invite.Company,invite.Invitation.Id,time,ct);if(fresh.Status!="Pending")throw new CompanyConflict();
  var membershipRevision=await CompanyAccountRepository.NewMembershipRevisionAsync(c,tx,user,ct);
  await using(var q=new NpgsqlCommand("INSERT INTO memberships(company_id,user_id,role,revision) VALUES(@company,@user,@role,@membershipRevision); UPDATE account_invitations SET state='Accepted',accepted_user_id=@user,revision=revision+1 WHERE id=@id",c,tx)){q.Parameters.AddWithValue("membershipRevision",membershipRevision);q.Parameters.AddWithValue("company",invite.Company);q.Parameters.AddWithValue("user",user);q.Parameters.AddWithValue("role",fresh.Role.ToString());q.Parameters.AddWithValue("id",fresh.Id);await q.ExecuteNonQueryAsync(ct);}
  await AccountAuditRepository.AppendAsync(c,tx,user,invite.Company,"InvitationAccepted",fresh.Id,fresh,await InvitationService.ReadAsync(c,tx,invite.Company,fresh.Id,time,ct),ct);await tx.CommitAsync(ct);
 }

}
