using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api.Accounts;

public record AccountInput(string Issuer,string Subject,string DisplayName,string? Email,[property:System.Text.Json.Serialization.JsonRequired] CompanyRole Role,[property:System.Text.Json.Serialization.JsonRequired] bool Enabled,int? Revision);
public record CompanyCreateInput(string Name,SubscriptionInput Subscription,AccountInput Administrator);
public record CompanyAdminUpdate(string Name,[property:System.Text.Json.Serialization.JsonRequired] bool Active,int? Revision);
public record AdministratorInput(AccountInput? Account,int? Revision,Guid? UserId=null,int? AccountRevision=null);
public record SubscriptionDto(PlanKind Kind,int? Limit,int Revision,SeatUsage Seats);
public record AccountActor(Guid UserId,OperatorContext? Operator,CompanyContext? Company)
{
    public static AccountActor For(OperatorContext owner)=>new(owner.UserId,owner,null);
    public static AccountActor For(CompanyContext company)=>new(company.UserId,null,company);
}
public delegate Task AccountTransactionHook(NpgsqlConnection c,NpgsqlTransaction tx,AccountDto account,CancellationToken ct);
public record AccountDeletionDto(Guid Id,bool Deleted,int Revision);
public sealed class CompanyAccountRepository(NpgsqlDataSource database)
{
    // Every caller locks company first, including disabled companies managed by the owner.
    internal static async Task LockAsync(NpgsqlConnection c,NpgsqlTransaction tx,AccountActor actor,Guid company,CancellationToken ct)
    {
        await using(var q=new NpgsqlCommand("SELECT id FROM companies WHERE id=@company FOR UPDATE",c,tx))
        {q.Parameters.AddWithValue("company",company);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyMissing();}
        if(actor.Operator is not null)await RevalidateOperatorAsync(c,tx,actor.UserId,ct,actor.Operator.SessionId);
        else
        {if(actor.Company is null||actor.Company.CompanyId!=company)throw new CompanyDenied();await AccountPermissions.RevalidateAsync(c,tx,actor.Company,CompanyCapability.AccountAdmin,ct);}
    }
    internal static async Task RevalidateOperatorAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid user,CancellationToken ct,Guid? session=null)
    {
        // Match invitation/account authority ordering: lock the user before its session.
        await using(var q=new NpgsqlCommand("SELECT u.id FROM operator_owner o JOIN users u ON u.id=o.user_id WHERE o.user_id=@user AND u.active AND u.issuer=o.issuer AND u.identity_subject=o.subject FOR SHARE OF u",c,tx))
        {q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyDenied();}
        await SessionPresence.RevalidateAsync(c,tx,user,session,ct);
    }
    internal static void Validate(AccountInput input,bool administrator=false)
    {
        if(!OperatorBootstrap.ValidBinding(input.Issuer,input.Subject)||string.IsNullOrWhiteSpace(input.DisplayName)||input.DisplayName.Trim().Length>(input.Issuer==Clary.Api.LocalIdentity.LocalCredentialRepository.Issuer?201:200)||input.Email?.Length>320||!Enum.IsDefined(input.Role))throw new ArgumentException("A valid trusted identity, display name and role are required.");
        if(!administrator&&input.Role==CompanyRole.Administrator)throw new CompanyDenied();
        if(input.Email is not null)InvitationService.NormalizeEmail(input.Email);
    }
    internal static void Revision(int? revision){if(revision is null or <1)throw new ArgumentException("Expected revision is required.");}
    private static void Name(string name){if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>200)throw new ArgumentException("Company name is required (maximum 200 characters).");}
    public async Task<CompanyAdminDto> CreateAsync(OperatorContext owner,CompanyCreateInput input,CancellationToken ct,AccountTransactionHook? provision=null)
    {
        Name(input.Name);if(input.Subscription is null||input.Administrator is null)throw new ArgumentException("Subscription and initial Administrator are required.");Validate(input.Administrator,true);var limit=SeatAccounting.Limit(input.Subscription);var id=Guid.NewGuid();
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using(var insert=new NpgsqlCommand("INSERT INTO companies(id,name,plan_kind,seat_limit) VALUES(@id,@name,@plan,@limit)",c,tx))
        {insert.Parameters.AddWithValue("id",id);insert.Parameters.AddWithValue("name",input.Name.Trim());insert.Parameters.AddWithValue("plan",input.Subscription.Kind.ToString());insert.Parameters.AddWithValue("limit",NpgsqlDbType.Integer,(object?)limit??DBNull.Value);await insert.ExecuteNonQueryAsync(ct);}
        await AssetConfigurationRepository.SeedDefaultsAsync(c,tx,id,ct);
        var actor=AccountActor.For(owner);await LockAsync(c,tx,actor,id,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        var added=await AddAsync(c,tx,actor,id,input.Administrator with{Role=CompanyRole.Administrator,Enabled=true},time,true,ct);
        if(provision is not null)await provision(c,tx,added,ct);
        await using(var setup=new NpgsqlCommand("SELECT seed_workspace_setup(@company,@user)",c,tx)){setup.Parameters.AddWithValue("company",id);setup.Parameters.AddWithValue("user",added.Id);await setup.ExecuteNonQueryAsync(ct);}
        var result=await CompanyAsync(c,tx,id,time,ct);await AccountAuditRepository.AppendAsync(c,tx,owner.UserId,id,"CompanyCreated",id,null,result,ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<CompanyAdminDto> UpdateCompanyAsync(OperatorContext owner,Guid company,CompanyAdminUpdate input,CancellationToken ct)
    {
        Name(input.Name);Revision(input.Revision);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await LockAsync(c,tx,AccountActor.For(owner),company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);var before=await CompanyAsync(c,tx,company,time,ct);
        if(before.Revision!=input.Revision)throw new CompanyConflict();
        await using(var q=new NpgsqlCommand("UPDATE companies SET name=@name,active=@active,revision=revision+1 WHERE id=@company",c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("name",input.Name.Trim());q.Parameters.AddWithValue("active",input.Active);await q.ExecuteNonQueryAsync(ct);}
        if(before.Active!=input.Active)await RevokeCompanyAsync(c,tx,company,ct);
        var result=await CompanyAsync(c,tx,company,time,ct);await AccountAuditRepository.AppendAsync(c,tx,owner.UserId,company,"CompanyUpdated",company,before,result,ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<SubscriptionDto> SubscriptionAsync(OperatorContext owner,Guid company,SubscriptionInput input,CancellationToken ct)
    {
        var limit=SeatAccounting.Limit(input);Revision(input.Revision);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await LockAsync(c,tx,AccountActor.For(owner),company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);var before=await SubscriptionReadAsync(c,tx,company,time,ct);
        if(before.Revision!=input.Revision)throw new CompanyConflict();
        await using(var q=new NpgsqlCommand("UPDATE companies SET plan_kind=@plan,seat_limit=@limit,subscription_revision=subscription_revision+1 WHERE id=@company",c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("plan",input.Kind.ToString());q.Parameters.AddWithValue("limit",NpgsqlDbType.Integer,(object?)limit??DBNull.Value);await q.ExecuteNonQueryAsync(ct);}
        var result=await SubscriptionReadAsync(c,tx,company,time,ct);await AccountAuditRepository.AppendAsync(c,tx,owner.UserId,company,"SubscriptionUpdated",company,before,result,ct);await tx.CommitAsync(ct);return result;
    }
    internal static async Task<SubscriptionDto> SubscriptionReadAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,DateTimeOffset time,CancellationToken ct)
    {
        PlanKind kind;int revision;await using(var q=new NpgsqlCommand("SELECT plan_kind,subscription_revision FROM companies WHERE id=@company",c,tx)){q.Parameters.AddWithValue("company",company);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();kind=Enum.Parse<PlanKind>(r.GetString(0));revision=r.GetInt32(1);}
        var seats=await SeatAccounting.UsageAsync(c,tx,company,time,ct);return new(kind,seats.Limit,revision,seats);
    }
    public async Task<AccountDto> CreateAccountAsync(AccountActor actor,Guid company,AccountInput input,CancellationToken ct,AccountTransactionHook? provision=null)
    {
        Validate(input);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);var result=await AddAsync(c,tx,actor,company,input,time,false,ct);if(provision is not null){await provision(c,tx,result,ct);result=await AccountAsync(c,tx,company,result.Id,await SeatAccounting.TimeAsync(c,tx,ct),ct);}await tx.CommitAsync(ct);return result;
    }
    internal static async Task<AccountDto> AddAsync(NpgsqlConnection c,NpgsqlTransaction tx,AccountActor actor,Guid company,AccountInput input,DateTimeOffset time,bool administrator,CancellationToken ct)
    {
        Validate(input,administrator);Guid user;
        await using(var q=new NpgsqlCommand("INSERT INTO users(id,issuer,identity_subject,display_name,email,active) VALUES(@id,@issuer,@subject,@name,@email,@enabled) ON CONFLICT(issuer,identity_subject) DO NOTHING RETURNING id",c,tx))
        {q.Parameters.AddWithValue("id",Guid.NewGuid());q.Parameters.AddWithValue("issuer",input.Issuer);q.Parameters.AddWithValue("subject",input.Subject);q.Parameters.AddWithValue("name",input.DisplayName.Trim());q.Parameters.AddWithValue("email",NpgsqlDbType.Text,(object?)input.Email??DBNull.Value);q.Parameters.AddWithValue("enabled",input.Enabled);user=await q.ExecuteScalarAsync(ct) is Guid id?id:Guid.Empty;}
        if(user==Guid.Empty)
        {
            await using var q=new NpgsqlCommand("SELECT id FROM users WHERE issuer=@issuer AND identity_subject=@subject",c,tx);q.Parameters.AddWithValue("issuer",input.Issuer);q.Parameters.AddWithValue("subject",input.Subject);user=(Guid)(await q.ExecuteScalarAsync(ct))!;
        }
        if(user==actor.UserId)throw new CompanyDenied();
        await using(var exists=new NpgsqlCommand("SELECT 1 FROM memberships WHERE user_id=@user",c,tx)){exists.Parameters.AddWithValue("user",user);if(await exists.ExecuteScalarAsync(ct) is not null)throw new CompanyConflict();}
        if(actor.Operator is null)await ProtectOwnerAsync(c,tx,user,ct);
        await using(var targetLock=new NpgsqlCommand("SELECT id FROM users WHERE id=@user FOR UPDATE",c,tx)){targetLock.Parameters.AddWithValue("user",user);await targetLock.ExecuteScalarAsync(ct);}
        if(input.Enabled)await SeatAccounting.RequireAdditionalAsync(c,tx,company,time,ct);
        var revision=await NewMembershipRevisionAsync(c,tx,user,ct);
        await using(var update=new NpgsqlCommand("UPDATE users SET display_name=@name,email=@email,active=@active WHERE id=@user; INSERT INTO memberships(company_id,user_id,role,active,revision) VALUES(@company,@user,@role,@active,@revision)",c,tx))
        {update.Parameters.AddWithValue("revision",revision);update.Parameters.AddWithValue("user",user);update.Parameters.AddWithValue("company",company);update.Parameters.AddWithValue("role",input.Role.ToString());update.Parameters.AddWithValue("name",input.DisplayName.Trim());update.Parameters.AddWithValue("email",NpgsqlDbType.Text,(object?)input.Email??DBNull.Value);update.Parameters.AddWithValue("active",input.Enabled);await update.ExecuteNonQueryAsync(ct);}
        await RevokeUserAsync(c,tx,user,ct);var result=await AccountAsync(c,tx,company,user,time,ct);await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"AccountCreated",user,null,result,ct);return result;
    }
    public async Task<AccountDto> UpdateAccountAsync(AccountActor actor,Guid company,Guid user,AccountInput input,CancellationToken ct,AccountTransactionHook? provision=null)
    {
        Revision(input.Revision);if(string.IsNullOrWhiteSpace(input.DisplayName)||input.DisplayName.Trim().Length>(input.Issuer==Clary.Api.LocalIdentity.LocalCredentialRepository.Issuer?201:200)||input.Email?.Length>320||!Enum.IsDefined(input.Role))throw new ArgumentException("A valid display name, role and expected revision are required.");if(input.Email is not null)InvitationService.NormalizeEmail(input.Email);
        if(input.Role==CompanyRole.Administrator&&actor.Operator is null)throw new CompanyDenied();
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);
        await LockTargetAsync(c,tx,company,user,ct);var before=await AccountAsync(c,tx,company,user,time,ct);
        if(user==actor.UserId&&(actor.Operator is null||before.Role!=input.Role))throw new CompanyDenied();
        if(actor.Operator is null){await ProtectOwnerAsync(c,tx,user,ct);if(before.Role==CompanyRole.Administrator)throw new CompanyDenied();}
        if((before.Role==CompanyRole.Administrator)!=(input.Role==CompanyRole.Administrator))throw new CompanyDenied(); // assignment/replacement is exclusively the separate operation
        if(before.Revision!=input.Revision)throw new CompanyConflict();
        if(input.Enabled&&!before.Enabled)await SeatAccounting.RequireAdditionalAsync(c,tx,company,time,ct);
        await using(var q=new NpgsqlCommand("UPDATE users SET display_name=@name,email=@email,active=@enabled WHERE id=@user; UPDATE memberships SET role=@role,active=@enabled,visibility_scope=CASE WHEN @role='Viewer' THEN visibility_scope ELSE 'Company' END,revision=revision+1 WHERE company_id=@company AND user_id=@user",c,tx))
        {q.Parameters.AddWithValue("user",user);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("name",input.DisplayName.Trim());q.Parameters.AddWithValue("email",NpgsqlDbType.Text,(object?)input.Email??DBNull.Value);q.Parameters.AddWithValue("enabled",input.Enabled);q.Parameters.AddWithValue("role",input.Role.ToString());await q.ExecuteNonQueryAsync(ct);}
        await RevokeUserAsync(c,tx,user,ct);if(provision is not null)await provision(c,tx,await AccountAsync(c,tx,company,user,time,ct),ct);var result=await AccountAsync(c,tx,company,user,time,ct);await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"AccountUpdated",user,before,result,ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<AccountDeletionDto> DeleteAccountAsync(AccountActor actor,Guid company,Guid user,RevisionInput input,CancellationToken ct)
    {
        Revision(input.Revision);
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await LockAsync(c,tx,actor,company,ct);await LockTargetAsync(c,tx,company,user,ct);
        var before=await AccountAsync(c,tx,company,user,await SeatAccounting.TimeAsync(c,tx,ct),ct);
        if(user==actor.UserId||before.Role is not (CompanyRole.Editor or CompanyRole.Viewer))throw new CompanyDenied();
        await ProtectOwnerAsync(c,tx,user,ct);
        if(before.Revision!=input.Revision)throw new CompanyConflict();
        // Preserve global identity, credentials and content. Only remove this company's association.
        await RevokeUserAsync(c,tx,user,ct);
        await using(var q=new NpgsqlCommand("DELETE FROM memberships WHERE company_id=@company AND user_id=@user",c,tx))
        {q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("user",user);await q.ExecuteNonQueryAsync(ct);}
        // Token/session waits may cross actor session expiry; check again before committing.
        await SessionPresence.RevalidateAsync(c,tx,actor.UserId,actor.Operator?.SessionId??actor.Company?.SessionId,ct);
        var result=new AccountDeletionDto(user,true,before.Revision+1);
        await AccountAuditRepository.AppendAsync(c,tx,actor.UserId,company,"AccountDeleted",user,before,result,ct);
        await tx.CommitAsync(ct);return result;
    }
    public async Task<CompanyAdminDto> AdministratorAsync(OperatorContext owner,Guid company,AdministratorInput input,CancellationToken ct,AccountTransactionHook? provision=null)
    {
        Revision(input.Revision);await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);var actor=AccountActor.For(owner);await LockAsync(c,tx,actor,company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);var beforeCompany=await CompanyAsync(c,tx,company,time,ct);if(beforeCompany.Revision!=input.Revision)throw new CompanyConflict();
        var account=input.Account;
        if(input.UserId is Guid selected)
        {
            if(account is not null)throw new ArgumentException("Select an existing account or supply a new identity, not both.");Revision(input.AccountRevision);
            await using var q=new NpgsqlCommand("SELECT u.issuer,u.identity_subject,u.display_name,u.email FROM users u JOIN memberships m ON m.user_id=u.id WHERE m.company_id=@company AND u.id=@user",c,tx);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("user",selected);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();account=new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),CompanyRole.Administrator,true,input.AccountRevision);
        }
        if(account is null)throw new ArgumentException("Administrator account is required.");if(input.UserId is null)Validate(account,true);
        Guid? target=null;await using(var q=new NpgsqlCommand("SELECT id FROM users WHERE issuer=@issuer AND identity_subject=@subject",c,tx)){q.Parameters.AddWithValue("issuer",account.Issuer);q.Parameters.AddWithValue("subject",account.Subject);target=await q.ExecuteScalarAsync(ct) as Guid?;}
        if(target==owner.UserId)throw new CompanyDenied();
        if(target is Guid targetId)
        {await using var q=new NpgsqlCommand("SELECT company_id FROM memberships WHERE user_id=@user",c,tx);q.Parameters.AddWithValue("user",targetId);if(await q.ExecuteScalarAsync(ct) is Guid memberCompany&&memberCompany!=company)throw new CompanyConflict();}
        var previous=new List<AccountDto>();await using(var q=new NpgsqlCommand("SELECT user_id FROM memberships WHERE company_id=@company AND role='Administrator' ORDER BY user_id",c,tx)){q.Parameters.AddWithValue("company",company);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))previous.Add(new(r.GetGuid(0),"",null,CompanyRole.Administrator,false,0,null,false));}
        // Self role changes are forbidden even when incidental to a replacement.
        if(previous.Any(x=>x.Id==owner.UserId&&x.Id!=target))throw new CompanyDenied();
        foreach(var old in previous.Where(x=>x.Id!=target))
        {
            await LockTargetAsync(c,tx,company,old.Id,ct);var before=await AccountAsync(c,tx,company,old.Id,time,ct);
            await using var q=new NpgsqlCommand("UPDATE memberships SET role='Editor',revision=revision+1 WHERE company_id=@company AND user_id=@user",c,tx);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("user",old.Id);await q.ExecuteNonQueryAsync(ct);await RevokeUserAsync(c,tx,old.Id,ct);await AccountAuditRepository.AppendAsync(c,tx,owner.UserId,company,"AdministratorDemoted",old.Id,before,await AccountAsync(c,tx,company,old.Id,time,ct),ct);
        }
        bool member=false;if(target is Guid known){await using var q=new NpgsqlCommand("SELECT 1 FROM memberships WHERE user_id=@user AND company_id=@company",c,tx);q.Parameters.AddWithValue("user",known);q.Parameters.AddWithValue("company",company);member=await q.ExecuteScalarAsync(ct) is not null;}
        if(!member){var added=await AddAsync(c,tx,actor,company,account with{Role=CompanyRole.Administrator,Enabled=true},time,true,ct);if(provision is not null)await provision(c,tx,added,ct);
}
        else
        {
            var user=target!.Value;await LockTargetAsync(c,tx,company,user,ct);var before=await AccountAsync(c,tx,company,user,time,ct);Revision(account.Revision);if(before.Revision!=account.Revision)throw new CompanyConflict();if(!before.Enabled)await SeatAccounting.RequireAdditionalAsync(c,tx,company,time,ct);
            await using(var q=new NpgsqlCommand("UPDATE users SET active=true WHERE id=@user; UPDATE memberships SET role='Administrator',active=true,visibility_scope='Company',revision=revision+1 WHERE user_id=@user AND company_id=@company",c,tx)){q.Parameters.AddWithValue("user",user);q.Parameters.AddWithValue("company",company);await q.ExecuteNonQueryAsync(ct);}
            await RevokeUserAsync(c,tx,user,ct);await AccountAuditRepository.AppendAsync(c,tx,owner.UserId,company,"AdministratorAssigned",user,before,await AccountAsync(c,tx,company,user,time,ct),ct);
        }
        await using(var q=new NpgsqlCommand("UPDATE companies SET revision=revision+1 WHERE id=@company",c,tx)){q.Parameters.AddWithValue("company",company);await q.ExecuteNonQueryAsync(ct);}
        var result=await CompanyAsync(c,tx,company,time,ct);await AccountAuditRepository.AppendAsync(c,tx,owner.UserId,company,"AdministratorReplaced",company,beforeCompany,result,ct);await tx.CommitAsync(ct);return result;
    }
    internal static async Task<int> NewMembershipRevisionAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid user,CancellationToken ct)
    {
        // The retained user lock serializes membership lifetimes across companies.
        await using(var locked=new NpgsqlCommand("SELECT id FROM users WHERE id=@user FOR UPDATE",c,tx))
        {locked.Parameters.AddWithValue("user",user);if(await locked.ExecuteScalarAsync(ct) is null)throw new CompanyMissing();}
        // AccountDeleted audit is immutable and committed atomically with removal. Its after
        // revision is a durable high-water mark, including every edit of the removed lifetime.
        await using var q=new NpgsqlCommand("SELECT coalesce(max((after_value->>'Revision')::integer),0)+1 FROM account_audit WHERE action='AccountDeleted' AND target_id=@user",c,tx);
        q.Parameters.AddWithValue("user",user);return (int)(await q.ExecuteScalarAsync(ct))!;
    }
    internal static async Task ProtectOwnerAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid user,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT 1 FROM operator_owner WHERE user_id=@user",c,tx);q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is not null)throw new CompanyDenied();}
    internal static async Task LockTargetAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,Guid user,CancellationToken ct)
    {
        // user before membership, after company; never bare-reactivate a user before the company lock.
        await using var q=new NpgsqlCommand("SELECT u.id FROM users u JOIN memberships m ON m.user_id=u.id WHERE m.company_id=@company AND u.id=@user FOR UPDATE OF u,m",c,tx);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("user",user);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyMissing();
    }
    internal static async Task RevokeUserAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid user,CancellationToken ct)
    {await using var q=new NpgsqlCommand("UPDATE local_credential_tokens SET revoked_at=clock_timestamp() WHERE user_id=@user AND revoked_at IS NULL AND consumed_at IS NULL; UPDATE account_sessions SET revoked_at=clock_timestamp() WHERE user_id=@user AND revoked_at IS NULL",c,tx);q.Parameters.AddWithValue("user",user);await q.ExecuteNonQueryAsync(ct);}
    internal static async Task RevokeCompanyAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,CancellationToken ct)
    {await using var q=new NpgsqlCommand("UPDATE local_credential_tokens t SET revoked_at=clock_timestamp() FROM memberships m WHERE m.company_id=@company AND m.user_id=t.user_id AND t.revoked_at IS NULL AND t.consumed_at IS NULL; UPDATE account_sessions s SET revoked_at=clock_timestamp() FROM memberships m WHERE m.company_id=@company AND m.user_id=s.user_id AND s.revoked_at IS NULL",c,tx);q.Parameters.AddWithValue("company",company);await q.ExecuteNonQueryAsync(ct);}
    internal static async Task<AccountDto> AccountAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,Guid user,DateTimeOffset time,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("""
            SELECT u.id,u.display_name,u.email,m.role,(m.active AND u.active),m.revision,
            (SELECT max(last_activity_at) FROM account_sessions WHERE user_id=u.id),
            (m.active AND u.active AND co.active AND EXISTS(SELECT 1 FROM account_sessions WHERE user_id=u.id AND revoked_at IS NULL AND expires_at>@time AND last_activity_at>@time-interval '5 minutes'))
            FROM memberships m JOIN users u ON u.id=m.user_id JOIN companies co ON co.id=m.company_id WHERE m.company_id=@company AND u.id=@user
            """,c,tx);q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("user",user);q.Parameters.AddWithValue("time",time);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();var result=new AccountDto(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),Enum.Parse<CompanyRole>(r.GetString(3)),r.GetBoolean(4),r.GetInt32(5),r.IsDBNull(6)?null:new DateTimeOffset(r.GetDateTime(6)),r.GetBoolean(7));await r.DisposeAsync();return result with{LocalProfile=await new Clary.Api.LocalIdentity.LocalCredentialRepository().ProfileAsync(c,tx,user,ct)};
    }
    internal static async Task<CompanyAdminDto> CompanyAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid company,DateTimeOffset time,CancellationToken ct)
    {
        string name;bool active,needs;int revision,online;PlanKind plan;DateTimeOffset? last;
        await using(var q=new NpgsqlCommand("""
            SELECT c.name,c.active,c.revision,c.plan_kind,
            NOT EXISTS(SELECT 1 FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=c.id AND m.active AND u.active AND m.role='Administrator'),
            (SELECT count(DISTINCT s.user_id)::integer FROM account_sessions s JOIN memberships m ON m.user_id=s.user_id JOIN users u ON u.id=s.user_id WHERE m.company_id=c.id AND c.active AND m.active AND u.active AND s.revoked_at IS NULL AND s.expires_at>@time AND s.last_activity_at>@time-interval '5 minutes'),
            (SELECT max(s.last_activity_at) FROM account_sessions s JOIN memberships m ON m.user_id=s.user_id JOIN users u ON u.id=s.user_id WHERE m.company_id=c.id)
            FROM companies c WHERE id=@company
            """,c,tx)){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("time",time);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();name=r.GetString(0);active=r.GetBoolean(1);revision=r.GetInt32(2);plan=Enum.Parse<PlanKind>(r.GetString(3));needs=r.GetBoolean(4);online=r.GetInt32(5);last=r.IsDBNull(6)?null:new DateTimeOffset(r.GetDateTime(6));}
        var seats=await SeatAccounting.UsageAsync(c,tx,company,time,ct);return new(company,name,active,revision,plan,seats.Limit,seats,online,last,needs);
    }
    public async Task<SeatUsage> SeatsAsync(AccountActor actor,Guid company,CancellationToken ct)
    {await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await LockAsync(c,tx,actor,company,ct);return await SeatAccounting.UsageAsync(c,tx,company,await SeatAccounting.TimeAsync(c,tx,ct),ct);}
    public async Task<object> DetailAsync(OperatorContext owner,Guid company,CancellationToken ct)
    {await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await LockAsync(c,tx,AccountActor.For(owner),company,ct);var time=await SeatAccounting.TimeAsync(c,tx,ct);return new{company=await CompanyAsync(c,tx,company,time,ct),subscription=await SubscriptionReadAsync(c,tx,company,time,ct)};}
    public async Task<MembershipDto> LegacySaveAsync(CompanyContext context,MembershipInput input,bool update,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.Subject)||input.Subject.Length>500||string.IsNullOrWhiteSpace(input.Issuer)||input.Issuer.Length>2000
            ||!Enum.TryParse<CompanyRole>(input.Role,out var role)||!Enum.IsDefined(role)||input.Role!=role.ToString()
            ||(update&&input.Revision is null or <1))throw new ArgumentException("A valid identity, role and expected revision are required.");
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        // All membership writers lock the company first, serializing the last-active-Admin invariant.
        await LockAsync(c,tx,AccountActor.For(context),context.CompanyId,ct);
        if(!update||role==CompanyRole.Administrator)throw new CompanyDenied();
        Guid userId;
        if(input.EmployeeId is Guid employee)
        {
            await using var valid=new NpgsqlCommand("SELECT 1 FROM employees WHERE company_id=@company AND id=@employee AND active",c,tx);
            valid.Parameters.AddWithValue("company",context.CompanyId);valid.Parameters.AddWithValue("employee",employee);
            if(await valid.ExecuteScalarAsync(ct) is null)throw new CompanyMissing();
        }
        await using(var find=new NpgsqlCommand("SELECT id FROM users WHERE issuer=@issuer AND identity_subject=@subject",c,tx))
        {
            find.Parameters.AddWithValue("issuer",input.Issuer);find.Parameters.AddWithValue("subject",input.Subject);
            var found=await find.ExecuteScalarAsync(ct);
            if(found is not Guid id)throw new CompanyMissing();
            userId=id;
        }
        if(userId==context.UserId)throw new CompanyDenied();
        await ProtectOwnerAsync(c,tx,userId,ct);
        var time=await SeatAccounting.TimeAsync(c,tx,ct);
        await LockTargetAsync(c,tx,context.CompanyId,userId,ct);
        var accountBefore=await AccountAsync(c,tx,context.CompanyId,userId,time,ct);
        var before=await AuditRepository.SnapshotAsync(c,tx,context,"memberships","user_id",userId,ct);
        bool enabledBefore,enabledAfter;
        await using(var current=new NpgsqlCommand("SELECT m.role,m.revision,u.active,m.active FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=@company AND m.user_id=@user FOR UPDATE OF m,u",c,tx))
        {
            current.Parameters.AddWithValue("company",context.CompanyId);current.Parameters.AddWithValue("user",userId);
            await using var r=await current.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyMissing();
            if(r.GetString(0)=="Administrator")throw new CompanyDenied();
            if(r.GetInt32(1)!=input.Revision)throw new CompanyConflict();
            var userActive=r.GetBoolean(2);
            enabledBefore=userActive&&r.GetBoolean(3);
            // This compatibility writer changes membership activity only, not users.active.
            enabledAfter=userActive&&input.Active;
        }
        if(enabledAfter&&!enabledBefore)await SeatAccounting.RequireAdditionalAsync(c,tx,context.CompanyId,time,ct);
        await using var save=new NpgsqlCommand("UPDATE memberships SET role=@role,active=@active,employee_id=@employee,visibility_scope=CASE WHEN @role='Viewer' THEN visibility_scope ELSE 'Company' END,revision=revision+1 WHERE company_id=@company AND user_id=@user RETURNING revision",c,tx);
        save.Parameters.AddWithValue("company",context.CompanyId);save.Parameters.AddWithValue("user",userId);save.Parameters.AddWithValue("role",input.Role);save.Parameters.AddWithValue("active",input.Active);save.Parameters.AddWithValue("employee",NpgsqlDbType.Uuid,(object?)input.EmployeeId??DBNull.Value);
        var saved=await save.ExecuteScalarAsync(ct);if(saved is null)throw new CompanyConflict();await AuditRepository.AppendAsync(c,tx,context,"Updated","membership",userId,before,new MembershipDto(userId,input.Subject,input.Issuer,input.Role,input.Active,input.EmployeeId,(int)saved),ct);
        await RevokeUserAsync(c,tx,userId,ct);
        await AccountAuditRepository.AppendAsync(c,tx,context.UserId,context.CompanyId,"LegacyAccountUpdated",userId,accountBefore,await AccountAsync(c,tx,context.CompanyId,userId,time,ct),ct);
        await tx.CommitAsync(ct);
        return new(userId,input.Subject,input.Issuer,input.Role,input.Active,input.EmployeeId,(int)saved);
    }
}

