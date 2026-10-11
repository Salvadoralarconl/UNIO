using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api.LocalIdentity;
using Microsoft.Extensions.Configuration;
using Xunit;

public class LocalLoginTests
{
 internal static void Local(Microsoft.AspNetCore.Builder.WebApplicationBuilder b)=>b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Identity:Mode"]="Local",["Identity:Development:Enabled"]="false"});
 internal const string Password="a private test password 123";
 internal static async Task Seed(IdentityFixture f)
 {
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
  var r=new LocalCredentialRepository();await r.CreateUnsetAsync(c,tx,IdentityFixture.User,default);await r.SetAsync(c,tx,IdentityFixture.User,"Alice","Alice","Tester",new LocalPasswordService().Hash(Password),default);await tx.CommitAsync();
 }
 internal static async Task<HttpResponseMessage> Public(IdentityFixture f,string path,object body)
 {
  var csrf=await f.Client.GetAsync("/api/auth/antiforgery");f.Capture(csrf);var json=await csrf.Content.ReadFromJsonAsync<JsonElement>();
  using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};request.Headers.Add("X-CSRF-TOKEN",json.GetProperty("token").GetString());var result=await f.Client.SendAsync(request);f.Capture(result);return result;
 }
 [Fact] public async Task LoginSessionLogoutUsesPersistedIdentity()=>await IdentityFixture.Run(async f=>
 {
  await Seed(f);var config=await f.Client.GetFromJsonAsync<JsonElement>("/api/auth/config");Assert.Equal("Local",config.GetProperty("mode").GetString());
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PostAsJsonAsync("/api/auth/login",new {username="Alice",password=Password})).StatusCode);
  var login=await Public(f,"/api/auth/login",new {username=" ALICE ",password=Password,iss="evil",sub="evil",role="Operator",companyId=Guid.NewGuid()});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_sessions"));
  var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");Assert.Equal("Local",session.GetProperty("authenticationMode").GetString());Assert.Equal(IdentityFixture.User,session.GetProperty("user").GetProperty("id").GetGuid());Assert.False(session.GetProperty("isOperator").GetBoolean());
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/logout",new {})).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
 },Local);
 [Fact] public async Task FiveFailuresLockPersistentlyAndUnknownIsGeneric()=>await IdentityFixture.Run(async f=>
 {
  await Seed(f);string? generic=null;
  foreach(var name in new[]{"unknown","Alice","Alice","Alice","Alice","Alice"}){var response=await Public(f,"/api/auth/login",new {username=name,password="wrong"});Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);generic??=await response.Content.ReadAsStringAsync();Assert.Equal(generic,await response.Content.ReadAsStringAsync());}
  Assert.Equal(5,await f.Scalar("SELECT failed_attempts FROM local_credentials"));Assert.Equal(true,await f.Scalar("SELECT locked_until>clock_timestamp()+interval '14 minutes' FROM local_credentials"));
  Assert.Equal(HttpStatusCode.Unauthorized,(await Public(f,"/api/auth/login",new {username="Alice",password=Password})).StatusCode);
  await f.Sql("UPDATE local_credentials SET locked_until=clock_timestamp()-interval '1 second'");Assert.Equal(HttpStatusCode.OK,(await Public(f,"/api/auth/login",new {username="Alice",password=Password})).StatusCode);Assert.Equal(0,await f.Scalar("SELECT failed_attempts FROM local_credentials"));
 },Local);
 [Fact] public async Task RequestLimitPrecedesPasswordWork()=>await IdentityFixture.Run(async f=>
 {for(int i=0;i<30;i++)Assert.Equal(HttpStatusCode.Unauthorized,(await Public(f,"/api/auth/login",new {username="unknown",password="wrong"})).StatusCode);Assert.Equal(HttpStatusCode.TooManyRequests,(await Public(f,"/api/auth/login",new {username="unknown",password="wrong"})).StatusCode);},Local);
 [Fact] public async Task DisabledMembershipCompanyAndUnconfiguredAccountsFailGenerically()=>await IdentityFixture.Run(async f=>
 {
  await Seed(f);string? body=null;foreach(var sql in new[]{"UPDATE memberships SET active=false","UPDATE memberships SET active=true; UPDATE companies SET active=false","UPDATE companies SET active=true; UPDATE users SET active=false","UPDATE users SET active=true; UPDATE local_credentials SET password_hash=NULL"}){await f.Sql(sql);var result=await Public(f,"/api/auth/login",new{username="Alice",password=Password});Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);body??=await result.Content.ReadAsStringAsync();Assert.Equal(body,await result.Content.ReadAsStringAsync());}Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_sessions"));
 },Local);

 [Fact] public async Task CompanylessOwnerLoginHasNoCompanyAccessAndManagerCannotResetOwner()=>await IdentityFixture.Run(async f=>
 {
  var owner=await CompanyAccountTests.Owner(f);await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync()){var repo=new LocalCredentialRepository();await repo.CreateUnsetAsync(c,tx,owner,default);await repo.SetAsync(c,tx,owner,"Owner","Private","Owner",new LocalPasswordService().Hash(Password),default);await tx.CommitAsync();}
  await f.Send(HttpMethod.Post,"/api/session/logout",new{});Assert.Equal(HttpStatusCode.OK,(await Public(f,"/api/auth/login",new{username="Owner",password=Password})).StatusCode);var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");Assert.True(session.GetProperty("isOperator").GetBoolean());Assert.Equal(JsonValueKind.Null,session.GetProperty("activeCompany").ValueKind);Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/documents")).StatusCode);
  var service=new LocalProvisioningService(f.Database,new(f.Database),new(),new());var actor=Clary.Api.Accounts.AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,Clary.Api.Accounts.CompanyRole.Administrator,null,null));await Assert.ThrowsAsync<Clary.Api.Identity.CompanyMissing>(()=>service.IssueAsync(actor,IdentityFixture.Company,owner,new(1),default));
  await f.Sql($"INSERT INTO memberships(company_id,user_id,role) VALUES('{IdentityFixture.Company}','{owner}','Editor')");await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>service.IssueAsync(actor,IdentityFixture.Company,owner,new(1),default));
 },Local);

}
