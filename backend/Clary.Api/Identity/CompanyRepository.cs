using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api.Identity;

public record CompanyDto(Guid Id,string Name,int Revision);
public record CompanyUpdate(string Name,int? Revision);
public record MembershipDto(Guid UserId,string Subject,string Issuer,string Role,bool Active,Guid? EmployeeId,int Revision);
public record MembershipInput(string Subject,string Issuer,string Role,bool Active,Guid? EmployeeId,int? Revision);
public record SessionUser(Guid Id,string DisplayName,string? Email);
public record SessionMembership(Guid CompanyId,string Name,string Role);
public sealed class CompanyConflict : Exception;
public sealed class CompanyMissing : Exception;
public sealed class CompanyDenied : Exception;

public sealed class CompanyRepository(NpgsqlDataSource database)
{
    public async Task PersistIdentityAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        var issuer=principal.FindFirstValue("iss");var subject=principal.FindFirstValue("sub");
        if(string.IsNullOrWhiteSpace(issuer)||string.IsNullOrWhiteSpace(subject))throw new CompanyDenied();
        await using var q=database.CreateCommand("""
            INSERT INTO users(id,issuer,identity_subject,display_name,email) VALUES(@id,@issuer,@subject,@name,@email)
            ON CONFLICT(issuer,identity_subject) DO UPDATE SET display_name=EXCLUDED.display_name,email=EXCLUDED.email
            """);
        q.Parameters.AddWithValue("id",Guid.NewGuid());q.Parameters.AddWithValue("issuer",issuer);q.Parameters.AddWithValue("subject",subject);
        q.Parameters.AddWithValue("name",principal.FindFirstValue("name")??subject);
        q.Parameters.AddWithValue("email",NpgsqlDbType.Text,(object?)principal.FindFirstValue("email")??DBNull.Value);
        await q.ExecuteNonQueryAsync(ct);
    }
    public async Task<SessionUser?> UserAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var q=database.CreateCommand("SELECT id,display_name,email FROM users WHERE issuer=@issuer AND identity_subject=@subject AND active");
        q.Parameters.AddWithValue("issuer",principal.FindFirstValue("iss")??"");q.Parameters.AddWithValue("subject",principal.FindFirstValue("sub")??"");
        await using var r=await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2)):null;
    }
    public async Task<List<SessionMembership>> MembershipsAsync(Guid userId,CancellationToken ct)
    {
        await using var q=database.CreateCommand("""
            SELECT c.id,c.name,m.role FROM memberships m JOIN companies c ON c.id=m.company_id
            JOIN users u ON u.id=m.user_id WHERE m.user_id=@user AND m.active AND c.active AND u.active ORDER BY c.id
            """);q.Parameters.AddWithValue("user",userId);
        await using var r=await q.ExecuteReaderAsync(ct);var result=new List<SessionMembership>();
        while(await r.ReadAsync(ct))result.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2)));return result;
    }
    public async Task<CompanyDto?> GetAsync(CompanyContext context,CancellationToken ct)
    {
        await using var q=database.CreateCommand("SELECT id,name,revision FROM companies WHERE id=@company AND active");q.Parameters.AddWithValue("company",context.CompanyId);
        await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetString(1),r.GetInt32(2)):null;
    }
    public async Task<CompanyDto> UpdateAsync(CompanyContext context,CompanyUpdate input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Trim().Length>200||input.Revision is null or <1)throw new ArgumentException("Name and expected revision are required.");
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await LockAdmin(c,tx,context,ct);
        var before=await AuditRepository.SnapshotAsync(c,tx,context,"companies","id",context.CompanyId,ct);
        var accountBefore=before is {} snapshot
            ? new CompanyDto(context.CompanyId,snapshot.GetProperty("name").GetString()!,snapshot.GetProperty("revision").GetInt32())
            : throw new CompanyMissing();
        await using var q=new NpgsqlCommand("UPDATE companies SET name=@name,revision=revision+1 WHERE id=@company AND revision=@revision RETURNING revision",c,tx);
        q.Parameters.AddWithValue("name",input.Name.Trim());q.Parameters.AddWithValue("company",context.CompanyId);q.Parameters.AddWithValue("revision",input.Revision.Value);
        var revision=await q.ExecuteScalarAsync(ct);
        if(revision is null)throw new CompanyConflict();
        var result=new CompanyDto(context.CompanyId,input.Name.Trim(),(int)revision);
        await AuditRepository.AppendAsync(c,tx,context,"Updated","company",context.CompanyId,before,result,ct);
        await AccountAuditRepository.AppendAsync(c,tx,context.UserId,context.CompanyId,"CompanyUpdated",context.CompanyId,accountBefore,result,ct);
        await tx.CommitAsync(ct);
        return result;
    }
    public async Task<List<MembershipDto>> ListAsync(CompanyContext context,CancellationToken ct)
    {
        await using var q=database.CreateCommand("""
            SELECT u.id,u.identity_subject,u.issuer,m.role,m.active,m.employee_id,m.revision
            FROM memberships m JOIN users u ON u.id=m.user_id WHERE company_id=@company ORDER BY u.identity_subject,u.issuer
            """);q.Parameters.AddWithValue("company",context.CompanyId);await using var r=await q.ExecuteReaderAsync(ct);var items=new List<MembershipDto>();
        while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetBoolean(4),r.IsDBNull(5)?null:r.GetGuid(5),r.GetInt32(6)));return items;
    }
    public Task<MembershipDto> SaveAsync(CompanyContext context,MembershipInput input,bool update,CancellationToken ct) => new CompanyAccountRepository(database).LegacySaveAsync(context,input,update,ct);
    private static Task LockAdmin(NpgsqlConnection c,NpgsqlTransaction tx,CompanyContext context,CancellationToken ct)
        =>AccountPermissions.RevalidateAsync(c,tx,context,CompanyCapability.AccountAdmin,ct);
}
