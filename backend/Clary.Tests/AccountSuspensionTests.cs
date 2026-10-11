using System.Net;
using Xunit;
public class AccountSuspensionTests
{
 [Fact] public async Task SuspendedThenReactivatedCompanyNeverRestoresOldCookie()=>await IdentityFixture.Run(async f=>{
  await f.Login();var cookie=f.Client.DefaultRequestHeaders.GetValues("Cookie").Single();await CompanyAccountTests.Owner(f);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}",new{name="Legacy company",active=false,revision=1})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}",new{name="Legacy company",active=true,revision=2})).StatusCode);
  f.Client.DefaultRequestHeaders.Remove("Cookie");f.Client.DefaultRequestHeaders.Add("Cookie",cookie);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.PostAsync("/api/session/heartbeat",null)).StatusCode);
 });
 [Fact] public async Task DisabledThenEnabledAccountNeverRestoresOldCookie()=>await IdentityFixture.Run(async f=>{
  await f.Login();var cookie=f.Client.DefaultRequestHeaders.GetValues("Cookie").Single();await CompanyAccountTests.Owner(f);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{IdentityFixture.User}",new{issuer="clary-development",subject="bootstrap",displayName="Bootstrap",role="Administrator",enabled=false,revision=1})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{IdentityFixture.User}",new{issuer="clary-development",subject="bootstrap",displayName="Bootstrap",role="Administrator",enabled=true,revision=2})).StatusCode);
  f.Client.DefaultRequestHeaders.Remove("Cookie");f.Client.DefaultRequestHeaders.Add("Cookie",cookie);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/company")).StatusCode);
 });
 [Fact] public async Task DisabledUserCannotRefreshPresence()=>await IdentityFixture.Run(async f=>{
  await f.Login();await f.Sql($"UPDATE users SET active=false WHERE id='{IdentityFixture.User}'");
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.PostAsync("/api/session/heartbeat",null)).StatusCode);
 });
}
