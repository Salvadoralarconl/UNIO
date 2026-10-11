using System.Text.Json;
using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api;
public sealed record AuditEvent(Guid Id,Guid ActorUserId,string Action,string ResourceType,Guid ResourceId,JsonElement? Before,JsonElement? After,DateTime OccurredAt,string? ActorDisplayName,Guid? TicketId=null);
public sealed record AuditPage(IReadOnlyList<AuditEvent> Items,string? NextCursor);
public sealed class AuditRepository(NpgsqlDataSource database) {
 record Cursor(Guid Company,Guid Asset,DateTime Time,Guid Id);
 public static async Task AppendAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,CompanyContext context,string action,string resourceType,Guid resourceId,object? before,object? after,CancellationToken ct,Guid? ticketId=null) {
  await using var q=new NpgsqlCommand("INSERT INTO inventory_audit(id,company_id,actor_user_id,action,resource_type,resource_id,before_snapshot,after_snapshot,actor_name_snapshot,ticket_id) VALUES(@id,@company,@actor,@action,@type,@resource,@before,@after,(SELECT display_name FROM users WHERE id=@actor),@ticket)",connection,transaction);
  q.Parameters.AddWithValue("ticket",NpgsqlDbType.Uuid,(object?)ticketId??DBNull.Value);q.Parameters.AddWithValue("id",Guid.NewGuid());q.Parameters.AddWithValue("company",context.CompanyId);q.Parameters.AddWithValue("actor",context.UserId);q.Parameters.AddWithValue("action",action);q.Parameters.AddWithValue("type",resourceType);q.Parameters.AddWithValue("resource",resourceId);
  q.Parameters.AddWithValue("before",NpgsqlDbType.Jsonb,before is null?DBNull.Value:JsonSerializer.Serialize(before));q.Parameters.AddWithValue("after",NpgsqlDbType.Jsonb,after is null?DBNull.Value:JsonSerializer.Serialize(after));await q.ExecuteNonQueryAsync(ct);
 }
 internal static async Task<JsonElement?> SnapshotAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,CompanyContext c,string table,string key,Guid? id,CancellationToken ct) {
  if(id is null)return null;await using var q=new NpgsqlCommand($"SELECT to_jsonb(t) FROM {table} t WHERE {(table=="companies"?"id":"company_id")}=@company AND {key}=@id",connection,transaction);q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("id",id.Value);var value=await q.ExecuteScalarAsync(ct);return value is string json?JsonSerializer.Deserialize<JsonElement>(json):null;
 }
 public async Task<AuditPage> AssetHistoryAsync(CompanyContext c,Guid assetId,string? cursor,int limit,CancellationToken ct) {
  if(limit is <1 or >100)throw new ArgumentException("Limit must be 1 to 100.");
  Cursor? after=null;if(cursor is not null){try{after=JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor));}catch(Exception e)when(e is FormatException or JsonException){throw new ArgumentException("Invalid cursor.");}if(after is null||after.Company!=c.CompanyId||after.Asset!=assetId||after.Time.Kind!=DateTimeKind.Utc)throw new ArgumentException("Invalid cursor.");}
  await using var connection=await database.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
  await using(var visible=new NpgsqlCommand("SELECT 1 FROM inventory_assets WHERE company_id=@company AND id=@asset AND (@all OR employee_id=@employee)",connection,tx)){visible.Parameters.AddWithValue("company",c.CompanyId);visible.Parameters.AddWithValue("asset",assetId);visible.Parameters.AddWithValue("all",c.VisibilityScope!=CompanyVisibilityScope.AssignedOnly);visible.Parameters.AddWithValue("employee",NpgsqlDbType.Uuid,(object?)c.EmployeeId??DBNull.Value);if(await visible.ExecuteScalarAsync(ct) is null)throw new InventoryMissing();}
  await using var q=new NpgsqlCommand("SELECT a.id,a.actor_user_id,a.action,a.resource_type,a.resource_id,a.before_snapshot::text,a.after_snapshot::text,a.occurred_at,coalesce(a.actor_name_snapshot,u.display_name),a.ticket_id FROM inventory_audit a LEFT JOIN users u ON u.id=a.actor_user_id WHERE a.company_id=@company AND a.resource_type='asset' AND a.resource_id=@asset"+(after is null?"":" AND (a.occurred_at,a.id)<(@time,@id)")+" ORDER BY a.occurred_at DESC,a.id DESC LIMIT @limit",connection,tx);
  q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("asset",assetId);q.Parameters.AddWithValue("limit",limit+1);if(after is not null){q.Parameters.AddWithValue("time",after.Time);q.Parameters.AddWithValue("id",after.Id);}
  var rows=new List<AuditEvent>();await using(var r=await q.ExecuteReaderAsync(ct)){while(await r.ReadAsync(ct))rows.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.GetGuid(4),r.IsDBNull(5)?null:JsonSerializer.Deserialize<JsonElement>(r.GetString(5)),r.IsDBNull(6)?null:JsonSerializer.Deserialize<JsonElement>(r.GetString(6)),r.GetDateTime(7),r.IsDBNull(8)?null:r.GetString(8),r.IsDBNull(9)?null:r.GetGuid(9)));}
  var more=rows.Count>limit;if(more)rows.RemoveAt(limit);return new(rows,more?Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(c.CompanyId,assetId,rows[^1].OccurredAt,rows[^1].Id))):null);
 }
}

