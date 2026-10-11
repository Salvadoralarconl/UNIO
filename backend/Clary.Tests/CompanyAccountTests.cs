using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api.Accounts;
using Clary.Api.Identity;
using Xunit;

public class CompanyAccountTests
{
    [Fact] public async Task LegacyCompanyHttpEditRecordsTokenFreePlatformAudit()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();
        var response=await f.Send(HttpMethod.Put,"/api/company",new{name="Renamed legacy",revision=1});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_audit"));
        Assert.Equal(IdentityFixture.User,await f.Scalar("SELECT actor_user_id FROM account_audit"));
        Assert.Equal(IdentityFixture.Company,await f.Scalar("SELECT company_id FROM account_audit"));
        Assert.Equal(IdentityFixture.Company,await f.Scalar("SELECT target_id FROM account_audit"));
        Assert.Equal("CompanyUpdated",await f.Scalar("SELECT action FROM account_audit"));
        var audit=JsonDocument.Parse((string)(await f.Scalar("SELECT jsonb_build_object('before',before_value,'after',after_value)::text FROM account_audit"))!);
        Assert.Equal("Legacy company",audit.RootElement.GetProperty("before").GetProperty("Name").GetString());
        Assert.Equal(1,audit.RootElement.GetProperty("before").GetProperty("Revision").GetInt32());
        Assert.Equal("Renamed legacy",audit.RootElement.GetProperty("after").GetProperty("Name").GetString());
        Assert.Equal(2,audit.RootElement.GetProperty("after").GetProperty("Revision").GetInt32());
        Assert.DoesNotContain("token",audit.RootElement.GetRawText(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,"/api/company",new{name="Stale",revision=1})).StatusCode);
        Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
    [Fact] public async Task LegacyCompanyHttpEditRollsBackBothAuditsWhenPlatformAuditFails()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();
        var before=await f.Scalar("SELECT row_to_json(c)::text FROM companies c");
        await f.Sql("CREATE FUNCTION reject_company_account_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'company account audit unavailable'; END $$; CREATE TRIGGER reject_company_account_audit BEFORE INSERT ON account_audit FOR EACH ROW EXECUTE FUNCTION reject_company_account_audit()");
        var response=await f.Send(HttpMethod.Put,"/api/company",new{name="Must roll back",revision=1});
        Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);
        Assert.Equal(before,await f.Scalar("SELECT row_to_json(c)::text FROM companies c"));
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_audit"));
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
    internal static object Account(string subject,bool enabled=true,string role="Editor",int? revision=null)=>new {issuer="https://accounts.example",subject,displayName=subject,email=subject+"@example.test",role,enabled,revision};
    internal static async Task<Guid> Owner(IdentityFixture f)
    {
        var owner=await new OperatorBootstrap(f.Database).InitializeAsync("https://owner.example","owner");
        f.SignInAs("https://owner.example","owner");return owner.UserId;
    }
    [Fact] public async Task OwnAccountEndpointsEnforceRolesRevisionsAndSelfProtection()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();
        var response=await f.Send(HttpMethod.Post,"/api/company/accounts",Account("editor"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var row=await response.Content.ReadFromJsonAsync<JsonElement>();var id=row.GetProperty("id").GetGuid();
        Assert.Equal(1,row.GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{id}",Account("ignored",revision:99))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{IdentityFixture.User}",Account("ignored",role:"Viewer",revision:1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{id}",Account("ignored",role:"Administrator",revision:1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{id}",Account("editor",false,"Viewer",1))).StatusCode);
        Assert.Equal(false,await f.Scalar($"SELECT active FROM memberships WHERE user_id='{id}'"));
        Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
    [Fact] public async Task CompanyCreateAdministratorReplacementSuspensionAndSelfRoleDenial()=>await IdentityFixture.Run(async f=>
    {
        var owner=await Owner(f);
        var created=await f.Send(HttpMethod.Post,"/api/operator/companies",new {name="New company",subscription=new {kind="Ten"},administrator=Account("first-admin")});
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var company=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM memberships WHERE company_id='{company}' AND role='Administrator'"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{company}/administrator",new {account=Account("second-admin"),revision=1})).StatusCode);
        Assert.Equal("Editor",await f.Scalar("SELECT m.role FROM memberships m JOIN users u ON u.id=m.user_id WHERE identity_subject='first-admin'"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{company}",new {name="Suspended",active=false,revision=2})).StatusCode);
        Assert.Equal(2L,await f.Scalar($"SELECT count(*) FROM memberships WHERE company_id='{company}'"));
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{company}",new {name="Stale",active=true,revision=2})).StatusCode);
        await f.Sql($"INSERT INTO memberships(company_id,user_id,role) VALUES('{IdentityFixture.Company}','{owner}','Editor')");
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{owner}",Account("owner",role:"Viewer",revision:1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/administrator",new {account=new {issuer="https://owner.example",subject="owner",displayName="Owner",role="Editor",enabled=true},revision=1})).StatusCode);
    });
    [Fact] public async Task AdministratorCannotMutateOwnerAndForeignIdentityCannotJoin()=>await IdentityFixture.Run(async f=>
    {
        var owner=await Owner(f);await f.Sql($"INSERT INTO memberships(company_id,user_id,role) VALUES('{IdentityFixture.Company}','{owner}','Editor')");await f.Login();
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{owner}",Account("owner",false,"Viewer",1))).StatusCode);
        var foreign=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign}','Foreign')");
        await Owner(f);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,$"/api/operator/companies/{foreign}/accounts",new {issuer="https://owner.example",subject="owner",displayName="Owner",role="Editor",enabled=true})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/operator/companies/{IdentityFixture.Company}/accounts",Account("reused"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,$"/api/operator/companies/{foreign}/accounts",Account("reused"))).StatusCode);
    });
    [Fact] public async Task AuditFailureRollsBackAccount()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();await f.Sql("CREATE FUNCTION reject_account_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit unavailable'; END $$; CREATE TRIGGER reject_account_audit BEFORE INSERT ON account_audit FOR EACH ROW EXECUTE FUNCTION reject_account_audit()");
        var response=await f.Send(HttpMethod.Post,"/api/company/accounts",Account("rollback"));Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>new CompanyAccountRepository(f.Database).CreateAccountAsync(AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null)),IdentityFixture.Company,new("https://accounts.example","rollback-direct","Rollback",null,CompanyRole.Editor,true,null),default));Assert.Equal("P0001",error.SqlState);
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM users WHERE identity_subject='rollback'"));
    });
    [Fact] public async Task PaginationIsOpaqueBoundAndPresenceCountsDistinctActiveUsers()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();await f.Send(HttpMethod.Post,"/api/company/accounts",Account("list1"));await f.Send(HttpMethod.Post,"/api/company/accounts",Account("list2"));
        await f.Sql($"INSERT INTO account_sessions(id,user_id,expires_at,last_activity_at) VALUES(gen_random_uuid(),'{IdentityFixture.User}',clock_timestamp()+interval '1 hour',clock_timestamp()),(gen_random_uuid(),'{IdentityFixture.User}',clock_timestamp()+interval '1 hour',clock_timestamp())");
        var first=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/accounts?limit=1");Assert.Single(first.GetProperty("items").EnumerateArray());var cursor=first.GetProperty("nextCursor").GetString()!;
        var next=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/accounts?limit=1&cursor="+Uri.EscapeDataString(cursor));Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetGuid(),next.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.GetAsync("/api/company/accounts?limit=2&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.GetAsync("/api/company/invitations?limit=1&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.GetAsync("/api/company/accounts?limit=101")).StatusCode);
        await Owner(f);var detail=await f.Client.GetFromJsonAsync<JsonElement>($"/api/operator/companies/{IdentityFixture.Company}");Assert.Equal(1,detail.GetProperty("company").GetProperty("onlineUsers").GetInt32());Assert.Equal("Unlimited",detail.GetProperty("subscription").GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.GetAsync($"/api/operator/companies/{IdentityFixture.Company}/accounts?limit=1&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
    });
    [Fact] public async Task MissingInputDiscriminantsAndNullNestedInputsAreBadRequests()=>await IdentityFixture.Run(async f=>
    {
        await Owner(f);
        foreach(var input in new object[]{new{revision=1},new{kind=0,revision=1},new{kind="Custom",customLimit=100.5,revision=1}})
        {var response=await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/subscription",input);Assert.True(response.StatusCode==HttpStatusCode.BadRequest,await response.Content.ReadAsStringAsync());}
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,"/api/operator/companies",new{name="Malformed",subscription=(object?)null,administrator=(object?)null})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,$"/api/operator/companies/{IdentityFixture.Company}/accounts",new{issuer="https://accounts.example",subject="missing-role",displayName="Missing",enabled=true})).StatusCode);
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
    [Fact] public async Task ExistingAdministratorTargetRequiresAccountRevisionAndReplacementFailureIsAtomic()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var response=await f.Send(HttpMethod.Post,"/api/company/accounts",Account("replacement"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);var user=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();await Owner(f);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/administrator",new{account=Account("replacement",revision:99),revision=1})).StatusCode);
        Assert.Equal("Administrator",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{IdentityFixture.User}'"));Assert.Equal("Editor",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{user}'"));Assert.Equal(1,await f.Scalar("SELECT revision FROM companies"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/administrator",new{userId=user,accountRevision=1,revision=1})).StatusCode);
        Assert.Equal("Administrator",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{user}'"));Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships WHERE active"));
    });
    [Fact] public async Task StaleAuthorityCannotCommitAndAccountChangesRevokeSessions()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var response=await f.Send(HttpMethod.Post,"/api/company/accounts",Account("session-target"));var user=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await f.Sql($"INSERT INTO account_sessions(id,user_id,expires_at) VALUES(gen_random_uuid(),'{user}',clock_timestamp()+interval '1 hour')");
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{user}",Account("session-target",false,revision:1))).StatusCode);Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM account_sessions WHERE user_id='{user}' AND revoked_at IS NULL"));
        var repo=new CompanyAccountRepository(f.Database);var actor=AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));await f.Sql($"UPDATE memberships SET role='Viewer' WHERE user_id='{IdentityFixture.User}'");
        await Assert.ThrowsAsync<CompanyDenied>(()=>repo.CreateAccountAsync(actor,IdentityFixture.Company,new("https://accounts.example","stale","Stale",null,CompanyRole.Editor,true,null),default));
        var owner=await Owner(f);await f.Sql($"UPDATE users SET active=false WHERE id='{owner}'");
        await Assert.ThrowsAsync<CompanyDenied>(()=>repo.SubscriptionAsync(new(owner),IdentityFixture.Company,new(PlanKind.Ten,null,1),default));Assert.Equal("Unlimited",await f.Scalar("SELECT plan_kind FROM companies"));
    });
    [Fact] public async Task OperatorCanDisableAndReactivateAdministratorWithoutReassigningRole()=>await IdentityFixture.Run(async f=>
    {
        await Owner(f);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{IdentityFixture.User}",new{displayName="Bootstrap",email=(string?)null,role="Administrator",enabled=false,revision=1})).StatusCode);
        var detail=await f.Client.GetFromJsonAsync<JsonElement>($"/api/operator/companies/{IdentityFixture.Company}");Assert.True(detail.GetProperty("company").GetProperty("needsAdministrator").GetBoolean());Assert.Equal(0,detail.GetProperty("company").GetProperty("seats").GetProperty("enabled").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{IdentityFixture.User}",new{displayName="Bootstrap",role="Administrator",enabled=true,revision=2})).StatusCode);
        Assert.Equal("Administrator",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{IdentityFixture.User}'"));
    });
    [Fact] public async Task RealAccountSurfacesDenyForeignUsersEditorsAndUnboundOperators()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/operator/companies")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/operator/companies",new{name="Denied",subscription=new{kind="Ten"},administrator=Account("denied")})).StatusCode);
        var foreign=new CompanyContext(Guid.NewGuid(),Guid.NewGuid(),CompanyRole.Editor,null,null);await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign.CompanyId}','Foreign')");await FixtureCompany.AddActorAsync(f.Database,foreign);
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{foreign.UserId}",Account("foreign",false,revision:1))).StatusCode);
        await f.Sql($"UPDATE memberships SET role='Editor' WHERE user_id='{IdentityFixture.User}'");
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/company/accounts")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/company/invitations",new{email="denied@example.test",role="Viewer"})).StatusCode);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
}
