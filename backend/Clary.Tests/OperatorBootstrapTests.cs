using System.Security.Claims;
using Clary.Api.Accounts;
using Xunit;
public class OperatorBootstrapTests
{
 [Fact] public async Task BootstrapIsIdempotentAndRejectsReplacement()=>await AccountMigrationTests.Run(async db=>{
  await AccountMigrationTests.Apply(db);var bootstrap=new OperatorBootstrap(db);
  var owner=await bootstrap.InitializeAsync("https://issuer.example","owner");Assert.NotEqual(Guid.Empty,owner.UserId);
  Assert.Equal(owner,await bootstrap.InitializeAsync("https://issuer.example","owner"));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>bootstrap.InitializeAsync("https://issuer.example","other"));
  Assert.Equal(1L,await AccountMigrationTests.Scalar(db,"SELECT count(*) FROM operator_owner"));
 });
 [Fact] public async Task ConcurrentBootstrapHasExactlyOneOwner()=>await AccountMigrationTests.Run(async db=>{
  await AccountMigrationTests.Apply(db);var bootstrap=new OperatorBootstrap(db);
  async Task<bool> Attempt(string sub){try{await bootstrap.InitializeAsync("https://issuer.example",sub);return true;}catch(InvalidOperationException){return false;}}
  Assert.Single(await Task.WhenAll(Attempt("one"),Attempt("two")),x=>x);
 });
 [Theory][InlineData("test","owner")][InlineData("http://issuer.example","owner")][InlineData("https://issuer.example"," ")][InlineData("https://user:pass@issuer.example","owner")]
 public async Task InvalidBindingIsRejected(string issuer,string subject)=>await AccountMigrationTests.Run(async db=>{
  await AccountMigrationTests.Apply(db);await Assert.ThrowsAsync<ArgumentException>(()=>new OperatorBootstrap(db).InitializeAsync(issuer,subject));
 });
 [Fact] public async Task ResolveRequiresExactAuthenticatedActiveIdentity()=>await AccountMigrationTests.Run(async db=>{
  await AccountMigrationTests.Apply(db);var access=new OperatorAccess(db);
  ClaimsPrincipal Principal(string issuer="https://issuer.example",string subject="owner",bool authenticated=true)=>new(new ClaimsIdentity([new Claim("iss",issuer),new Claim("sub",subject),new Claim("email","owner@example.com")],authenticated?"test":null));
  Assert.Null(await access.ResolveAsync(Principal()));var owner=await new OperatorBootstrap(db).InitializeAsync("https://issuer.example","owner");
  Assert.Equal(owner,await access.ResolveAsync(Principal()));Assert.Null(await access.ResolveAsync(Principal(subject:"other")));Assert.Null(await access.ResolveAsync(Principal(issuer:"https://other.example")));Assert.Null(await access.ResolveAsync(Principal(authenticated:false)));
  await AccountMigrationTests.Sql(db,"UPDATE users SET active=false");Assert.Null(await access.ResolveAsync(Principal()));await Assert.ThrowsAsync<InvalidOperationException>(()=>new OperatorBootstrap(db).InitializeAsync("https://issuer.example","owner"));
 });

 [Fact] public async Task ExistingMemberBindingPreservesExplicitMembershipAndVisibility()=>await AccountMigrationTests.Run(async db=>{
  await AccountMigrationTests.Apply(db);
  await AccountMigrationTests.Sql(db,"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('10000000-0000-0000-0000-000000000001','https://issuer.example','owner','Existing name'); INSERT INTO memberships(company_id,user_id,role,visibility_scope) SELECT id,'10000000-0000-0000-0000-000000000001','Viewer','AssignedOnly' FROM companies;");
  var before=await AccountMigrationTests.Scalar(db,"SELECT jsonb_agg(to_jsonb(m))::text FROM memberships m");
  var owner=await new OperatorBootstrap(db).InitializeAsync("https://issuer.example","owner");
  Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"),owner.UserId);
  Assert.Equal(before,await AccountMigrationTests.Scalar(db,"SELECT jsonb_agg(to_jsonb(m))::text FROM memberships m"));
  var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("iss","https://issuer.example"),new Claim("sub","owner")],"test"));
  Assert.Equal(owner,await new OperatorAccess(db).ResolveAsync(principal));
  var access=new Clary.Api.Identity.CompanyAccess(db);var company=Guid.Parse("00000000-0000-0000-0000-000000000001");
  var context=await access.ResolveAsync(principal,company,default);Assert.NotNull(context);Assert.Equal(CompanyRole.Viewer,context.Role);Assert.Equal(CompanyVisibilityScope.AssignedOnly,context.VisibilityScope);
  Assert.Null(await access.ResolveAsync(principal,Guid.NewGuid(),default));
 });
}
