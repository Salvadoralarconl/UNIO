using Clary.Api.Identity;
using Npgsql;
namespace Clary.Api.Accounts;

public sealed class SeatLimitReached(SeatUsage usage):Exception
{ public SeatUsage Usage {get;}=usage; }
public static class SeatAccounting
{
    public static int? Limit(SubscriptionInput input)=> input.Kind switch {
        PlanKind.Ten when input.CustomLimit is null=>10,
        PlanKind.Twenty when input.CustomLimit is null=>20,
        PlanKind.Fifty when input.CustomLimit is null=>50,
        PlanKind.Hundred when input.CustomLimit is null=>100,
        PlanKind.Custom when input.CustomLimit is >100=>input.CustomLimit,
        PlanKind.Unlimited when input.CustomLimit is null=>null,
        _=>throw new ArgumentException("Select a valid plan; Custom requires an integer greater than 100.")
    };
    // Call after acquiring the company row lock. Transaction-start now() is insufficient after a wait.
    public static async Task<DateTimeOffset> TimeAsync(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT clock_timestamp()",c,tx);return new DateTimeOffset((DateTime)(await q.ExecuteScalarAsync(ct))!);}
    public static async Task<SeatUsage> UsageAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,DateTimeOffset time,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("""
            SELECT (SELECT count(*)::integer FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=c.id AND m.active AND u.active),
            (SELECT count(*)::integer FROM account_invitations i WHERE i.company_id=c.id AND i.state='Pending' AND i.expires_at>@time),c.seat_limit
            FROM companies c WHERE c.id=@company
            """,c,tx);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("time",time);
        await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();
        var enabled=r.GetInt32(0);var pending=r.GetInt32(1);int? limit=r.IsDBNull(2)?null:r.GetInt32(2);
        return new(enabled,pending,limit,limit is int cap?Math.Max(0,cap-enabled-pending):null,limit is int max&&enabled+pending>max);
    }
    public static async Task RequireAdditionalAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,DateTimeOffset time,CancellationToken ct)
    {var usage=await UsageAsync(c,tx,company,time,ct);if(usage.Limit is int max&&usage.Enabled+usage.Pending>=max)throw new SeatLimitReached(usage);}
}
