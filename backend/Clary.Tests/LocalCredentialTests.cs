using Clary.Api;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using Xunit;
using Clary.Api.LocalIdentity;

public class LocalCredentialTests
{
 [Fact] public void PasswordHashAndBounds()
 {
  var service=new LocalPasswordService();
  Assert.False((bool)service.Validate(new string('a',14),new string('a',14)));
  Assert.True((bool)service.Validate(new string('a',15),new string('a',15)));
  Assert.True((bool)service.Validate(new string('界',128),new string('界',128)));
  Assert.False((bool)service.Validate(new string('a',129),new string('a',129)));
  Assert.False((bool)service.Validate("123456789012345 ","123456789012345"));
  string hash=service.Hash("123456789012345 ");
  Assert.NotEqual(hash,(string)service.Hash("123456789012345 "));
  Assert.Equal(PasswordVerificationResult.Success,(PasswordVerificationResult)service.Verify(hash,"123456789012345 "));
  Assert.Equal(PasswordVerificationResult.Failed,(PasswordVerificationResult)service.Verify(hash,"123456789012345"));
  Assert.Equal(PasswordVerificationResult.Failed,(PasswordVerificationResult)service.Verify("broken","password"));
  var data=Convert.FromBase64String(hash);
  Assert.Equal(1,data[0]);
  Assert.Equal(2,System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1,4)));
  Assert.True(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(5,4))>=220000);
  var old=new PasswordHasher<object>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions{IterationCount=10000})).HashPassword(new(),"123456789012345");
  Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,(PasswordVerificationResult)service.Verify(old,"123456789012345"));
 }
 [Fact] public async Task UsernameNormalizationAndUniqueConstraint()=>await IdentityFixture.Run(async f=>
 {
  Assert.Equal("abc",LocalCredentialRepository.NormalizeUsername(" AbC "));
  Assert.Equal(new string('a',64),LocalCredentialRepository.NormalizeUsername(new string('A',64)));
  foreach(var name in new[]{"ab",new string('a',65),"josé","a+b","a b","a/b","a\nb"})Assert.Throws<ArgumentException>(()=>LocalCredentialRepository.NormalizeUsername(name));
  LocalCredentialRepository.ValidateNames("李","O’Connor");
  Assert.Throws<ArgumentException>(()=>LocalCredentialRepository.ValidateNames(" ","Last"));
  Assert.Throws<ArgumentException>(()=>LocalCredentialRepository.ValidateNames(new string('a',101),"Last"));
  var repo=new LocalCredentialRepository();
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync())
  {
   await repo.CreateUnsetAsync(c,tx,IdentityFixture.User,default);
   Assert.Equal(new LocalProfile(null,null,null,false),await repo.ProfileAsync(c,tx,IdentityFixture.User,default));
   await tx.CommitAsync();
  }
  Assert.Equal("Bootstrap",await f.Scalar($"SELECT display_name FROM users WHERE id='{IdentityFixture.User}'"));
  async Task<bool> Compete(string username)
  {
   await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
   try{
    var id=await repo.CreateIdentityAsync(c,tx,"Élodie","李",null,default);
    await repo.CreateUnsetAsync(c,tx,id,default);
    await using(var q=new NpgsqlCommand("INSERT INTO memberships(company_id,user_id,role) VALUES(@company,@user,'Editor')",c,tx)){q.Parameters.AddWithValue("company",IdentityFixture.Company);q.Parameters.AddWithValue("user",id);await q.ExecuteNonQueryAsync();}
    await repo.SetAsync(c,tx,id,username,"Élodie","李","opaque-hash",default);await tx.CommitAsync();return true;
   }catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation){return false;}
  }
  Assert.Single(await Task.WhenAll(Compete(" AbC "),Compete("abc")),x=>x);
  Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM users"));
  Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM memberships"));
  Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credentials WHERE normalized_username='abc'"));
  Assert.Equal("Élodie 李",await f.Scalar("SELECT display_name FROM users WHERE issuer='https://brie.local/identity'"));
 });
 [Fact] public async Task TokenPurposeExpiryAndSecrecy()=>await IdentityFixture.Run(async f=>
 {
  var repo=new LocalCredentialRepository();var tokens=new LocalCredentialTokens();CredentialLink link;
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync())
  {await repo.CreateUnsetAsync(c,tx,IdentityFixture.User,default);link=await tokens.IssueAsync(c,tx,IdentityFixture.User,CredentialTokenPurpose.Setup,default);await tx.CommitAsync();}
  string Raw(CredentialLink l)=>new Uri("https://localhost"+l.Link).Fragment.Split('=',2)[1];
  var raw=Raw(link);
  Assert.Equal(32,Convert.FromHexString((string)(await f.Scalar("SELECT encode(token_hash,'hex') FROM local_credential_tokens"))!).Length);
  Assert.Equal(true,await f.Scalar("SELECT expires_at=created_at+interval '30 minutes' FROM local_credential_tokens"));
  Assert.DoesNotContain(raw,(string)(await f.Scalar("SELECT row_to_json(t)::text FROM local_credential_tokens t"))!);
  async Task<Guid?> Consume(string token,CredentialTokenPurpose purpose){await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var id=await tokens.ConsumeAsync(c,tx,token,purpose,default);await tx.CommitAsync();return id;}
  Assert.Null(await Consume(raw,CredentialTokenPurpose.Reset));
  Assert.Equal(IdentityFixture.User,await Consume(raw,CredentialTokenPurpose.Setup));
  Assert.Null(await Consume(raw,CredentialTokenPurpose.Setup));
  CredentialLink second;
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync())
  {second=await tokens.IssueAsync(c,tx,IdentityFixture.User,CredentialTokenPurpose.Reset,default);await tx.CommitAsync();}
  await f.Sql("UPDATE local_credential_tokens SET created_at=now()-interval '31 minutes',expires_at=now()-interval '1 minute' WHERE consumed_at IS NULL");
  Assert.Null(await Consume(Raw(second),CredentialTokenPurpose.Reset));
  Assert.Null(await Consume("malformed",CredentialTokenPurpose.Setup));
 });
 [Fact] public async Task CredentialWriteRequiresExistingRowAndPreservesLegacyOnFailure()=>await IdentityFixture.Run(async f=>
 {
  await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
  await Assert.ThrowsAsync<InvalidOperationException>(()=>new LocalCredentialRepository().SetAsync(c,tx,IdentityFixture.User,"abc","First","Last","opaque",default));
  await tx.RollbackAsync();
  Assert.Equal("Bootstrap",await f.Scalar($"SELECT display_name FROM users WHERE id='{IdentityFixture.User}'"));
 });
 [Fact] public async Task TokensAreSupersededRevisionBoundAndTransactionScoped()=>await IdentityFixture.Run(async f=>
 {
  var repo=new LocalCredentialRepository();var tokens=new LocalCredentialTokens();
  async Task<CredentialLink> Issue(bool commit=true){await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var link=await tokens.IssueAsync(c,tx,IdentityFixture.User,CredentialTokenPurpose.Setup,default);if(commit)await tx.CommitAsync();return link;}
  async Task<Guid?> Consume(CredentialLink link){await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var id=await tokens.ConsumeAsync(c,tx,link.Link.Split('=')[1],CredentialTokenPurpose.Setup,default);await tx.CommitAsync();return id;}
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync()){await repo.CreateUnsetAsync(c,tx,IdentityFixture.User,default);await tx.CommitAsync();}
  var first=await Issue();var rolledBack=await Issue(false);Assert.Null(await Consume(rolledBack));
  var second=await Issue();Assert.Null(await Consume(first));
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync()){await repo.SetAsync(c,tx,IdentityFixture.User,"abc","First","Last","opaque",default);await tx.CommitAsync();}
  Assert.Null(await Consume(second));
 });
 [Fact] public async Task AnonymousSecurityEventsAreBoundedWithoutInventedActor()=>await IdentityFixture.Run(async f=>
 {
  await f.Sql("INSERT INTO local_auth_security_events(event) SELECT 'LoginFailed' FROM generate_series(1,10003)");
  Assert.Equal(10000L,await f.Scalar("SELECT count(*) FROM local_auth_security_events"));
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_auth_security_events WHERE actor_user_id IS NOT NULL"));
 });
 [Fact] public async Task TokenExpiryIsRecheckedAfterTokenLockWait()=>await IdentityFixture.Run(async f=>
 {
  var tokens=new LocalCredentialTokens();CredentialLink link;
  await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync())
  {await new LocalCredentialRepository().CreateUnsetAsync(c,tx,IdentityFixture.User,default);link=await tokens.IssueAsync(c,tx,IdentityFixture.User,CredentialTokenPurpose.Setup,default);await tx.CommitAsync();}
  await f.Sql("UPDATE local_credential_tokens SET created_at=now()-interval '29 minutes 59 seconds',expires_at=now()+interval '1 second'");
  await using var blocked=await f.Database.OpenConnectionAsync();await using var blocker=await blocked.BeginTransactionAsync();
  await using(var q=new NpgsqlCommand("SELECT id FROM local_credential_tokens FOR UPDATE",blocked,blocker))await q.ExecuteScalarAsync();
  async Task<Guid?> Consume(){await using var c=await f.Database.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var result=await tokens.ConsumeAsync(c,tx,link.Link.Split('=')[1],CredentialTokenPurpose.Setup,default);await tx.CommitAsync();return result;}
  var pending=Consume();await Task.Delay(200);Assert.False(pending.IsCompleted);
  await Task.Delay(1000);await blocker.CommitAsync();Assert.Null(await pending);
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE consumed_at IS NOT NULL"));
 });
}
