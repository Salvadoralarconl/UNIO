using Clary.Api.LocalIdentity;
using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api.Accounts;

public record AccountPage<T>(IReadOnlyList<T> Items,string? NextCursor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record AccountDeletionInput(int? Revision);
public sealed class ExistingAccountConflict:Exception;
// Signed opaque keyset cursors are bound to actor, surface, company, search and page size.
public sealed class AccountCursor
{
    private readonly byte[] key=RandomNumberGenerator.GetBytes(32);
    public string Encode(string scope,Guid last)
    {var payload=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Cursor(scope,last)));var tag=HMACSHA256.HashData(key,payload);return Convert.ToBase64String(payload.Concat(tag).ToArray());}
    public Guid? Decode(string scope,string? value)
    {
        if(value is null)return null;
        try{if(value.Length>5500)throw new FormatException();var bytes=Convert.FromBase64String(value);if(bytes.Length<=32||bytes.Length>4096)throw new FormatException();var payload=bytes[..^32];if(!CryptographicOperations.FixedTimeEquals(bytes[^32..],HMACSHA256.HashData(key,payload)))throw new FormatException();var cursor=JsonSerializer.Deserialize<Cursor>(payload)!;if(cursor.Scope!=scope)throw new FormatException();return cursor.Last;}
        catch(Exception e) when(e is FormatException or JsonException or ArgumentException){throw new ArgumentException("Invalid cursor for this query.");}
    }
    private record Cursor(string Scope,Guid Last);
}
public static class AccountEndpoints
{
    public static void AddAccountServices(this IServiceCollection services)
    {
        services.AddSingleton<SessionPresence>();services.AddSingleton<CompanyAccountRepository>();services.AddSingleton<InvitationService>();services.AddSingleton<AccountCursor>();
        services.ConfigureHttpJsonOptions(options=>options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false)));
    }
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapGet("/api/operator/audit",(HttpContext h,NpgsqlDataSource db,AccountCursor cursors,int? limit,string? cursor)=>Run(()=>ListAudit(db,cursors,AccountActor.For(Owner(h)),null,limit,cursor,h.RequestAborted))).WithMetadata(new OperatorPermission()).RequireAuthorization(CompanyPolicies.Operator);
        var operators=app.MapGroup("/api/operator/companies").WithMetadata(new OperatorPermission()).RequireAuthorization(CompanyPolicies.Operator);
        operators.MapGet("",(HttpContext h,NpgsqlDataSource db,AccountCursor cursors,string? search,int? limit,string? cursor)=>Run(()=>ListCompanies(db,cursors,Owner(h),search,limit,cursor,h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))operators.MapPost("",(LocalCompanyCreateInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.CreateCompanyAsync(Owner(h),input,h.RequestAborted)));
        else operators.MapPost("",(CompanyCreateInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.CreateAsync(Owner(h),input,h.RequestAborted)));
        operators.MapGet("/{company:guid}",(Guid company,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.DetailAsync(Owner(h),company,h.RequestAborted)));
        operators.MapPut("/{company:guid}",(Guid company,CompanyAdminUpdate input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.UpdateCompanyAsync(Owner(h),company,input,h.RequestAborted)));
        operators.MapPut("/{company:guid}/subscription",(Guid company,SubscriptionInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.SubscriptionAsync(Owner(h),company,input,h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))operators.MapPut("/{company:guid}/administrator",(Guid company,LocalAdministratorInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.AdministratorAsync(Owner(h),company,input,h.RequestAborted)));
        else operators.MapPut("/{company:guid}/administrator",(Guid company,AdministratorInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.AdministratorAsync(Owner(h),company,input,h.RequestAborted)));
        operators.MapGet("/{company:guid}/accounts",(Guid company,HttpContext h,NpgsqlDataSource db,AccountCursor cursors,string? search,int? limit,string? cursor)=>Run(()=>ListAccounts(db,cursors,AccountActor.For(Owner(h)),company,search,limit,cursor,h.RequestAborted)));
        operators.MapDelete("/{company:guid}/accounts/{user:guid}",(Guid company,Guid user,[Microsoft.AspNetCore.Mvc.FromBody] AccountDeletionInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.DeleteAccountAsync(AccountActor.For(Owner(h)),company,user,new RevisionInput(input.Revision),h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))operators.MapPost("/{company:guid}/accounts",(Guid company,LocalAccountInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.CreateAsync(AccountActor.For(Owner(h)),company,input,h.RequestAborted)));
        else operators.MapPost("/{company:guid}/accounts",(Guid company,AccountInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.CreateAccountAsync(AccountActor.For(Owner(h)),company,input,h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))operators.MapPut("/{company:guid}/accounts/{user:guid}",(Guid company,Guid user,LocalAccountInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.UpdateAsync(AccountActor.For(Owner(h)),company,user,input,h.RequestAborted)));
        else operators.MapPut("/{company:guid}/accounts/{user:guid}",(Guid company,Guid user,AccountInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.UpdateAccountAsync(AccountActor.For(Owner(h)),company,user,input,h.RequestAborted)));
        operators.MapGet("/{company:guid}/invitations",(Guid company,HttpContext h,NpgsqlDataSource db,AccountCursor cursors,string? search,int? limit,string? cursor)=>Run(()=>ListInvitations(db,cursors,AccountActor.For(Owner(h)),company,search,limit,cursor,h.RequestAborted)));
        operators.MapPost("/{company:guid}/invitations",(Guid company,InvitationInput input,HttpContext h,InvitationService repo)=>Run(()=>repo.IssueAsync(AccountActor.For(Owner(h)),company,input,h.RequestAborted)));
        operators.MapDelete("/{company:guid}/invitations/{id:guid}",(Guid company,Guid id,[Microsoft.AspNetCore.Mvc.FromBody] RevisionInput input,HttpContext h,InvitationService repo)=>Run(()=>repo.RevokeAsync(AccountActor.For(Owner(h)),company,id,input,h.RequestAborted)));
        var own=app.MapGroup("/api/company").WithMetadata(new CompanyPermission(CompanyCapability.AccountAdmin)).RequireAuthorization(CompanyPolicies.AccountAdmin);
        own.MapGet("/account-audit",(HttpContext h,NpgsqlDataSource db,AccountCursor cursors,int? limit,string? cursor)=>Run(()=>ListAudit(db,cursors,Actor(h),Company(h),limit,cursor,h.RequestAborted)));
        own.MapGet("/accounts",(HttpContext h,NpgsqlDataSource db,AccountCursor cursors,string? search,int? limit,string? cursor)=>Run(()=>ListAccounts(db,cursors,Actor(h),Company(h),search,limit,cursor,h.RequestAborted)));
        own.MapDelete("/accounts/{user:guid}",(Guid user,[Microsoft.AspNetCore.Mvc.FromBody] AccountDeletionInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.DeleteAccountAsync(Actor(h),Company(h),user,new RevisionInput(input.Revision),h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))own.MapPost("/accounts",(LocalAccountInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.CreateAsync(Actor(h),Company(h),input,h.RequestAborted)));
        else own.MapPost("/accounts",(AccountInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.CreateAccountAsync(Actor(h),Company(h),input,h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))own.MapPut("/accounts/{user:guid}",(Guid user,LocalAccountInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.UpdateAsync(Actor(h),Company(h),user,input,h.RequestAborted)));
        else own.MapPut("/accounts/{user:guid}",(Guid user,AccountInput input,HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.UpdateAccountAsync(Actor(h),Company(h),user,input,h.RequestAborted)));
        own.MapGet("/seats",(HttpContext h,CompanyAccountRepository repo)=>Run(()=>repo.SeatsAsync(Actor(h),Company(h),h.RequestAborted)));
        own.MapGet("/invitations",(HttpContext h,NpgsqlDataSource db,AccountCursor cursors,string? search,int? limit,string? cursor)=>Run(()=>ListInvitations(db,cursors,Actor(h),Company(h),search,limit,cursor,h.RequestAborted)));
        own.MapPost("/invitations",(InvitationInput input,HttpContext h,InvitationService repo)=>Run(()=>repo.IssueAsync(Actor(h),Company(h),input,h.RequestAborted)));
        own.MapDelete("/invitations/{id:guid}",(Guid id,[Microsoft.AspNetCore.Mvc.FromBody] RevisionInput input,HttpContext h,InvitationService repo)=>Run(()=>repo.RevokeAsync(Actor(h),Company(h),id,input,h.RequestAborted)));
        if(LocalIdentityEndpoints.IsLocal(app.Configuration))
        {
            operators.MapPost("/{company:guid}/accounts/{user:guid}/credential-link",(Guid company,Guid user,RevisionInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.IssueAsync(AccountActor.For(Owner(h)),company,user,input,h.RequestAborted)));
            own.MapPost("/accounts/{user:guid}/credential-link",(Guid user,RevisionInput input,HttpContext h,LocalProvisioningService repo)=>Run(()=>repo.IssueAsync(Actor(h),Company(h),user,input,h.RequestAborted)));
        }
        app.MapPost("/api/session/invitations/accept",(AcceptInvitationInput input,HttpContext h,InvitationService repo)=>Run(async()=>{var account=await repo.AcceptAsync(h.User,input.Token,h.RequestAborted);await h.SignOutAsync(IdentityEndpoints.CookieScheme);return new{account,freshLoginRequired=true};})).RequireAuthorization();
    }
    private static OperatorContext Owner(HttpContext h)=>h.Items[typeof(OperatorContext)] as OperatorContext??throw new CompanyDenied();
    private static AccountActor Actor(HttpContext h)=>AccountActor.For(CompanyPolicies.Context(h)??throw new CompanyDenied());
    private static Guid Company(HttpContext h)=>CompanyPolicies.Context(h)?.CompanyId??throw new CompanyDenied();
    internal static async Task<IResult> Run<T>(Func<Task<T>> work)
    {
        try{return Results.Ok(await work());}
        catch(SeatLimitReached e){return Results.Conflict(new{code="seat_limit_reached",usage=e.Usage,limit=e.Usage.Limit});}
        catch(ExistingAccountConflict){return Results.Conflict(new{code="existing_account",error="Manage or reactivate the existing account through account administration."});}
        catch(CompanyConflict){return Results.Conflict(new{code="account_conflict",error="Revision, identity or account invariant conflict."});}
        catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.UniqueViolation){return Results.Conflict(new{code="account_conflict",error="Identity or account invariant conflict."});}
        catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
        catch(CompanyMissing){return Results.NotFound();}
        catch(CompanyDenied){return Results.Forbid();}
    }
    private static (int Size,string Search) Query(int? limit,string? search)
    {if(limit is <1 or >100||search?.Length>200)throw new ArgumentException("Page size must be 1 to 100; search at most 200 characters.");return(limit??50,search?.Trim()??"");}
    private static string Scope(AccountActor actor,Guid? company,string surface,string search,int size)=>JsonSerializer.Serialize(new{actor.UserId,IsOperator=actor.Operator is not null,company,surface,search,size});
    private static NpgsqlCommand PageCommand(NpgsqlConnection c,NpgsqlTransaction tx,string sql,Guid? company,Guid? after,string search,int size)
    {var q=new NpgsqlCommand(sql,c,tx);if(company is Guid id)q.Parameters.AddWithValue("company",id);q.Parameters.AddWithValue("after",NpgsqlDbType.Uuid,(object?)after??DBNull.Value);q.Parameters.AddWithValue("search",search);q.Parameters.AddWithValue("size",size+1);return q;}
    private static async Task<AccountPage<AccountAuditDto>> ListAudit(NpgsqlDataSource database,AccountCursor cursors,AccountActor actor,Guid? company,int? limit,string? cursor,CancellationToken ct)
    {
        var query=Query(limit,null);var scope=Scope(actor,company,"audit","",query.Size);var after=cursors.Decode(scope,cursor);
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        if(company is Guid own)await CompanyAccountRepository.LockAsync(c,tx,actor,own,ct);else await CompanyAccountRepository.RevalidateOperatorAsync(c,tx,actor.UserId,ct,actor.Operator!.SessionId);
        await using var q=new NpgsqlCommand("SELECT id,actor_user_id,company_id,action,target_id,occurred_at,before_value::text,after_value::text FROM account_audit WHERE (@company IS NULL OR company_id=@company) AND (@after IS NULL OR id>@after) ORDER BY id LIMIT @size",c,tx);
        q.Parameters.AddWithValue("company",NpgsqlDbType.Uuid,(object?)company??DBNull.Value);q.Parameters.AddWithValue("after",NpgsqlDbType.Uuid,(object?)after??DBNull.Value);q.Parameters.AddWithValue("size",query.Size+1);
        var items=new List<AccountAuditDto>();await using(var r=await q.ExecuteReaderAsync(ct)){while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetGuid(1),r.IsDBNull(2)?null:r.GetGuid(2),r.GetString(3),r.IsDBNull(4)?null:r.GetGuid(4),new DateTimeOffset(r.GetDateTime(5)),r.IsDBNull(6)?null:JsonSerializer.Deserialize<JsonElement>(r.GetString(6)),r.IsDBNull(7)?null:JsonSerializer.Deserialize<JsonElement>(r.GetString(7))));}
        var more=items.Count>query.Size;if(more)items.RemoveAt(items.Count-1);return new(items,more?cursors.Encode(scope,items[^1].Id):null);
    }
    private record AccountAuditDto(Guid Id,Guid ActorUserId,Guid? CompanyId,string Action,Guid? TargetId,DateTimeOffset OccurredAt,JsonElement? BeforeValue,JsonElement? AfterValue);
    private static async Task<AccountPage<CompanyAdminDto>> ListCompanies(NpgsqlDataSource database,AccountCursor cursors,OperatorContext owner,string? search,int? limit,string? cursor,CancellationToken ct)
    {
        var query=Query(limit,search);var scope=Scope(AccountActor.For(owner),null,"companies",query.Search,query.Size);var after=cursors.Decode(scope,cursor);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await CompanyAccountRepository.RevalidateOperatorAsync(c,tx,owner.UserId,ct,owner.SessionId);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        var ids=new List<Guid>();await using(var q=PageCommand(c,tx,"SELECT id FROM companies WHERE (@after IS NULL OR id>@after) AND (@search='' OR strpos(lower(name),lower(@search))>0) ORDER BY id LIMIT @size",null,after,query.Search,query.Size)){await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add(r.GetGuid(0));}
        var more=ids.Count>query.Size;if(more)ids.RemoveAt(ids.Count-1);var items=new List<CompanyAdminDto>();foreach(var id in ids)items.Add(await CompanyAccountRepository.CompanyAsync(c,tx,id,time,ct));return new(items,more?cursors.Encode(scope,ids[^1]):null);
    }
    private static async Task<AccountPage<AccountDto>> ListAccounts(NpgsqlDataSource database,AccountCursor cursors,AccountActor actor,Guid company,string? search,int? limit,string? cursor,CancellationToken ct)
    {
        var query=Query(limit,search);var scope=Scope(actor,company,"accounts",query.Search,query.Size);var after=cursors.Decode(scope,cursor);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await CompanyAccountRepository.LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        var ids=new List<Guid>();await using(var q=PageCommand(c,tx,"SELECT u.id FROM users u JOIN memberships m ON m.user_id=u.id WHERE m.company_id=@company AND (@after IS NULL OR u.id>@after) AND (@search='' OR strpos(lower(u.display_name||' '||coalesce(u.email,'')),lower(@search))>0) ORDER BY u.id LIMIT @size",company,after,query.Search,query.Size)){await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add(r.GetGuid(0));}
        var more=ids.Count>query.Size;if(more)ids.RemoveAt(ids.Count-1);var items=new List<AccountDto>();foreach(var id in ids)items.Add(await CompanyAccountRepository.AccountAsync(c,tx,company,id,time,ct));return new(items,more?cursors.Encode(scope,ids[^1]):null);
    }
    private static async Task<AccountPage<InvitationDto>> ListInvitations(NpgsqlDataSource database,AccountCursor cursors,AccountActor actor,Guid company,string? search,int? limit,string? cursor,CancellationToken ct)
    {
        var query=Query(limit,search);var scope=Scope(actor,company,"invitations",query.Search,query.Size);var after=cursors.Decode(scope,cursor);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await CompanyAccountRepository.LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        var ids=new List<Guid>();await using(var q=PageCommand(c,tx,"SELECT id FROM account_invitations WHERE company_id=@company AND (@after IS NULL OR id>@after) AND (@search='' OR strpos(lower(email),lower(@search))>0) ORDER BY id LIMIT @size",company,after,query.Search,query.Size)){await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add(r.GetGuid(0));}
        var more=ids.Count>query.Size;if(more)ids.RemoveAt(ids.Count-1);var items=new List<InvitationDto>();foreach(var id in ids)items.Add(await InvitationService.ReadAsync(c,tx,company,id,time,ct));return new(items,more?cursors.Encode(scope,ids[^1]):null);
    }
}
