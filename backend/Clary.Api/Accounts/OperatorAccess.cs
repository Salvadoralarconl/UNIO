using System.Security.Claims;
using Npgsql;
namespace Clary.Api.Accounts;
public sealed class OperatorAccess(NpgsqlDataSource database)
{
 public async Task<OperatorContext?> ResolveAsync(ClaimsPrincipal principal,CancellationToken ct=default)
 {
  if(principal.Identity?.IsAuthenticated!=true)return null;
  var issuers=principal.FindAll("iss").Select(c=>c.Value).ToArray();var subjects=principal.FindAll("sub").Select(c=>c.Value).ToArray();
  if(issuers.Length!=1||subjects.Length!=1||!OperatorBootstrap.ValidBinding(issuers[0],subjects[0]))return null;
  await using var q=database.CreateCommand("SELECT u.id FROM operator_owner o JOIN users u ON u.id=o.user_id WHERE o.issuer=@issuer AND o.subject=@subject AND u.issuer=o.issuer AND u.identity_subject=o.subject AND u.active");
  q.Parameters.AddWithValue("issuer",issuers[0]);q.Parameters.AddWithValue("subject",subjects[0]);
  return await q.ExecuteScalarAsync(ct) is Guid id?new(id):null;
 }
}
