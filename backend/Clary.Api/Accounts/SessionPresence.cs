using System.Security.Claims;
using Clary.Api.Identity;
using Npgsql;
namespace Clary.Api.Accounts;
public sealed class SessionPresence(NpgsqlDataSource database)
{
 public const string ClaimType="clary_session";
 public static Guid? Id(ClaimsPrincipal p)=>p.FindAll(ClaimType).Count()==1&&Guid.TryParse(p.FindFirstValue(ClaimType),out var id)?id:null;
 public async Task<Guid> CreateAsync(ClaimsPrincipal principal,DateTimeOffset expires,CancellationToken ct)
 {
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  // Same company-first order as account transitions, then identity, then session.
  await using(var company=new NpgsqlCommand("SELECT c.id FROM companies c JOIN memberships m ON m.company_id=c.id JOIN users u ON u.id=m.user_id WHERE u.issuer=@issuer AND u.identity_subject=@subject FOR UPDATE OF c",c,tx)){company.Parameters.AddWithValue("issuer",principal.FindFirstValue("iss")!);company.Parameters.AddWithValue("subject",principal.FindFirstValue("sub")!);await company.ExecuteScalarAsync(ct);}
  var id=Guid.NewGuid();await using var q=new NpgsqlCommand("INSERT INTO account_sessions(id,user_id,expires_at) SELECT @id,u.id,@expires FROM users u WHERE u.issuer=@issuer AND u.identity_subject=@subject AND u.active RETURNING id",c,tx);q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("expires",expires);q.Parameters.AddWithValue("issuer",principal.FindFirstValue("iss")!);q.Parameters.AddWithValue("subject",principal.FindFirstValue("sub")!);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();await tx.CommitAsync(ct);return id;
 }
 public async Task<bool> ValidAsync(ClaimsPrincipal p,CancellationToken ct)
 {
  if(Id(p) is not Guid id)return false;
  await using var q=database.CreateCommand("SELECT 1 FROM account_sessions s JOIN users u ON u.id=s.user_id WHERE s.id=@id AND u.issuer=@issuer AND u.identity_subject=@subject AND u.active AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()");q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("issuer",p.FindFirstValue("iss")??"");q.Parameters.AddWithValue("subject",p.FindFirstValue("sub")??"");return await q.ExecuteScalarAsync(ct) is not null;
 }
 public async Task<DateTimeOffset?> ValidExpiryAsync(ClaimsPrincipal p,CancellationToken ct)
 {
  if(Id(p) is not Guid id)return null;await using var q=database.CreateCommand("SELECT s.expires_at FROM account_sessions s JOIN users u ON u.id=s.user_id WHERE s.id=@id AND u.issuer=@issuer AND u.identity_subject=@subject AND u.active AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()");q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("issuer",p.FindFirstValue("iss")??"");q.Parameters.AddWithValue("subject",p.FindFirstValue("sub")??"");return await q.ExecuteScalarAsync(ct) is DateTime expiry?new DateTimeOffset(expiry):null;
 }
 public static async Task RevalidateAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid user,Guid? session,CancellationToken ct)
 {
  if(session is null)return; // Offline service contexts have no cookie; HTTP always carries a validated session.
  // Obtain the row lock before checking the clock: a predicate evaluated before
  // a lock-only wait can otherwise remain true after the session expires.
  await using(var locked=new NpgsqlCommand("SELECT id FROM account_sessions WHERE id=@id FOR UPDATE",c,tx))
  {locked.Parameters.AddWithValue("id",session.Value);if(await locked.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
  await using var q=new NpgsqlCommand("SELECT id FROM account_sessions WHERE id=@id AND user_id=@user AND revoked_at IS NULL AND expires_at>clock_timestamp()",c,tx);q.Parameters.AddWithValue("id",session.Value);q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();
 }
 public async Task<DateTimeOffset?> HeartbeatAsync(ClaimsPrincipal p,CompanyContext? company,OperatorContext? owner,CancellationToken ct,bool renew=false)
 {
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);Guid user;
  if(company is not null){await AccountPermissions.RevalidateAsync(c,tx,company,CompanyCapability.OwnInventory,ct);user=company.UserId;}
  else if(owner is not null){await CompanyAccountRepository.RevalidateOperatorAsync(c,tx,owner.UserId,ct,owner.SessionId);user=owner.UserId;}
  else throw new CompanyDenied();
  await using var q=new NpgsqlCommand("UPDATE account_sessions SET last_activity_at=clock_timestamp() WHERE id=@id AND user_id=@user AND revoked_at IS NULL AND expires_at>clock_timestamp() AND last_activity_at<=clock_timestamp()-interval '30 seconds'",c,tx);q.Parameters.AddWithValue("id",Id(p)!.Value);q.Parameters.AddWithValue("user",user);await q.ExecuteNonQueryAsync(ct);DateTimeOffset? expiry=null;
  if(renew){await using var renewal=new NpgsqlCommand("UPDATE account_sessions SET expires_at=clock_timestamp()+@lifetime WHERE id=@id AND user_id=@user AND revoked_at IS NULL AND expires_at>clock_timestamp() AND expires_at<clock_timestamp()+@threshold RETURNING expires_at",c,tx);renewal.Parameters.AddWithValue("id",Id(p)!.Value);renewal.Parameters.AddWithValue("user",user);renewal.Parameters.AddWithValue("lifetime",Clary.Api.LocalIdentity.LocalSessionPolicy.Lifetime);renewal.Parameters.AddWithValue("threshold",Clary.Api.LocalIdentity.LocalSessionPolicy.Lifetime/2);if(await renewal.ExecuteScalarAsync(ct) is DateTime renewed)expiry=new DateTimeOffset(renewed);}
  await tx.CommitAsync(ct);return expiry;
 }
 public async Task RevokeAsync(ClaimsPrincipal p,CancellationToken ct){if(Id(p) is not Guid id)return;await using var q=database.CreateCommand("UPDATE account_sessions SET revoked_at=clock_timestamp() WHERE id=@id AND revoked_at IS NULL");q.Parameters.AddWithValue("id",id);await q.ExecuteNonQueryAsync(ct);}
}
