using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Clary.Api.Accounts;
using Clary.Api.Identity;
using Clary.Api.LocalIdentity;

public class AccountDeletionTests
{
 [Theory][InlineData(false)][InlineData(true)]
 public async Task ReattachedIdentityNeverReusesDeletedMembershipRevision(bool invitation)=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f);var repo=new CompanyAccountRepository(f.Database);
  var actor=AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));
  // Include revisions from edits, multiple removals and cross-company reattachment.
  var old=await repo.UpdateAccountAsync(actor,IdentityFixture.Company,id,new("https://accounts.example","delete-target","Changed",null,CompanyRole.Editor,true,1),default);
  await repo.DeleteAccountAsync(actor,IdentityFixture.Company,id,new(old.Revision),default);
  AccountDto attached;
  if(invitation){
   var service=new InvitationService(f.Database);var issued=await service.IssueAsync(actor,IdentityFixture.Company,new("delete-target@example.test",CompanyRole.Viewer,"https://accounts.example","delete-target"),default);
   var principal=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim("iss","https://accounts.example"),new System.Security.Claims.Claim("sub","delete-target")},"test"));
   attached=await service.AcceptAsync(principal,issued.Token,default);
  }else attached=await repo.CreateAccountAsync(actor,IdentityFixture.Company,new("https://accounts.example","delete-target","Returned",null,CompanyRole.Viewer,true,null),default);
  Assert.True(attached.Revision>old.Revision+1);Assert.Equal(id,attached.Id);
  foreach(var stale in new[]{1,old.Revision}){
   Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{revision=stale})).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{id}",CompanyAccountTests.Account("delete-target",role:"Editor",revision:stale))).StatusCode);
  }
  Assert.Equal("Viewer",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{id}'"));
  await repo.DeleteAccountAsync(actor,IdentityFixture.Company,id,new(attached.Revision),default);
  var foreign=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign}','Returned tenant')");var owner=await CompanyAccountTests.Owner(f);
  var cross=await repo.CreateAccountAsync(AccountActor.For(new OperatorContext(owner)),foreign,new("https://accounts.example","delete-target","Returned",null,CompanyRole.Editor,true,null),default);
  Assert.True(cross.Revision>attached.Revision+1);
 });
 private static async Task<Guid> Create(IdentityFixture f,string role="Editor")
 {
  var r=await f.Send(HttpMethod.Post,"/api/company/accounts",CompanyAccountTests.Account("delete-target",role:role));
  Assert.Equal(HttpStatusCode.OK,r.StatusCode);return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
 }
 [Fact]public async Task RemovedLocalIdentityCannotLoginOrUseOldContentCookie()=>await IdentityFixture.Run(async f=>{
  var actor=AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));
  var result=await new LocalProvisioningService(f.Database,new(f.Database),new(),new()).CreateAsync(actor,IdentityFixture.Company,new("Removed","Person","removed@example.test",CompanyRole.Editor,true,null),default);
  var id=result.Account.Id;
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync()){
   await new LocalCredentialRepository().SetAsync(c,tx,id,"removed","Removed","Person",new LocalPasswordService().Hash(LocalLoginTests.Password),default);await tx.CommitAsync();
  }
  Assert.Equal(HttpStatusCode.OK,(await LocalLoginTests.Public(f,"/api/auth/login",new{username="removed",password=LocalLoginTests.Password})).StatusCode);
  var oldCookie=f.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
  Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/documents")).StatusCode);
  f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{revision=1})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/logout",new{})).StatusCode);
  f.Client.DefaultRequestHeaders.Remove("Cookie");f.Client.DefaultRequestHeaders.Add("Cookie",oldCookie);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/documents")).StatusCode);
  f.Client.DefaultRequestHeaders.Remove("Cookie");
  Assert.Equal(HttpStatusCode.Unauthorized,(await LocalLoginTests.Public(f,"/api/auth/login",new{username="removed",password=LocalLoginTests.Password})).StatusCode);
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM local_credentials WHERE user_id='{id}' AND password_hash IS NOT NULL"));
 },LocalLoginTests.Local);
 [Fact]public async Task ActualForeignCompanyTargetIsInvisibleToAdministrator()=>await IdentityFixture.Run(async f=>{
  var owner=await CompanyAccountTests.Owner(f);var foreign=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign}','Foreign')");
  var row=await new CompanyAccountRepository(f.Database).CreateAccountAsync(AccountActor.For(new OperatorContext(owner)),foreign,new("https://accounts.example","foreign-delete","Foreign",null,CompanyRole.Editor,true,null),default);
  await f.Login();Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{row.Id}",new{revision=row.Revision})).StatusCode);
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{row.Id}' AND company_id='{foreign}'"));
 });
 [Theory][InlineData("Editor")][InlineData("Viewer")]
 public async Task DeleteRemovesOnlyMembershipRevokesSessionsAndFreesSeat(string role)=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f,role);
  await f.Sql($"INSERT INTO account_sessions(id,user_id,expires_at,last_activity_at) VALUES(gen_random_uuid(),'{id}',clock_timestamp()+interval '1 hour',clock_timestamp())");
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{revision=1})).StatusCode);
  Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{id}'"));
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM users WHERE id='{id}' AND active"));
  Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM account_sessions WHERE user_id='{id}' AND revoked_at IS NULL"));
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM account_audit WHERE action='AccountDeleted' AND target_id='{id}' AND before_value IS NOT NULL AND after_value IS NOT NULL"));
  Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Put,$"/api/company/accounts/{id}",CompanyAccountTests.Account("delete-target",revision:1))).StatusCode);
  Assert.Equal(1,(await f.Client.GetFromJsonAsync<JsonElement>("/api/company/seats")).GetProperty("enabled").GetInt32());
 });
 [Fact]public async Task StaleMissingRevisionSelfAndProtectedTargetsFail()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f);
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{})).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{revision=9})).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{IdentityFixture.User}",new{revision=1})).StatusCode);
  var owner=await CompanyAccountTests.Owner(f);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Delete,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{IdentityFixture.User}",new{revision=1})).StatusCode);
  await f.Sql($"INSERT INTO memberships(company_id,user_id,role) VALUES('{IdentityFixture.Company}','{owner}','Editor')");await f.Login();
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{owner}",new{revision=1})).StatusCode);
 });
 [Fact]public async Task AuditFailureRollsBackMembershipAndSessionRevocation()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f);
  await f.Sql($"INSERT INTO local_credentials(user_id) VALUES('{id}'); INSERT INTO local_credential_tokens(id,user_id,purpose,token_hash,credential_revision,created_at,expires_at) VALUES(gen_random_uuid(),'{id}','Setup',decode(repeat('ab',32),'hex'),1,now(),now()+interval '30 minutes')");
  await f.Sql($"INSERT INTO account_sessions(id,user_id,expires_at,last_activity_at) VALUES(gen_random_uuid(),'{id}',clock_timestamp()+interval '1 hour',clock_timestamp()); CREATE FUNCTION reject_delete_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit unavailable'; END $$; CREATE TRIGGER reject_delete_audit BEFORE INSERT ON account_audit FOR EACH ROW EXECUTE FUNCTION reject_delete_audit()");
  Assert.Equal(HttpStatusCode.InternalServerError,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{revision=1})).StatusCode);
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{id}'"));
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM account_sessions WHERE user_id='{id}' AND revoked_at IS NULL"));
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM local_credential_tokens WHERE user_id='{id}' AND revoked_at IS NULL"));
 });
 [Fact]public async Task OperatorCanRemoveViewerAndPendingCredentialTokenCannotBeConsumed()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f,"Viewer");
  await f.Sql($"INSERT INTO local_credentials(user_id) VALUES('{id}')");
  string token;
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync()){
   var link=await new LocalCredentialTokens().IssueAsync(c,tx,id,CredentialTokenPurpose.Setup,default);token=link.Link.Split("token=")[1];await tx.CommitAsync();
  }
  await CompanyAccountTests.Owner(f);
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Delete,$"/api/operator/companies/{IdentityFixture.Company}/accounts/{id}",new{revision=1})).StatusCode);
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM local_credentials WHERE user_id='{id}'"));
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM local_credential_tokens WHERE user_id='{id}' AND revoked_at IS NOT NULL"));
  await using var c2=await f.Database.OpenConnectionAsync();await using var tx2=await c2.BeginTransactionAsync();
  Assert.Null(await new LocalCredentialTokens().ConsumeAsync(c2,tx2,token,CredentialTokenPurpose.Setup,default));
 });
 [Fact]public async Task StrictInputForeignScopeCsrfAndEditorAuthorityAreEnforced()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f);
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{id}",new{revision=1,role="Administrator"})).StatusCode);
  using(var request=new HttpRequestMessage(HttpMethod.Delete,$"/api/company/accounts/{id}"){Content=JsonContent.Create(new{revision=1})})
   Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.SendAsync(request)).StatusCode);
  var foreign=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign}','Foreign')");
  var repo=new CompanyAccountRepository(f.Database);
  await Assert.ThrowsAsync<CompanyDenied>(()=>repo.DeleteAccountAsync(AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null)),foreign,id,new(1),default));
  f.SignInAs("https://accounts.example","delete-target");
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Delete,$"/api/company/accounts/{IdentityFixture.User}",new{revision=1})).StatusCode);
 });
 [Fact]public async Task RepositoryRechecksPersistedAuthorityAndSession()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f);var repo=new CompanyAccountRepository(f.Database);
  var actor=AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));
  await f.Sql($"UPDATE memberships SET role='Editor' WHERE user_id='{IdentityFixture.User}'");
  await Assert.ThrowsAsync<CompanyDenied>(()=>repo.DeleteAccountAsync(actor,IdentityFixture.Company,id,new(1),default));
  await f.Sql($"UPDATE memberships SET role='Administrator' WHERE user_id='{IdentityFixture.User}'");
  var session=(Guid)(await f.Scalar($"SELECT id FROM account_sessions WHERE user_id='{IdentityFixture.User}' LIMIT 1"))!;
  await f.Sql($"UPDATE account_sessions SET revoked_at=clock_timestamp() WHERE id='{session}'");
  actor=AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null,SessionId:session));
  await Assert.ThrowsAsync<CompanyDenied>(()=>repo.DeleteAccountAsync(actor,IdentityFixture.Company,id,new(1),default));
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{id}'"));
 });
 [Fact]public async Task ConcurrentStaleUpdateAndDeleteCannotRestoreMembership()=>await IdentityFixture.Run(async f=>{
  await f.Login();var id=await Create(f);var repo=new CompanyAccountRepository(f.Database);
  var actor=AccountActor.For(new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null));
  var update=repo.UpdateAccountAsync(actor,IdentityFixture.Company,id,new("https://accounts.example","delete-target","Changed",null,CompanyRole.Editor,true,1),default);
  var delete=repo.DeleteAccountAsync(actor,IdentityFixture.Company,id,new(1),default);
  try{await Task.WhenAll(update,delete);}catch(Exception e){Assert.True(e is CompanyConflict or CompanyMissing);}
  if(update.IsCompletedSuccessfully){Assert.True(delete.IsFaulted);await repo.DeleteAccountAsync(actor,IdentityFixture.Company,id,new(2),default);}
  else {Assert.True(delete.IsCompletedSuccessfully);Assert.True(update.IsFaulted);}
  Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{id}'"));
 });
}

