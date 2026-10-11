using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api.Accounts;

public record InvitationInput(string Email,[property:System.Text.Json.Serialization.JsonRequired] CompanyRole Role,string? Issuer=null,string? Subject=null);
public record InvitationDto(Guid Id,string Email,CompanyRole Role,string Status,int Revision,DateTimeOffset CreatedAt,DateTimeOffset ExpiresAt,string? Issuer,string? Subject);
public record IssuedInvitation(Guid Id,string Email,CompanyRole Role,string Status,int Revision,DateTimeOffset CreatedAt,DateTimeOffset ExpiresAt,string? Issuer,string? Subject,string Token,string Link);
public record RevisionInput(int? Revision);
public record AcceptInvitationInput(string Token);
public sealed class InvitationService(NpgsqlDataSource database)
{
    public static string NormalizeEmail(string email)
    {
        if(string.IsNullOrWhiteSpace(email)||email.Length>320||email!=email.Trim()||!MailAddress.TryCreate(email,out var address)||address.Address!=email)throw new ArgumentException("A valid email address is required.");return email.ToLowerInvariant();
    }
    public async Task<IssuedInvitation> IssueAsync(AccountActor actor,Guid company,InvitationInput input,CancellationToken ct)
    {
        var email=NormalizeEmail(input.Email);if(input.Role is not (CompanyRole.Editor or CompanyRole.Viewer))throw new CompanyDenied();
        if((input.Issuer is null)!=(input.Subject is null)||(input.Issuer is not null&&!OperatorBootstrap.ValidBinding(input.Issuer,input.Subject!)))throw new ArgumentException("Both valid issuer and subject are required for identity binding.");
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await CompanyAccountRepository.LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        if(input.Issuer is not null)
        {
            await using var owner=new NpgsqlCommand("SELECT 1 FROM operator_owner o WHERE o.issuer=@issuer AND o.subject=@subject AND (o.user_id=@actor OR EXISTS(SELECT 1 FROM memberships m WHERE m.user_id=o.user_id AND m.company_id=@company))",c,tx);owner.Parameters.AddWithValue("issuer",input.Issuer);owner.Parameters.AddWithValue("subject",input.Subject!);owner.Parameters.AddWithValue("actor",actor.UserId);owner.Parameters.AddWithValue("company",company);if(await owner.ExecuteScalarAsync(ct) is not null)throw new CompanyDenied();
        }
        await using(var q=new NpgsqlCommand("""
            SELECT 1 FROM users u JOIN memberships m ON m.user_id=u.id WHERE
            ((m.company_id=@company AND lower(u.email)=@email) OR (u.issuer=@issuer AND u.identity_subject=@subject))
            """,c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("email",email);q.Parameters.AddWithValue("issuer",input.Issuer??"");q.Parameters.AddWithValue("subject",input.Subject??"");if(await q.ExecuteScalarAsync(ct) is not null)throw new ExistingAccountConflict();}
        // Reissue expires the old logical reservation before adding one new reservation.
        var expired=new List<InvitationDto>();await using(var q=new NpgsqlCommand("SELECT id FROM account_invitations WHERE company_id=@company AND (lower(email)=@email OR (@issuer<>'' AND bound_issuer=@issuer AND bound_subject=@subject)) AND state='Pending' AND expires_at<=@time FOR UPDATE",c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("email",email);q.Parameters.AddWithValue("issuer",input.Issuer??"");q.Parameters.AddWithValue("subject",input.Subject??"");q.Parameters.AddWithValue("time",time);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))expired.Add(new(r.GetGuid(0),email,input.Role,"Expired",0,time,time,null,null));}
        foreach(var old in expired)
        {var before=await ReadAsync(c,tx,company,old.Id,time,ct);await using var q=new NpgsqlCommand("UPDATE account_invitations SET state='Revoked',revision=revision+1 WHERE id=@id",c,tx);q.Parameters.AddWithValue("id",old.Id);await q.ExecuteNonQueryAsync(ct);await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"InvitationExpiredReissued",old.Id,before,await ReadAsync(c,tx,company,old.Id,time,ct),ct);}
        await using(var q=new NpgsqlCommand("SELECT 1 FROM account_invitations WHERE company_id=@company AND (lower(email)=@email OR (@issuer<>'' AND bound_issuer=@issuer AND bound_subject=@subject)) AND state='Pending'",c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("email",email);q.Parameters.AddWithValue("issuer",input.Issuer??"");q.Parameters.AddWithValue("subject",input.Subject??"");if(await q.ExecuteScalarAsync(ct) is not null)throw new CompanyConflict();}
        await SeatAccounting.RequireAdditionalAsync(c,tx,company,time,ct);
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var hash=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));var id=Guid.NewGuid();
        await using(var q=new NpgsqlCommand("INSERT INTO account_invitations(id,company_id,email,role,token_hash,created_at,expires_at,bound_issuer,bound_subject) VALUES(@id,@company,@email,@role,@hash,@time,@time+interval '7 days',@issuer,@subject)",c,tx))
        {q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("email",email);q.Parameters.AddWithValue("role",input.Role.ToString());q.Parameters.AddWithValue("hash",hash);q.Parameters.AddWithValue("time",time);q.Parameters.AddWithValue("issuer",NpgsqlDbType.Text,(object?)input.Issuer??DBNull.Value);q.Parameters.AddWithValue("subject",NpgsqlDbType.Text,(object?)input.Subject??DBNull.Value);await q.ExecuteNonQueryAsync(ct);}
        var result=await ReadAsync(c,tx,company,id,time,ct);await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"InvitationIssued",id,null,result,ct);await tx.CommitAsync(ct);
        // Fragment keeps the secret out of server URL/query logs; frontend Task5 consumes it.
        return new(result.Id,result.Email,result.Role,result.Status,result.Revision,result.CreatedAt,result.ExpiresAt,result.Issuer,result.Subject,token,"/accept-invitation#token="+token);
    }
    public async Task<InvitationDto> RevokeAsync(AccountActor actor,Guid company,Guid id,RevisionInput input,CancellationToken ct)
    {
        CompanyAccountRepository.Revision(input.Revision);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await CompanyAccountRepository.LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        await using(var q=new NpgsqlCommand("SELECT id FROM account_invitations WHERE company_id=@company AND id=@id FOR UPDATE",c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("id",id);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyMissing();}
        var before=await ReadAsync(c,tx,company,id,time,ct);if(before.Revision!=input.Revision||before.Status is "Accepted" or "Revoked")throw new CompanyConflict();
        await using(var q=new NpgsqlCommand("UPDATE account_invitations SET state='Revoked',revision=revision+1 WHERE company_id=@company AND id=@id",c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("id",id);await q.ExecuteNonQueryAsync(ct);}
        var result=await ReadAsync(c,tx,company,id,time,ct);await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"InvitationRevoked",id,before,result,ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<AccountDto> AcceptAsync(ClaimsPrincipal principal, string token, CancellationToken ct)
    {
        // Never interpolate, log, audit or echo the input token, even on malformed/replayed requests.
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new ArgumentException("A valid invitation token is required.");
        if (principal.Identity?.IsAuthenticated != true || principal.FindAll("iss").Count() != 1 || principal.FindAll("sub").Count() != 1)
            throw new CompanyDenied();
        var issuer = principal.FindFirstValue("iss")!;
        var subject = principal.FindFirstValue("sub")!;
        if (!OperatorBootstrap.ValidBinding(issuer, subject)) throw new CompanyDenied();
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        Guid company, id;
        await using var c = await database.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var q = new NpgsqlCommand("SELECT company_id,id FROM account_invitations WHERE token_hash=@hash", c, tx))
        {
            q.Parameters.AddWithValue("hash", hash);
            await using var r = await q.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new CompanyMissing();
            company = r.GetGuid(0);
            id = r.GetGuid(1);
        }

        // Lock company, invitation, user and persisted session in the established order.
        await using (var q = new NpgsqlCommand("SELECT id FROM companies WHERE id=@company AND active FOR UPDATE", c, tx))
        {
            q.Parameters.AddWithValue("company", company);
            if (await q.ExecuteScalarAsync(ct) is null) throw new CompanyDenied();
        }
        var time = await SeatAccounting.TimeAsync(c, tx, ct);
        await using (var q = new NpgsqlCommand("SELECT id FROM account_invitations WHERE company_id=@company AND id=@id AND token_hash=@hash FOR UPDATE", c, tx))
        {
            q.Parameters.AddWithValue("company", company);
            q.Parameters.AddWithValue("id", id);
            q.Parameters.AddWithValue("hash", hash);
            if (await q.ExecuteScalarAsync(ct) is null) throw new CompanyMissing();
        }
        var invitation = await ReadAsync(c, tx, company, id, time, ct);
        if (invitation.Status != "Pending") throw new CompanyConflict();
        if (invitation.Issuer is not null)
        {
            if (invitation.Issuer != issuer || invitation.Subject != subject) throw new CompanyDenied();
        }
        else
        {
            var emails = principal.FindAll("email").ToArray();
            var verified = principal.FindAll("email_verified").ToArray();
            if (emails.Length != 1 || verified.Length != 1 || verified[0].Value != "true") throw new CompanyDenied();
            string email;
            try { email = NormalizeEmail(emails[0].Value); }
            catch (ArgumentException) { throw new CompanyDenied(); }
            if (email != invitation.Email) throw new CompanyDenied();
        }
        Guid user;
        await using (var q = new NpgsqlCommand("SELECT id FROM users WHERE issuer=@issuer AND identity_subject=@subject AND active FOR UPDATE", c, tx))
        {
            q.Parameters.AddWithValue("issuer", issuer);
            q.Parameters.AddWithValue("subject", subject);
            if (await q.ExecuteScalarAsync(ct) is not Guid found) throw new CompanyDenied();
            user = found;
        }
        await SessionPresence.RevalidateAsync(c, tx, user, SessionPresence.Id(principal), ct);
        await CompanyAccountRepository.ProtectOwnerAsync(c, tx, user, ct);
        await using (var q = new NpgsqlCommand("SELECT 1 FROM memberships WHERE user_id=@user", c, tx))
        {
            q.Parameters.AddWithValue("user", user);
            if (await q.ExecuteScalarAsync(ct) is not null) throw new CompanyConflict();
        }

        // Later row-lock waits may outlive the reservation. Check the fresh database
        // clock after all locks/checks, immediately before converting the locked invitation.
        time = await SeatAccounting.TimeAsync(c, tx, ct);
        invitation = await ReadAsync(c, tx, company, id, time, ct);
        if (invitation.Status != "Pending") throw new CompanyConflict();
        // Its valid reservation already consumes a seat; conversion is legal even after plan reduction.
        var membershipRevision=await CompanyAccountRepository.NewMembershipRevisionAsync(c,tx,user,ct);
        await using (var q = new NpgsqlCommand("INSERT INTO memberships(company_id,user_id,role,revision) VALUES(@company,@user,@role,@membershipRevision); UPDATE account_invitations SET state='Accepted',accepted_user_id=@user,revision=revision+1 WHERE id=@id", c, tx))
        {
            q.Parameters.AddWithValue("membershipRevision",membershipRevision);
            q.Parameters.AddWithValue("company", company);
            q.Parameters.AddWithValue("user", user);
            q.Parameters.AddWithValue("role", invitation.Role.ToString());
            q.Parameters.AddWithValue("id", id);
            await q.ExecuteNonQueryAsync(ct);
        }
        await CompanyAccountRepository.RevokeUserAsync(c, tx, user, ct);
        var result = await CompanyAccountRepository.AccountAsync(c, tx, company, user, time, ct);
        await AccountAuditRepository.AppendAsync(c, tx, user, company, "InvitationAccepted", id, invitation,
            await ReadAsync(c, tx, company, id, time, ct), ct);
        await tx.CommitAsync(ct);
        return result;
    }
    internal static async Task<InvitationDto> ReadAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,Guid id,DateTimeOffset time,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT email,role,CASE WHEN state='Pending' AND expires_at<=@time THEN 'Expired' ELSE state END,revision,created_at,expires_at,bound_issuer,bound_subject FROM account_invitations WHERE company_id=@company AND id=@id",c,tx);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("time",time);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();return new(id,r.GetString(0),Enum.Parse<CompanyRole>(r.GetString(1)),r.GetString(2),r.GetInt32(3),new(r.GetDateTime(4)),new(r.GetDateTime(5)),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7));
    }
}
