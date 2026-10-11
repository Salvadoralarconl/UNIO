using System.Security.Claims;
using Npgsql;
namespace Clary.Api.Identity;

public sealed class CompanyAccess(NpgsqlDataSource database)
{
    public async Task<CompanyContext?> ResolveAsync(ClaimsPrincipal principal, Guid companyId, CancellationToken ct)
    {
        if(principal.Identity?.IsAuthenticated!=true)return null;
        var issuer=principal.FindFirstValue("iss");var subject=principal.FindFirstValue("sub");
        if(string.IsNullOrWhiteSpace(issuer)||string.IsNullOrWhiteSpace(subject))return null;
        await using var q=database.CreateCommand("""
            SELECT u.id,m.role,m.employee_id,e.department_id,m.visibility_scope FROM memberships m
            JOIN users u ON u.id=m.user_id JOIN companies c ON c.id=m.company_id
            LEFT JOIN employees e ON e.company_id=m.company_id AND e.id=m.employee_id
            WHERE m.company_id=@company AND u.issuer=@issuer AND u.identity_subject=@subject
            AND u.active AND c.active AND m.active
            """);
        q.Parameters.AddWithValue("company",companyId);q.Parameters.AddWithValue("issuer",issuer);q.Parameters.AddWithValue("subject",subject);
        await using var reader=await q.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)||!Enum.TryParse<CompanyRole>(reader.GetString(1),out var role))return null;
        return new(companyId,reader.GetGuid(0),role,reader.IsDBNull(2)?null:reader.GetGuid(2),reader.IsDBNull(3)?null:reader.GetGuid(3),Enum.Parse<CompanyVisibilityScope>(reader.GetString(4)));
    }
}
