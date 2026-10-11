using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api.Identity;
using Xunit;

public class SeatAccountingTests
{
    [Theory] [InlineData("Ten",null,10)] [InlineData("Twenty",null,20)] [InlineData("Fifty",null,50)] [InlineData("Hundred",null,100)] [InlineData("Custom",101,101)] [InlineData("Unlimited",null,null)]
    public async Task PlansHaveExactLimits(string kind,int? custom,int? limit)=>await IdentityFixture.Run(async f=>
    {
        await CompanyAccountTests.Owner(f);
        var response=await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/subscription",new {kind,customLimit=custom,revision=1});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(limit,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("limit").Deserialize<int?>());
        Assert.Equal(1,await f.Scalar("SELECT revision FROM companies"));Assert.Equal(2,await f.Scalar("SELECT subscription_revision FROM companies"));
    });
    [Theory] [InlineData("Custom",100)] [InlineData("Custom",null)] [InlineData("Ten",11)] [InlineData("Unlimited",1)] [InlineData("NoPlan",null)]
    public async Task InvalidPlansFailWithoutAudit(string kind,int? custom)=>await IdentityFixture.Run(async f=>
    {
        await CompanyAccountTests.Owner(f);Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/subscription",new {kind,customLimit=custom,revision=1})).StatusCode);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
    internal static async Task Fill(IdentityFixture f,int count)
    {
        for(var i=0;i<count;i++) {var c=new CompanyContext(IdentityFixture.Company,Guid.NewGuid(),CompanyRole.Editor,null,null);await FixtureCompany.AddActorAsync(f.Database,c);}
    }
    [Fact] public async Task ReductionPreservesUsersDisabledReleasesSeatAndLegacyCannotBypass()=>await IdentityFixture.Run(async f=>
    {
        await Fill(f,10);await CompanyAccountTests.Owner(f);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/subscription",new {kind="Ten",revision=1})).StatusCode);
        await f.Login();var seats=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.True(seats.GetProperty("overLimit").GetBoolean());Assert.Equal(11,seats.GetProperty("enabled").GetInt32());
        var full=await f.Send(HttpMethod.Post,"/api/company/accounts",CompanyAccountTests.Account("full"));Assert.Equal(HttpStatusCode.Conflict,full.StatusCode);var error=await full.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("seat_limit_reached",error.GetProperty("code").GetString());Assert.Equal(10,error.GetProperty("limit").GetInt32());Assert.Equal(11,error.GetProperty("usage").GetProperty("enabled").GetInt32());
        var target=(Guid)(await f.Scalar($"SELECT user_id FROM memberships WHERE user_id<>'{IdentityFixture.User}' LIMIT 1"))!;
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{target}",CompanyAccountTests.Account("disabled",false,revision:1))).StatusCode);
        // Legacy PUT changes only membership.active: an actual seat addition needs an active persisted user.
        await f.Sql($"UPDATE users SET active=true WHERE id='{target}'");
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,"/api/company/memberships",new {subject=target.ToString(),issuer="fixture-actor",role="Editor",active=true,revision=2})).StatusCode);
        Assert.Equal(10L,await f.Scalar("SELECT count(*) FROM memberships WHERE active"));
    });
    [Theory] [InlineData(10,true)] [InlineData(10,false)] [InlineData(11,true)] [InlineData(11,false)]
    public async Task LegacyInactiveUserEditsAddNoSeatAtFullOrOverCapacity(int enabled,bool membershipActive)=>await IdentityFixture.Run(async f=>
    {
        await Fill(f,enabled-1);
        var disabled=new CompanyContext(IdentityFixture.Company,Guid.NewGuid(),CompanyRole.Editor,null,null);
        var activeUser=disabled with{UserId=Guid.NewGuid()};
        await FixtureCompany.AddActorAsync(f.Database,disabled);await FixtureCompany.AddActorAsync(f.Database,activeUser);
        var employee=Guid.NewGuid();
        await f.Sql($"UPDATE users SET active=false WHERE id='{disabled.UserId}'; UPDATE memberships SET active={membershipActive.ToString().ToLowerInvariant()} WHERE user_id='{disabled.UserId}'; UPDATE memberships SET active=false WHERE user_id='{activeUser.UserId}'; INSERT INTO employees(id,company_id,display_name) VALUES('{employee}','{IdentityFixture.Company}','Assigned employee'); UPDATE companies SET plan_kind='Ten',seat_limit=10");
        await f.Login();
        var before=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.Equal(enabled,before.GetProperty("enabled").GetInt32());
        var response=await f.Send(HttpMethod.Put,"/api/company/memberships",new{issuer="fixture-actor",subject=disabled.UserId.ToString(),role="Viewer",active=true,employeeId=employee,revision=1});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(false,await f.Scalar($"SELECT active FROM users WHERE id='{disabled.UserId}'"));
        Assert.Equal(true,await f.Scalar($"SELECT active FROM memberships WHERE user_id='{disabled.UserId}'"));
        Assert.Equal("Viewer",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{disabled.UserId}'"));
        Assert.Equal(employee,await f.Scalar($"SELECT employee_id FROM memberships WHERE user_id='{disabled.UserId}'"));
        Assert.Equal(2,await f.Scalar($"SELECT revision FROM memberships WHERE user_id='{disabled.UserId}'"));
        var after=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.Equal(before.GetRawText(),after.GetRawText());
        Assert.Equal("false:false",await f.Scalar("SELECT (before_value->>'Enabled')||':'||(after_value->>'Enabled') FROM account_audit WHERE action='LegacyAccountUpdated'"));
        var membershipBefore=await f.Scalar($"SELECT row_to_json(m)::text FROM memberships m WHERE user_id='{activeUser.UserId}'");
        var inventoryAudits=await f.Scalar("SELECT count(*) FROM inventory_audit");var accountAudits=await f.Scalar("SELECT count(*) FROM account_audit");
        var blocked=await f.Send(HttpMethod.Put,"/api/company/memberships",new{issuer="fixture-actor",subject=activeUser.UserId.ToString(),role="Editor",active=true,revision=1});
        Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);Assert.Equal("seat_limit_reached",(await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(membershipBefore,await f.Scalar($"SELECT row_to_json(m)::text FROM memberships m WHERE user_id='{activeUser.UserId}'"));
        Assert.Equal(inventoryAudits,await f.Scalar("SELECT count(*) FROM inventory_audit"));Assert.Equal(accountAudits,await f.Scalar("SELECT count(*) FROM account_audit"));
    });
    [Theory] [InlineData("create","invite")] [InlineData("create","membership")] [InlineData("user","membership")] [InlineData("invite","user")]
    public async Task MixedLastSeatWritersSerialize(string first,string second)=>await IdentityFixture.Run(async f=>
    {
        await Fill(f,8);await f.Sql("UPDATE companies SET plan_kind='Ten',seat_limit=10");
        var disabledMembership=new CompanyContext(IdentityFixture.Company,Guid.NewGuid(),CompanyRole.Editor,null,null);var disabledUser=disabledMembership with{UserId=Guid.NewGuid()};
        await FixtureCompany.AddActorAsync(f.Database,disabledMembership);await FixtureCompany.AddActorAsync(f.Database,disabledUser);
        await f.Sql($"UPDATE memberships SET active=false WHERE user_id='{disabledMembership.UserId}'; UPDATE users SET active=false WHERE id='{disabledUser.UserId}'");
        await f.Login();var csrf=await f.Client.GetFromJsonAsync<JsonElement>("/api/session/antiforgery");
        // Capture cookies once before concurrent requests; do not share the mutable fixture Send helper.
        var tokenResponse=await f.Client.GetAsync("/api/session/antiforgery");f.Capture(tokenResponse);var token=(await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        async Task<HttpResponseMessage> Send(string operation)
        {
            var path=operation switch{"invite"=>"/api/company/invitations","membership"=>$"/api/company/accounts/{disabledMembership.UserId}","user"=>$"/api/company/accounts/{disabledUser.UserId}",_=>"/api/company/accounts"};
            using var q=new HttpRequestMessage(operation is "membership" or "user"?HttpMethod.Put:HttpMethod.Post,path){Content=JsonContent.Create(operation=="invite"?(object)new{email="invite@example.test",role="Viewer"}:CompanyAccountTests.Account("new",revision:operation is "membership" or "user"?1:null))};q.Headers.Add("X-CSRF-TOKEN",token);return await f.Client.SendAsync(q);
        }
        var results=await Task.WhenAll(Send(first),Send(second));Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        var usage=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.Equal(10,usage.GetProperty("enabled").GetInt32()+usage.GetProperty("pending").GetInt32());
    });
    [Fact] public async Task AdministratorReplacementAtCapacityRollsBackDemotion()=>await IdentityFixture.Run(async f=>
    {
        await Fill(f,9);await f.Sql("UPDATE companies SET plan_kind='Ten',seat_limit=10");await CompanyAccountTests.Owner(f);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/administrator",new{account=CompanyAccountTests.Account("no-room"),revision=1})).StatusCode);
        Assert.Equal("Administrator",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{IdentityFixture.User}'"));Assert.Equal(1,await f.Scalar("SELECT revision FROM companies"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_audit"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM users WHERE identity_subject='no-room'"));
    });
}
