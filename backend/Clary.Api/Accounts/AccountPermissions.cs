using Clary.Api.Identity;
using Npgsql;
namespace Clary.Api.Accounts;

public enum CompanyCapability { Read, OwnInventory, Write, Delete, Approve, Index, Repair, Settings, AccountAdmin }
public sealed record OperatorPermission;
public sealed record CompanyPermission(CompanyCapability Capability,bool Action=false);
public sealed record CompanyCapabilities(bool Read,bool OwnInventory,bool Write,bool Delete,bool Approve,bool Index,bool Repair,bool Settings,bool AccountAdmin);
public static class AccountPermissions
{
    public static bool Allows(CompanyContext? context,CompanyCapability capability)
    {
        if(context is null||!Enum.IsDefined(context.Role)||!Enum.IsDefined(context.VisibilityScope))return false;
        if(capability==CompanyCapability.OwnInventory)return true;
        if(context.VisibilityScope!=CompanyVisibilityScope.Company)return false;
        return capability switch {
            CompanyCapability.Read=>true,
            CompanyCapability.Write or CompanyCapability.Approve or CompanyCapability.Index or CompanyCapability.Repair=>context.Role is CompanyRole.Administrator or CompanyRole.Editor,
            CompanyCapability.Delete or CompanyCapability.Settings or CompanyCapability.AccountAdmin=>context.Role==CompanyRole.Administrator,
            _=>false
        };
    }
    public static CompanyCapabilities Capabilities(CompanyContext? c)=>new(Allows(c,CompanyCapability.Read),Allows(c,CompanyCapability.OwnInventory),Allows(c,CompanyCapability.Write),Allows(c,CompanyCapability.Delete),Allows(c,CompanyCapability.Approve),Allows(c,CompanyCapability.Index),Allows(c,CompanyCapability.Repair),Allows(c,CompanyCapability.Settings),Allows(c,CompanyCapability.AccountAdmin));
    public static async Task<int> ExecuteAsync(NpgsqlDataSource database,NpgsqlCommand command,CompanyContext context,CompanyCapability capability,CancellationToken ct)
        => await ExecuteAsync(database,command,context,capability,c=>c.ExecuteNonQueryAsync(ct),ct);
    public static async Task<object?> ScalarAsync(NpgsqlDataSource database,NpgsqlCommand command,CompanyContext context,CompanyCapability capability,CancellationToken ct)
        => await ExecuteAsync(database,command,context,capability,c=>c.ExecuteScalarAsync(ct),ct);
    private static async Task<T> ExecuteAsync<T>(NpgsqlDataSource database,NpgsqlCommand command,CompanyContext context,CompanyCapability capability,Func<NpgsqlCommand,Task<T>> execute,CancellationToken ct)
    {
        await using var connection=await database.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        await RevalidateAsync(connection,tx,context,capability,ct);
        await using var guarded=new NpgsqlCommand(command.CommandText,connection,tx);
        foreach(NpgsqlParameter parameter in command.Parameters)guarded.Parameters.Add(parameter.Clone());
        var result=await execute(guarded);await tx.CommitAsync(ct);return result;
    }
    // Lock the company first, then resolve current persisted authority inside the mutation transaction.
    public static async Task RevalidateAsync(NpgsqlConnection connection,NpgsqlTransaction tx,CompanyContext context,CompanyCapability capability,CancellationToken ct)
    {
        await using(var company=new NpgsqlCommand("SELECT id FROM companies WHERE id=@company AND active FOR UPDATE",connection,tx))
        {company.Parameters.AddWithValue("company",context.CompanyId);if(await company.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
        await using var q=new NpgsqlCommand("SELECT m.role,m.visibility_scope FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=@company AND m.user_id=@user AND m.active AND u.active FOR SHARE OF m,u",connection,tx);
        q.Parameters.AddWithValue("company",context.CompanyId);q.Parameters.AddWithValue("user",context.UserId);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct)||!Allows(context with {Role=Enum.Parse<CompanyRole>(r.GetString(0)),VisibilityScope=Enum.Parse<CompanyVisibilityScope>(r.GetString(1))},capability))throw new CompanyDenied();
        await r.DisposeAsync();
        await SessionPresence.RevalidateAsync(connection,tx,context.UserId,context.SessionId,ct);
    }
}

