using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api.Accounts;
public static class AccountAuditRepository
{
    public static async Task AppendAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid actor,Guid? company,string action,Guid? target,object? before,object? after,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("INSERT INTO account_audit(id,actor_user_id,company_id,action,target_id,before_value,after_value) VALUES(@id,@actor,@company,@action,@target,@before,@after)",c,tx);
        q.Parameters.AddWithValue("id",Guid.NewGuid());q.Parameters.AddWithValue("actor",actor);q.Parameters.AddWithValue("company",NpgsqlDbType.Uuid,(object?)company??DBNull.Value);q.Parameters.AddWithValue("action",action);
        q.Parameters.AddWithValue("target",NpgsqlDbType.Uuid,(object?)target??DBNull.Value);
        q.Parameters.AddWithValue("before",NpgsqlDbType.Jsonb,before is null?DBNull.Value:JsonSerializer.Serialize(before));
        q.Parameters.AddWithValue("after",NpgsqlDbType.Jsonb,after is null?DBNull.Value:JsonSerializer.Serialize(after));await q.ExecuteNonQueryAsync(ct);
    }
}
