using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api.Accounts;
using Xunit;
public class LocalProvisioningTests
{
 [Fact] public async Task DisabledCreationKeepsUnsetCredentialWithoutLink()=>await IdentityFixture.Run(async f=>
 {
  f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);var response=await f.Send(HttpMethod.Post,"/api/company/accounts",new{firstName="Disabled",lastName="Person",role="Editor",enabled=false});Assert.Equal(HttpStatusCode.OK,response.StatusCode);var value=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(JsonValueKind.Null,value.GetProperty("setup").ValueKind);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credentials"));
 },LocalLoginTests.Local);
 [Fact] public async Task PrivateAccountCreationHasOneSeatAndOneSetupLink()=>await IdentityFixture.Run(async f=>
 {
  f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);
  var response=await f.Send(HttpMethod.Post,"/api/company/accounts",new{firstName="Élodie",lastName="李",email="person@example.test",role="Editor",enabled=true});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var result=await response.Content.ReadFromJsonAsync<JsonElement>();var user=result.GetProperty("account").GetProperty("id").GetGuid();Assert.Equal("https://brie.local/identity",await f.Scalar($"SELECT issuer FROM users WHERE id='{user}'"));Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships WHERE active"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credential_tokens"));
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,$"/api/company/accounts/{IdentityFixture.User}/credential-link",new{revision=1})).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/company/accounts",new{firstName="Bad",lastName="Admin",role="Administrator",enabled=true})).StatusCode);
 },LocalLoginTests.Local);
 [Fact] public async Task InvitationCompletesWithoutAuthorityInjectionAndConvertsReservation()=>await IdentityFixture.Run(async f=>
 {
  f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);var invitation=await f.Send(HttpMethod.Post,"/api/company/invitations",new{email="invited@example.test",role="Viewer"});Assert.Equal(HttpStatusCode.OK,invitation.StatusCode);var token=(await invitation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
  await f.Send(HttpMethod.Post,"/api/session/logout",new{});var input=new{token,username="invited",firstName="Invited",lastName="Person",password=LocalLoginTests.Password,confirmPassword=LocalLoginTests.Password,role="Administrator",companyId=Guid.NewGuid()};
  Assert.Equal(HttpStatusCode.OK,(await LocalLoginTests.Public(f,"/api/auth/setup/details",new{token})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await LocalLoginTests.Public(f,"/api/auth/invitations/complete",input)).StatusCode);Assert.Equal("Viewer",await f.Scalar("SELECT role FROM memberships WHERE user_id<> '10000000-0000-0000-0000-000000000001'"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_invitations WHERE state='Pending'"));Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships"));
  Assert.Equal(HttpStatusCode.BadRequest,(await LocalLoginTests.Public(f,"/api/auth/invitations/complete",input)).StatusCode);
 },LocalLoginTests.Local);
 [Fact] public async Task CompanyProvisioningAndInvitationAuditFailuresRollBack()=>await IdentityFixture.Run(async f=>
 {
  await CompanyAccountTests.Owner(f);await f.Sql("CREATE FUNCTION reject_local_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action IN ('CredentialSetupIssued','InvitationAccepted') THEN RAISE EXCEPTION 'audit unavailable'; END IF; RETURN NEW; END $$; CREATE TRIGGER reject_local_audit BEFORE INSERT ON account_audit FOR EACH ROW EXECUTE FUNCTION reject_local_audit()");
  var service=new Clary.Api.LocalIdentity.LocalProvisioningService(f.Database,new(f.Database),new(),new());var owner=(Guid)(await f.Scalar("SELECT user_id FROM operator_owner"))!;var users=await f.Scalar("SELECT count(*) FROM users");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>service.CreateCompanyAsync(new(owner),new("Rolled back",new(PlanKind.Ten,null,null),new("First","Last",null,CompanyRole.Administrator,true,null)),default));
  Assert.Equal(users,await f.Scalar("SELECT count(*) FROM users"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM companies"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credentials"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens"));
  f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);var response=await f.Send(HttpMethod.Post,"/api/company/invitations",new{email="rollback@example.test",role="Editor"});var token=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
  var auth=new Clary.Api.LocalIdentity.LocalAuthenticationService(f.Database,new(),new(),new());await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>auth.CompleteInvitationAsync(new(token,"rollback","First","Last",LocalLoginTests.Password,LocalLoginTests.Password),default));Assert.Equal(users,await f.Scalar("SELECT count(*) FROM users"));Assert.Equal("Pending",await f.Scalar("SELECT state FROM account_invitations"));
 },LocalLoginTests.Local);

 [Fact] public async Task ResetAuthorityRevisionAttachmentAndDisableAreEnforced()=>await IdentityFixture.Run(async f=>
 {
  var service=new Clary.Api.LocalIdentity.LocalProvisioningService(f.Database,new(f.Database),new(),new());var actor=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));
  var target=new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,Guid.NewGuid(),CompanyRole.Editor,null,null);await FixtureCompany.AddActorAsync(f.Database,target);var original=await f.Scalar($"SELECT issuer||'/'||identity_subject||'/'||display_name FROM users WHERE id='{target.UserId}'");
  await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>service.IssueAsync(actor,IdentityFixture.Company,IdentityFixture.User,new(1),default));
  await Assert.ThrowsAsync<Clary.Api.Identity.CompanyConflict>(()=>service.IssueAsync(actor,IdentityFixture.Company,target.UserId,new(99),default));
  var link=await service.IssueAsync(actor,IdentityFixture.Company,target.UserId,new(1),default);Assert.Equal(original,await f.Scalar($"SELECT issuer||'/'||identity_subject||'/'||display_name FROM users WHERE id='{target.UserId}'"));Assert.Null(await f.Scalar($"SELECT first_name FROM local_credentials WHERE user_id='{target.UserId}'") as string);
  var auth=new Clary.Api.LocalIdentity.LocalAuthenticationService(f.Database,new(),new(),new());var token=link.Link.Split("token=")[1];var next=await service.IssueAsync(actor,IdentityFixture.Company,target.UserId,new(1),default);await Assert.ThrowsAsync<ArgumentException>(()=>auth.DetailsAsync(token,default));
  await service.UpdateAsync(actor,IdentityFixture.Company,target.UserId,new("First","Last",null,CompanyRole.Editor,false,1),default);await service.UpdateAsync(actor,IdentityFixture.Company,target.UserId,new("First","Last",null,CompanyRole.Editor,true,2),default);await Assert.ThrowsAsync<ArgumentException>(()=>auth.DetailsAsync(next.Link.Split("token=")[1],default));
  await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>service.IssueAsync(AccountActor.For(actor.Company! with{CompanyId=Guid.NewGuid()}),IdentityFixture.Company,target.UserId,new(3),default));
 },LocalLoginTests.Local);
 [Fact] public async Task ConcurrentInvitationUsernameHasOneWinnerAndNoPartialSeat()=>await IdentityFixture.Run(async f=>
 {
  var invitations=new InvitationService(f.Database);var actor=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));var first=await invitations.IssueAsync(actor,IdentityFixture.Company,new("first@example.test",CompanyRole.Editor),default);var second=await invitations.IssueAsync(actor,IdentityFixture.Company,new("second@example.test",CompanyRole.Viewer),default);
  var auth=new Clary.Api.LocalIdentity.LocalAuthenticationService(f.Database,new(),new(),new());async Task<bool> Complete(string token){try{await auth.CompleteInvitationAsync(new(token,"shared","First","Last",LocalLoginTests.Password,LocalLoginTests.Password),default);return true;}catch(Npgsql.PostgresException e) when(e.SqlState==Npgsql.PostgresErrorCodes.UniqueViolation){return false;}}
  Assert.Single(await Task.WhenAll(Complete(first.Token),Complete(second.Token)),x=>x);Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM users"));Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM account_invitations WHERE state='Pending'"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credentials"));
 },LocalLoginTests.Local);
 [Fact] public async Task ReservedInvitationConvertsAfterPlanReductionAndRejectsSetupPurpose()=>await IdentityFixture.Run(async f=>
 {
  var actor=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));var invitation=await new InvitationService(f.Database).IssueAsync(actor,IdentityFixture.Company,new("reduced@example.test",CompanyRole.Editor),default);await f.Sql("INSERT INTO users(id,issuer,identity_subject,display_name) SELECT ('30000000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,'fixture','extra-'||n,'Extra' FROM generate_series(1,9) n; INSERT INTO memberships(company_id,user_id,role) SELECT '00000000-0000-0000-0000-000000000001',id,'Editor' FROM users WHERE issuer='fixture'; UPDATE companies SET plan_kind='Ten',seat_limit=10");var auth=new Clary.Api.LocalIdentity.LocalAuthenticationService(f.Database,new(),new(),new());
  var input=new Clary.Api.LocalIdentity.CredentialSetupInput(invitation.Token,"reduced","First","Last",LocalLoginTests.Password,LocalLoginTests.Password);await Assert.ThrowsAsync<ArgumentException>(()=>auth.SetupAsync(input,default));await auth.CompleteInvitationAsync(input,default);Assert.Equal(11L,await f.Scalar("SELECT count(*) FROM memberships"));
 },LocalLoginTests.Local);

 [Fact] public async Task InvitationExpiryAfterLockWaitLeavesNoPartialIdentity()=>await IdentityFixture.Run(async f=>
 {
  var actor=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));var invitation=await new InvitationService(f.Database).IssueAsync(actor,IdentityFixture.Company,new("expiry@example.test",CompanyRole.Editor),default);await f.Sql("UPDATE account_invitations SET created_at=now()-interval '6 days 23 hours 59 minutes 58 seconds',expires_at=now()+interval '2 seconds'");
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();await using(var q=new Npgsql.NpgsqlCommand("SELECT id FROM account_invitations FOR UPDATE",c,tx))await q.ExecuteScalarAsync();var auth=new Clary.Api.LocalIdentity.LocalAuthenticationService(f.Database,new(),new(),new());var pending=auth.CompleteInvitationAsync(new(invitation.Token,"expiry","First","Last",LocalLoginTests.Password,LocalLoginTests.Password),default);await Task.Delay(300);Assert.False(pending.IsCompleted);await Task.Delay(2000);await tx.CommitAsync();await Assert.ThrowsAsync<ArgumentException>(()=>pending);Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM users"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credentials"));
 },LocalLoginTests.Local);

 [Fact] public async Task FullLengthLocalNamesArePreserved()=>await IdentityFixture.Run(async f=>
 {
  var service=new Clary.Api.LocalIdentity.LocalProvisioningService(f.Database,new(f.Database),new(),new());var actor=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));var first=new string('F',100);var last=new string('L',100);var account=await service.CreateAsync(actor,IdentityFixture.Company,new(first,last,null,CompanyRole.Editor,true,null),default);Assert.Equal(first,account.Account.LocalProfile!.FirstName);Assert.Equal(last,account.Account.LocalProfile.LastName);Assert.Equal(first+" "+last,account.Account.DisplayName);
 },LocalLoginTests.Local);

 [Theory] [InlineData(false)] [InlineData(true)]
 public async Task ExplicitLegacyProfileEditPersistsWithoutChangingIdentityCredentialsOrSeats(bool configured)=>await IdentityFixture.Run(async f=>
 {
  var target=new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,Guid.NewGuid(),CompanyRole.Editor,null,null);await FixtureCompany.AddActorAsync(f.Database,target);var identity=await f.Scalar($"SELECT issuer||'/'||identity_subject FROM users WHERE id='{target.UserId}'");var seats=await f.Scalar("SELECT count(*) FROM memberships WHERE active");object? password=null;
  if(configured){await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var repo=new Clary.Api.LocalIdentity.LocalCredentialRepository();await repo.CreateUnsetAsync(c,tx,target.UserId,default);await repo.SetAsync(c,tx,target.UserId,"legacy-user","Old","Profile",new Clary.Api.LocalIdentity.LocalPasswordService().Hash(LocalLoginTests.Password),default);await tx.CommitAsync();password=await f.Scalar($"SELECT password_hash FROM local_credentials WHERE user_id='{target.UserId}'");}
  var actor=AccountActor.For(new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));var service=new Clary.Api.LocalIdentity.LocalProvisioningService(f.Database,new(f.Database),new(),new());var result=await service.UpdateAsync(actor,IdentityFixture.Company,target.UserId,new("Explicit","Names",null,CompanyRole.Editor,true,1),default);
  Assert.Equal(new Clary.Api.LocalIdentity.LocalProfile(configured?"legacy-user":null,"Explicit","Names",configured),result.LocalProfile);Assert.Equal("Explicit Names",result.DisplayName);Assert.Equal(identity,await f.Scalar($"SELECT issuer||'/'||identity_subject FROM users WHERE id='{target.UserId}'"));Assert.Equal(seats,await f.Scalar("SELECT count(*) FROM memberships WHERE active"));Assert.Equal("Explicit",await f.Scalar($"SELECT first_name FROM local_credentials WHERE user_id='{target.UserId}'"));Assert.Equal("Names",await f.Scalar($"SELECT last_name FROM local_credentials WHERE user_id='{target.UserId}'"));var after=await f.Scalar($"SELECT password_hash FROM local_credentials WHERE user_id='{target.UserId}'");Assert.True(configured?Equals(password,after):after is null or DBNull);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens"));
 },LocalLoginTests.Local);

}
