using Clary.Api.Identity;
using Npgsql;
using System.Text.RegularExpressions;
namespace Clary.Api;
public static class EpsonModels {
 public static string[] Extract(string text)=>Regex.Matches(text,@"\b(?:tm[-\s]*)?t?(88iv|88v|20iii|20ii|m30)\b",RegexOptions.IgnoreCase).Select(m=>m.Groups[1].Value.Equals("m30",StringComparison.OrdinalIgnoreCase)?"TM-m30":"TM-T"+m.Groups[1].Value.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
 public static async Task RepairMissingAsync(CompanyContext context,NpgsqlDataSource db,CancellationToken ct=default)
 {
  await using var conn=await db.OpenConnectionAsync(ct);await using var tx=await conn.BeginTransactionAsync(ct);

  await AccountPermissions.RevalidateAsync(conn,tx,context,CompanyCapability.Repair,ct);
  var updates=new List<(Guid Id,string[] Models)>();
  await using(var q=CompanyCommand.Create(context,"SELECT id,canonical_text FROM documents WHERE company_id=@company AND cardinality(supported_models)=0 AND NOT archived",conn,tx))
  {await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var models=Extract(r.GetString(1));if(models.Length>0)updates.Add((r.GetGuid(0),models));}}
  foreach(var item in updates)
  {await using var q=CompanyCommand.Create(context,"UPDATE documents SET supported_models=@models WHERE company_id=@company AND id=@id AND cardinality(supported_models)=0",conn,tx);q.Parameters.AddWithValue("models",item.Models);q.Parameters.AddWithValue("id",item.Id);await q.ExecuteNonQueryAsync(ct);}
  await tx.CommitAsync(ct);
 }
}


