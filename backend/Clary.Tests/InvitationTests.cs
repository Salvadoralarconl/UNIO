using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

public class InvitationTests
{
    internal static async Task<JsonElement> Invite(IdentityFixture f,string email="invite@example.test",object? extra=null)
    {
        var response=await f.Send(HttpMethod.Post,"/api/company/invitations",extra??new {email,role="Editor"});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact] public async Task ReservationConversionReplayEmailMismatchAndTokenSecrecy()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var invite=await Invite(f);var token=invite.GetProperty("token").GetString()!;Assert.Equal(invite.GetProperty("createdAt").GetDateTimeOffset().AddDays(7),invite.GetProperty("expiresAt").GetDateTimeOffset());Assert.Equal(32,await f.Scalar("SELECT octet_length(token_hash) FROM account_invitations"));
        var list=await f.Client.GetStringAsync("/api/company/invitations");Assert.DoesNotContain(token,list);Assert.DoesNotContain("token",list,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token,(string)(await f.Scalar("SELECT string_agg(coalesce(after_value::text,''),'') FROM account_audit"))!);
        await f.Sql("INSERT INTO users(id,issuer,identity_subject,display_name,email) VALUES(gen_random_uuid(),'https://invite.example','recipient','Recipient','invite@example.test')");
        f.SignInAs("https://invite.example","recipient",null,new Claim("email","wrong@example.test"),new Claim("email_verified","true"));
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
        f.SignInAs("https://invite.example","recipient",null,new Claim("email","invite@example.test"),new Claim("email_verified","false"));
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
        f.SignInAs("https://invite.example","recipient",null,new Claim("email","invite@example.test"),new Claim("email_verified","true"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
        f.SignInAs("https://invite.example","recipient",IdentityFixture.Company,new Claim("email","invite@example.test"),new Claim("email_verified","true"));
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
        Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships"));await f.Login();
        var usage=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.Equal(2,usage.GetProperty("enabled").GetInt32());Assert.Equal(0,usage.GetProperty("pending").GetInt32());
    });
    [Fact] public async Task DuplicateRevocationExpiryAndReissueUseLogicalReservation()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var original=await Invite(f);var id=original.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,"/api/company/invitations",new {email="INVITE@example.test",role="Viewer"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Delete,$"/api/company/invitations/{id}",new {revision=99})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Delete,$"/api/company/invitations/{id}",new {revision=1})).StatusCode);
        var next=await Invite(f);Assert.NotEqual(original.GetProperty("token").GetString(),next.GetProperty("token").GetString());
        await f.Sql("UPDATE account_invitations SET created_at=expires_at-interval '15 days',expires_at=expires_at-interval '8 days' WHERE state='Pending'");
        var usage=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.Equal(0,usage.GetProperty("pending").GetInt32());
        await Invite(f);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_invitations WHERE state='Pending'"));
    });
    [Fact] public async Task ExplicitSubjectBindingRejectsWrongIdentityWithoutEmail()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var invitation=await Invite(f,extra:new {email="bound@example.test",role="Viewer",issuer="https://bound.example",subject="bound"});var token=invitation.GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,"/api/company/invitations",new{email="alias@example.test",role="Viewer",issuer="https://bound.example",subject="bound"})).StatusCode);
        await f.Sql("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(gen_random_uuid(),'https://bound.example','wrong','Wrong'),(gen_random_uuid(),'https://bound.example','bound','Bound')");
        f.SignInAs("https://bound.example","wrong",null,new Claim("email","bound@example.test"),new Claim("email_verified","true"));
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
        f.SignInAs("https://bound.example","bound");Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
    });
    [Fact] public async Task ValidReservationConvertsWhileReducedPlanIsOverLimit()=>await IdentityFixture.Run(async f=>
    {
        await SeatAccountingTests.Fill(f,10);await f.Login();var invitation=await Invite(f);var token=invitation.GetProperty("token").GetString();await CompanyAccountTests.Owner(f);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/subscription",new {kind="Ten",revision=1})).StatusCode);
        await f.Sql("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(gen_random_uuid(),'https://convert.example','convert','Convert')");f.SignInAs("https://convert.example","convert",null,new Claim("email","invite@example.test"),new Claim("email_verified","true"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);
        Assert.Equal(12L,await f.Scalar("SELECT count(*) FROM memberships WHERE active"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_invitations WHERE state='Pending'"));
    });
    [Fact] public async Task ExpiryDuringCompanyLockWaitCannotAcceptOrReserveASeat()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var invitation=await Invite(f);var id=invitation.GetProperty("id").GetGuid();var token=invitation.GetProperty("token").GetString();
        await f.Sql("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(gen_random_uuid(),'https://late.example','late','Late')");
        f.SignInAs("https://late.example","late",null,new Claim("email","invite@example.test"),new Claim("email_verified","true"));
        await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var q=new Npgsql.NpgsqlCommand($"SELECT id FROM companies WHERE id='{IdentityFixture.Company}' FOR UPDATE; UPDATE account_invitations SET created_at=clock_timestamp()-interval '7 days'+interval '1 second',expires_at=clock_timestamp()+interval '1 second' WHERE id='{id}'",c,tx))
        {
            // A single statement timestamp makes the schema's exact seven-day relation deterministic.
            q.CommandText=q.CommandText.Replace("clock_timestamp()","statement_timestamp()");await q.ExecuteNonQueryAsync();
        }
        var pending=f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token});
        var blocked=false;for(var attempt=0;attempt<100;attempt++)
        {await using var q=f.Database.CreateCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE @pid=ANY(pg_blocking_pids(pid)))");q.Parameters.AddWithValue("pid",c.ProcessID);if((bool)(await q.ExecuteScalarAsync())!){blocked=true;break;}await Task.Delay(20);}
        Assert.True(blocked,"Acceptance must actually wait for the company lock.");
        await using(var q=new Npgsql.NpgsqlCommand("SELECT pg_sleep(1.1)",c,tx))await q.ExecuteNonQueryAsync();await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await pending).StatusCode);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM memberships"));await f.Login();
        var usage=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");Assert.Equal(0,usage.GetProperty("pending").GetInt32());
    });
    [Theory]
    [InlineData("users")]
    [InlineData("account_sessions")]
    public async Task ExpiryDuringRecipientLockWaitCannotConvertReservation(string lockedTable) => await IdentityFixture.Run(async f =>
    {
        await f.Login();
        var invitation = await Invite(f);
        var id = invitation.GetProperty("id").GetGuid();
        var token = invitation.GetProperty("token").GetString();
        var user = Guid.NewGuid();
        await f.Sql($"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('{user}','https://late.example','recipient','Recipient')");
        f.SignInAs("https://late.example", "recipient", null, new Claim("email", "invite@example.test"), new Claim("email_verified", "true"));
        // Commit a near expiry before taking an unchanged recipient row lock.
        await f.Sql($"UPDATE account_invitations SET created_at=statement_timestamp()-interval '7 days'+interval '3 seconds',expires_at=statement_timestamp()+interval '3 seconds' WHERE id='{id}'");
        await using var c = await f.Database.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        var predicate = lockedTable == "users" ? $"id='{user}'" : $"user_id='{user}'";
        await using (var q = new Npgsql.NpgsqlCommand($"SELECT id FROM {lockedTable} WHERE {predicate} FOR UPDATE", c, tx))
            await q.ExecuteNonQueryAsync();
        var pending = f.Send(HttpMethod.Post, "/api/session/invitations/accept", new { token });
        var blocked = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var q = f.Database.CreateCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE @pid=ANY(pg_blocking_pids(pid)))");
            q.Parameters.AddWithValue("pid", c.ProcessID);
            if ((bool)(await q.ExecuteScalarAsync())!) { blocked = true; break; }
            await Task.Delay(20);
        }
        Assert.True(blocked, $"Acceptance must actually wait on the unchanged {lockedTable} row.");
        Assert.False(pending.IsCompleted);
        await using (var q = new Npgsql.NpgsqlCommand("SELECT pg_sleep(3.1)", c, tx)) await q.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.Equal(0L, await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{user}'"));
        Assert.Equal(0L, await f.Scalar("SELECT count(*) FROM account_audit WHERE action='InvitationAccepted'"));
        Assert.Equal(1L, await f.Scalar($"SELECT count(*) FROM account_sessions WHERE user_id='{user}' AND revoked_at IS NULL AND expires_at>clock_timestamp()"));
        await f.Login();
        var usage = await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats");
        Assert.Equal(0, usage.GetProperty("pending").GetInt32());
    });

    [Fact] public async Task ExistingMembershipInvitationDeniesReactivationAndForeignEmailDoesNotLeak()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var response=await f.Send(HttpMethod.Post,"/api/company/accounts",CompanyAccountTests.Account("existing",false));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,"/api/company/invitations",new{email="existing@example.test",role="Editor"})).StatusCode);
        var foreign=Guid.NewGuid();var foreignUser=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign}','Foreign'); INSERT INTO users(id,issuer,identity_subject,display_name,email) VALUES('{foreignUser}','https://foreign.example','foreign','Foreign','shared@example.test'); INSERT INTO memberships(company_id,user_id,role) VALUES('{foreign}','{foreignUser}','Editor')");
        var invitation=await Invite(f,"shared@example.test");var token=invitation.GetProperty("token").GetString();f.SignInAs("https://foreign.example","foreign",null,new Claim("email","shared@example.test"),new Claim("email_verified","true"));
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);Assert.Equal(foreign,await f.Scalar($"SELECT company_id FROM memberships WHERE user_id='{foreignUser}'"));
    });
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task OwnerCannotUseInvitationToGrantItsOwnCompanyMembership(bool bindSubject)=>await IdentityFixture.Run(async f=>
    {
        var owner=await CompanyAccountTests.Owner(f);
        var response=await f.Send(HttpMethod.Post,$"/api/operator/companies/{IdentityFixture.Company}/invitations",new{email="owner@example.test",role="Editor",issuer=bindSubject?"https://owner.example":null,subject=bindSubject?"owner":null});
        if(bindSubject){Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_invitations"));}
        else
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);var invitation=await response.Content.ReadFromJsonAsync<JsonElement>();var token=invitation.GetProperty("token").GetString();f.SignInAs("https://owner.example","owner",null,new Claim("email","owner@example.test"),new Claim("email_verified","true"));
            Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new {token})).StatusCode);Assert.Equal("Pending",await f.Scalar("SELECT state FROM account_invitations"));
        }
        Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{owner}'"));
    });
    [Fact] public async Task ConcurrentReplayConvertsExactlyOneReservation()=>await IdentityFixture.Run(async f=>
    {
        await f.Login();var invitation=await Invite(f);var token=invitation.GetProperty("token").GetString();await f.Sql("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(gen_random_uuid(),'https://replay.example','replay','Replay')");f.SignInAs("https://replay.example","replay",null,new Claim("email","invite@example.test"),new Claim("email_verified","true"));
        var csrf=await f.Client.GetAsync("/api/session/antiforgery");f.Capture(csrf);var csrfToken=(await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        async Task<HttpResponseMessage> Accept(){using var q=new HttpRequestMessage(HttpMethod.Post,"/api/session/invitations/accept"){Content=JsonContent.Create(new{token})};q.Headers.Add("X-CSRF-TOKEN",csrfToken);return await f.Client.SendAsync(q);}
        var responses=await Task.WhenAll(Accept(),Accept());Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_audit WHERE action='InvitationAccepted'"));
    });
}
