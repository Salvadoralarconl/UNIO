using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api.LocalIdentity;
using Clary.Api.Accounts;
using Xunit;
public class LocalRecoveryTests
{
 internal static async Task<string> Link(IdentityFixture f,CredentialTokenPurpose purpose)
 {await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var link=await new LocalCredentialTokens().IssueAsync(c,tx,IdentityFixture.User,purpose,default);await tx.CommitAsync();return link.Link.Split("token=")[1];}
 [Fact] public async Task SetupDetailsResetImmutableAndSingleUse()=>await IdentityFixture.Run(async f=>
 {
  await LocalLoginTests.Seed(f);var token=await Link(f,CredentialTokenPurpose.Reset);
  var detail=await LocalLoginTests.Public(f,"/api/auth/setup/details",new{token});Assert.Equal(HttpStatusCode.OK,detail.StatusCode);var fields=await detail.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("Reset",fields.GetProperty("purpose").GetString());Assert.Equal(5,fields.EnumerateObject().Count());
  var input=new{token,username="changed",firstName="Changed",lastName="Profile",password="a new private password 123",confirmPassword="a new private password 123"};
  Assert.Equal(HttpStatusCode.OK,(await LocalLoginTests.Public(f,"/api/auth/setup",input)).StatusCode);Assert.Equal("Alice",await f.Scalar("SELECT username FROM local_credentials"));Assert.Equal("Alice Tester",await f.Scalar("SELECT display_name FROM users"));
  Assert.Equal(HttpStatusCode.BadRequest,(await LocalLoginTests.Public(f,"/api/auth/setup",input)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
 },LocalLoginTests.Local);
 [Fact] public async Task PasswordChangeRevokesCookieTokensAndRequiresCurrentPassword()=>await IdentityFixture.Run(async f=>
 {
  await LocalLoginTests.Seed(f);var token=await Link(f,CredentialTokenPurpose.Reset);Assert.Equal(HttpStatusCode.OK,(await LocalLoginTests.Public(f,"/api/auth/login",new{username="Alice",password=LocalLoginTests.Password})).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,"/api/auth/password",new{currentPassword="wrong",password="another private password 123",confirmPassword="another private password 123"})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/auth/password",new{currentPassword=LocalLoginTests.Password,password="another private password 123",confirmPassword="another private password 123"})).StatusCode);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await LocalLoginTests.Public(f,"/api/auth/setup/details",new{token})).StatusCode);
 },LocalLoginTests.Local);
 [Theory] [InlineData("users")] [InlineData("account_sessions")]
 public async Task ResetExpiryAfterEveryLockRollsBack(string table)=>await IdentityFixture.Run(async f=>
 {
  await LocalLoginTests.Seed(f);f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);var token=await Link(f,CredentialTokenPurpose.Reset);
  var old=await f.Scalar("SELECT password_hash FROM local_credentials");await f.Sql("UPDATE local_credential_tokens SET created_at=now()-interval '29 minutes 58 seconds',expires_at=now()+interval '2 seconds'");
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();await using(var q=new Npgsql.NpgsqlCommand($"SELECT 1 FROM {table} FOR UPDATE",c,tx))await q.ExecuteScalarAsync();
  var auth=new LocalAuthenticationService(f.Database,new(),new(),new());var pending=auth.SetupAsync(new(token,"ignored","ignored","ignored","another private password 123","another private password 123"),default);
  await Task.Delay(300);Assert.False(pending.IsCompleted);await Task.Delay(2000);await tx.CommitAsync();await Assert.ThrowsAnyAsync<Exception>(()=>pending);Assert.Equal(old,await f.Scalar("SELECT password_hash FROM local_credentials"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE consumed_at IS NOT NULL"));
 },LocalLoginTests.Local);

 [Fact] public async Task ManagerSessionExpiryAfterCredentialLockWaitRollsBackIssuedLink()=>await IdentityFixture.Run(async f=>
 {
  f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);var session=(Guid)(await f.Scalar("SELECT id FROM account_sessions"))!;
  var offline=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));var service=new LocalProvisioningService(f.Database,new(f.Database),new(),new());var target=await service.CreateAsync(offline,IdentityFixture.Company,new("First","Last",null,CompanyRole.Editor,true,null),default);
  await f.Sql("UPDATE account_sessions SET expires_at=now()+interval '2 seconds'");await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();await using(var q=new Npgsql.NpgsqlCommand("SELECT user_id FROM local_credentials FOR UPDATE",c,tx))await q.ExecuteScalarAsync();
  var actor=AccountActor.For(offline.Company! with{SessionId=session});var pending=service.IssueAsync(actor,IdentityFixture.Company,target.Account.Id,new(1),default);await Task.Delay(300);Assert.False(pending.IsCompleted);await Task.Delay(2000);await tx.CommitAsync();await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>pending);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credential_tokens"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE revoked_at IS NOT NULL"));
 },LocalLoginTests.Local);

 [Theory] [InlineData("account_sessions")] [InlineData("local_credential_tokens")]
 public async Task OwnPasswordChangeExpiryAfterRevocationWaitRollsBackEverything(string table)=>await IdentityFixture.Run(async f=>
 {
  await LocalLoginTests.Seed(f);await Link(f,CredentialTokenPurpose.Reset);var auth=new LocalAuthenticationService(f.Database,new(),new(),new());
  var acting=(await auth.LoginAsync(new("Alice",LocalLoginTests.Password),default))!;var other=(await auth.LoginAsync(new("Alice",LocalLoginTests.Password),default))!;var session=SessionPresence.Id(acting.Principal)!.Value;
  var original=await f.Scalar("SELECT password_hash FROM local_credentials");await f.Sql($"UPDATE account_sessions SET expires_at=now()+interval '2 seconds' WHERE id='{session}'");
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var sql=table=="account_sessions"?$"SELECT id FROM account_sessions WHERE id='{SessionPresence.Id(other.Principal)}' FOR UPDATE":"SELECT id FROM local_credential_tokens FOR UPDATE";await using(var q=new Npgsql.NpgsqlCommand(sql,c,tx))await q.ExecuteScalarAsync();
  var pending=auth.ChangeAsync(acting.Principal,new(LocalLoginTests.Password,"another private password 123","another private password 123"),default);await Task.Delay(300);Assert.False(pending.IsCompleted);await Task.Delay(2000);await tx.CommitAsync();
  await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>pending);Assert.True(Equals(original,await f.Scalar("SELECT password_hash FROM local_credentials")));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_sessions WHERE revoked_at IS NOT NULL"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE revoked_at IS NOT NULL OR consumed_at IS NOT NULL"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_audit WHERE action='LocalPasswordChanged'"));
 },LocalLoginTests.Local);

}
