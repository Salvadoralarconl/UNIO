using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
public class SessionPresenceTests
{
 [Fact] public async Task SignInPersistsSessionAndLogoutPermanentlyRevokesCopiedCookie()=>await IdentityFixture.Run(async f=>{
  await f.Login(); var cookie=f.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_sessions WHERE revoked_at IS NULL"));
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/logout",new{})).StatusCode);
  f.Client.DefaultRequestHeaders.Remove("Cookie");f.Client.DefaultRequestHeaders.Add("Cookie",cookie);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
 });
 [Fact] public async Task HeartbeatRequiresCsrfAndExpiryIsEnforced()=>await IdentityFixture.Run(async f=>{
  await f.Login();Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PostAsJsonAsync("/api/session/heartbeat",new{})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/heartbeat",new{})).StatusCode);
  await f.Sql("UPDATE account_sessions SET created_at=clock_timestamp()-interval '2 hours',last_activity_at=clock_timestamp()-interval '1 hour',expires_at=clock_timestamp()-interval '1 minute'");
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
 });
 [Fact] public async Task HistoricalPresenceSurvivesRevocationAndCutoff()=>await IdentityFixture.Run(async f=>{
  await f.Login();await f.Sql("UPDATE account_sessions SET created_at=clock_timestamp()-interval '1 hour',last_activity_at=clock_timestamp()-interval '6 minutes',revoked_at=clock_timestamp()");
  await f.Login();await f.Sql("UPDATE account_sessions SET created_at=clock_timestamp()-interval '1 hour',last_activity_at=clock_timestamp()-interval '6 minutes'");
  var page=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/accounts");var user=page.GetProperty("items")[0];Assert.False(user.GetProperty("online").GetBoolean());Assert.NotEqual(JsonValueKind.Null,user.GetProperty("lastActivity").ValueKind);
 });
 [Fact] public async Task RevocationBetweenLoadAndMutationCannotCommit()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=(Guid)(await f.Scalar("SELECT id FROM account_sessions"))!;
  var context=new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null,SessionId:id);
  await f.Sql("UPDATE account_sessions SET revoked_at=clock_timestamp()");
  await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>new Clary.Api.Identity.CompanyRepository(f.Database).UpdateAsync(context,new("Should not commit",1),default));
  Assert.Equal("Legacy company",await f.Scalar("SELECT name FROM companies"));
 });
 [Fact] public async Task DistinctUsersAndExactCutoffDoNotCountExpiredOrDisabledSessions()=>await IdentityFixture.Run(async f=>{
  await f.Login();await f.Sql($"INSERT INTO account_sessions(id,user_id,expires_at) VALUES(gen_random_uuid(),'{IdentityFixture.User}',clock_timestamp()+interval '1 hour')");
  var owner=await CompanyAccountTests.Owner(f);var response=await f.Client.GetFromJsonAsync<JsonElement>($"/api/operator/companies/{IdentityFixture.Company}");Assert.Equal(1,response.GetProperty("company").GetProperty("onlineUsers").GetInt32());
  await f.Sql($"UPDATE account_sessions SET created_at=clock_timestamp()-interval '1 hour',last_activity_at=clock_timestamp()-interval '5 minutes' WHERE user_id='{IdentityFixture.User}'");
  response=await f.Client.GetFromJsonAsync<JsonElement>($"/api/operator/companies/{IdentityFixture.Company}");Assert.Equal(0,response.GetProperty("company").GetProperty("onlineUsers").GetInt32());
  Assert.NotEqual(JsonValueKind.Null,response.GetProperty("company").GetProperty("lastActivity").ValueKind);
 });
 [Fact] public async Task AcceptanceRejectsSessionRevokedAfterRequestLoad()=>await IdentityFixture.Run(async f=>{
  await f.Login();var invite=await InvitationTests.Invite(f);var user=Guid.NewGuid();await f.Sql($"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('{user}','https://invite.example','recipient','Recipient')");
  f.SignInAs("https://invite.example","recipient",null,new System.Security.Claims.Claim("email","invite@example.test"),new System.Security.Claims.Claim("email_verified","true"));
  var session=(Guid)(await f.Scalar($"SELECT id FROM account_sessions WHERE user_id='{user}'"))!;await f.Sql($"UPDATE account_sessions SET revoked_at=clock_timestamp() WHERE user_id='{user}'");
  var principal=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim("iss","https://invite.example"),new System.Security.Claims.Claim("sub","recipient"),new System.Security.Claims.Claim("email","invite@example.test"),new System.Security.Claims.Claim("email_verified","true"),new System.Security.Claims.Claim(Clary.Api.Accounts.SessionPresence.ClaimType,session.ToString())},"TrustedCookie"));
  await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>new Clary.Api.Accounts.InvitationService(f.Database).AcceptAsync(principal,invite.GetProperty("token").GetString()!,default));
  Assert.Equal("Pending",await f.Scalar("SELECT state FROM account_invitations"));
 });
 [Fact] public async Task OldCookieWithoutServerSessionRequiresFreshSignIn()=>await IdentityFixture.Run(async f=>{
  await f.Login();f.RemovePersistedSessionClaim();Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/company")).StatusCode);
 });
 [Fact] public async Task HeartbeatThrottlesWritesAndDoesNotExtendExpiry()=>await IdentityFixture.Run(async f=>{
  await f.Login();var expiry=await f.Scalar("SELECT expires_at FROM account_sessions");var initial=await f.Scalar("SELECT last_activity_at FROM account_sessions");
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/heartbeat",new{})).StatusCode);Assert.Equal(initial,await f.Scalar("SELECT last_activity_at FROM account_sessions"));
  await f.Sql("UPDATE account_sessions SET created_at=clock_timestamp()-interval '1 hour',last_activity_at=clock_timestamp()-interval '1 minute'");
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/heartbeat",new{})).StatusCode);Assert.Equal(expiry,await f.Scalar("SELECT expires_at FROM account_sessions"));Assert.True((DateTime)(await f.Scalar("SELECT last_activity_at FROM account_sessions"))!>DateTime.UtcNow.AddSeconds(-10));
 });
 [Fact] public async Task SessionRowMustBelongToTrustedIdentity()=>await IdentityFixture.Run(async f=>{
  await f.Login();var owner=await CompanyAccountTests.Owner(f);await f.Sql($"UPDATE account_sessions SET user_id='{IdentityFixture.User}' WHERE user_id='{owner}'");
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/operator/companies")).StatusCode);
 });
 [Fact] public async Task OperatorAuditPaginationIncludesBothCompaniesAndRejectsQueryRebinding()=>await IdentityFixture.Run(async f=>{
  var owner=await CompanyAccountTests.Owner(f);await f.Sql($"INSERT INTO companies(id,name) VALUES('22222222-2222-2222-2222-222222222222','Foreign'); INSERT INTO account_audit(id,actor_user_id,company_id,action) VALUES('00000000-0000-0000-0000-000000000001','{owner}','{IdentityFixture.Company}','Own'),('00000000-0000-0000-0000-000000000002','{owner}','22222222-2222-2222-2222-222222222222','Foreign')");
  var page=await f.Client.GetFromJsonAsync<JsonElement>("/api/operator/audit?limit=1");Assert.Equal("Own",page.GetProperty("items")[0].GetProperty("action").GetString());var cursor=Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
  var second=await f.Client.GetFromJsonAsync<JsonElement>("/api/operator/audit?limit=1&cursor="+cursor);Assert.Equal("Foreign",second.GetProperty("items")[0].GetProperty("action").GetString());Assert.Equal(JsonValueKind.Null,second.GetProperty("nextCursor").ValueKind);
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.GetAsync("/api/operator/audit?limit=2&cursor="+cursor)).StatusCode);
 });
 [Fact] public async Task ExpiryDuringUnchangedSessionLockWaitDeniesMutation()=>await IdentityFixture.Run(async f=>{
  await f.Login();var session=(Guid)(await f.Scalar("SELECT id FROM account_sessions"))!;
  await f.Sql("UPDATE account_sessions SET expires_at=clock_timestamp()+interval '2 seconds'");
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
  await using(var q=new Npgsql.NpgsqlCommand("SELECT id FROM account_sessions FOR UPDATE",c,tx))await q.ExecuteScalarAsync();
  var context=new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null,SessionId:session);
  var mutation=new Clary.Api.Identity.CompanyRepository(f.Database).UpdateAsync(context,new("Expired mutation",1),default);
  await WaitForBlockedRequests(f,c.ProcessID,1);
  // Wait against the database clock, retaining the unchanged row lock through expiration.
  await using(var q=new Npgsql.NpgsqlCommand("SELECT pg_sleep(GREATEST(0,extract(epoch FROM expires_at-clock_timestamp()))+0.1) FROM account_sessions",c,tx))await q.ExecuteNonQueryAsync();
  await tx.CommitAsync();await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>mutation);
  Assert.Equal("Legacy company",await f.Scalar("SELECT name FROM companies"));
 });
 [Theory] [InlineData("/api/session/heartbeat")] [InlineData("/api/operator/audit")]
 public async Task OwnerInvitationAndPresenceUseConsistentUserThenSessionLocks(string path)=>await IdentityFixture.Run(async f=>{
  var owner=await CompanyAccountTests.Owner(f);
  var invitation=await f.Send(HttpMethod.Post,$"/api/operator/companies/{IdentityFixture.Company}/invitations",new{email="owner@example.test",role="Editor"});Assert.Equal(HttpStatusCode.OK,invitation.StatusCode);
  var token=(await invitation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
  f.SignInAs("https://owner.example","owner",null,new System.Security.Claims.Claim("email","owner@example.test"),new System.Security.Claims.Claim("email_verified","true"));
  var csrf=await f.Client.GetAsync("/api/session/antiforgery");f.Capture(csrf);var csrfToken=(await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
  await using(var q=new Npgsql.NpgsqlCommand($"SELECT id FROM account_sessions WHERE user_id='{owner}' FOR UPDATE",c,tx))await q.ExecuteNonQueryAsync();
  using var presenceRequest=new HttpRequestMessage(path.EndsWith("heartbeat")?HttpMethod.Post:HttpMethod.Get,path);presenceRequest.Headers.Add("X-CSRF-TOKEN",csrfToken);
  var presence=f.Client.SendAsync(presenceRequest);await WaitForBlockedRequests(f,c.ProcessID,1);
  using var acceptRequest=new HttpRequestMessage(HttpMethod.Post,"/api/session/invitations/accept"){Content=JsonContent.Create(new{token})};acceptRequest.Headers.Add("X-CSRF-TOKEN",csrfToken);
  var accept=f.Client.SendAsync(acceptRequest);await WaitForBlockedRequests(f,c.ProcessID,2);
  await tx.CommitAsync();
  // A deadlock must never replace either intended response with SQLSTATE40P01/HTTP500.
  var presenceResponse=await presence;var acceptResponse=await accept;
  Assert.DoesNotContain("40P01",await presenceResponse.Content.ReadAsStringAsync());Assert.DoesNotContain("40P01",await acceptResponse.Content.ReadAsStringAsync());
  Assert.Equal(HttpStatusCode.OK,presenceResponse.StatusCode);Assert.Equal(HttpStatusCode.Forbidden,acceptResponse.StatusCode);
  Assert.Equal("Pending",await f.Scalar("SELECT state FROM account_invitations"));
  Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{owner}'"));
 });
 private static async Task WaitForBlockedRequests(IdentityFixture f,int blocker,int count)
 {
  for(var attempt=0;attempt<200;attempt++)
  {
   // Follow indirect blockers too: after the fix acceptance waits on the owner user held by presence.
   await using var q=f.Database.CreateCommand("WITH RECURSIVE waits AS (SELECT pid FROM pg_stat_activity WHERE @pid=ANY(pg_blocking_pids(pid)) UNION SELECT a.pid FROM pg_stat_activity a JOIN waits w ON w.pid=ANY(pg_blocking_pids(a.pid))) SELECT count(DISTINCT pid) FROM waits");q.Parameters.AddWithValue("pid",blocker);
   if((long)(await q.ExecuteScalarAsync())!>=count)return;await Task.Delay(10);
  }
  Assert.Fail("Requests must actually block behind the held session row lock.");
 }
 [Fact] public async Task CompanyAuditIsScopedAndCursorCannotCrossSurfaces()=>await IdentityFixture.Run(async f=>{
  await f.Login();await f.Sql($"INSERT INTO companies(id,name) VALUES('22222222-2222-2222-2222-222222222222','Foreign'); INSERT INTO account_audit(id,actor_user_id,company_id,action) VALUES(gen_random_uuid(),'{IdentityFixture.User}','{IdentityFixture.Company}','Own'),(gen_random_uuid(),'{IdentityFixture.User}','{IdentityFixture.Company}','Own2'),(gen_random_uuid(),'{IdentityFixture.User}','22222222-2222-2222-2222-222222222222','Foreign')");
  var response=await f.Client.GetAsync("/api/company/account-audit?limit=1");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var page=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Single(page.GetProperty("items").EnumerateArray());Assert.NotEqual("Foreign",page.GetProperty("items")[0].GetProperty("action").GetString());
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.GetAsync("/api/company/accounts?limit=1&cursor="+Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!))).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/operator/audit")).StatusCode);
 });
}
