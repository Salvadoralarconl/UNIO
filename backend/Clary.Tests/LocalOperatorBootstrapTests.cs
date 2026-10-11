using Clary.Api;
using Xunit;
using Clary.Api.LocalIdentity;
using Clary.Api.Accounts;
using System.Security.AccessControl;
using System.Security.Principal;
public class LocalOperatorBootstrapTests
{
 static string Output()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Brie","identity-test-"+Guid.NewGuid().ToString("N"),"setup.txt");
 [Fact] public async Task RecoveryResetSupersedesLinksRevokesSessionsAndRollsBackAuditFailure()=>await IdentityFixture.Run(async f=>
 {
  var files=new[]{Output(),Output(),Output(),Output()};
  try{
   var service=new LocalOperatorBootstrap(f.Database);var id=await service.InitializeAsync(files[0]);
   await f.Sql($"UPDATE local_credentials SET username='owner',normalized_username='owner',first_name='Local',last_name='Owner',password_hash='configured' WHERE user_id='{id}'; INSERT INTO account_sessions(id,user_id,expires_at) VALUES('{Guid.NewGuid()}','{id}',clock_timestamp()+interval '1 hour')");
   Assert.Equal(id,await service.RecoverAsync(files[1]));
   Assert.Equal("Reset",await f.Scalar("SELECT purpose FROM local_credential_tokens WHERE revoked_at IS NULL"));
   Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM account_sessions WHERE revoked_at IS NULL"));
   var raw=(await File.ReadAllTextAsync(files[1])).Trim().Split('=')[1];
   await service.RecoverAsync(files[2]);
   await using(var c=await f.Database.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync())Assert.Null(await new LocalCredentialTokens().ConsumeAsync(c,tx,raw,CredentialTokenPurpose.Reset,CancellationToken.None));
   Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE revoked_at IS NULL"));
   await f.Sql("CREATE FUNCTION fail_recovery_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit failure'; END $$; CREATE TRIGGER fail_recovery_audit BEFORE INSERT ON account_audit FOR EACH ROW EXECUTE FUNCTION fail_recovery_audit()");
   await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>service.RecoverAsync(files[3]));
   Assert.False(File.Exists(files[3]));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE revoked_at IS NULL"));
   Assert.DoesNotContain(raw,(string)(await f.Scalar("SELECT string_agg(row_to_json(a)::text,'') FROM account_audit a"))!);
  }finally{foreach(var file in files)if(File.Exists(file))File.Delete(file);}
 });
 [Fact] public async Task RecoveryRejectsNonlocalMismatchedDisabledAndUnsafeFile()=>await IdentityFixture.Run(async f=>
 {
  var files=new[]{Output(),Output()};try{
   var service=new LocalOperatorBootstrap(f.Database);
   await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RecoverAsync(files[1]));
   var id=await service.InitializeAsync(files[0]);
   await Assert.ThrowsAsync<ArgumentException>(()=>service.RecoverAsync(files[0]));
   await f.Sql("ALTER TABLE operator_owner DISABLE TRIGGER operator_owner_guard; UPDATE operator_owner SET subject='mismatch'");await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RecoverAsync(files[1]));
   await f.Sql("ALTER TABLE operator_owner DISABLE TRIGGER operator_owner_guard; UPDATE operator_owner SET subject=(SELECT identity_subject FROM users WHERE id=operator_owner.user_id); UPDATE users SET active=false WHERE id=(SELECT user_id FROM operator_owner)");await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RecoverAsync(files[1]));
   await f.Sql("ALTER TABLE users DISABLE TRIGGER account_identity_guard; UPDATE users SET active=true,issuer='https://external.example' WHERE id=(SELECT user_id FROM operator_owner); UPDATE operator_owner SET issuer='https://external.example'");await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RecoverAsync(files[1]));
   Assert.False(File.Exists(files[1]));Assert.Equal(id,await f.Scalar("SELECT user_id FROM operator_owner"));
  }finally{foreach(var file in files)if(File.Exists(file))File.Delete(file);}
 });
 [Fact] public async Task RecoveryReissuesExpiredSetupAndPreservesIdentity()=>await IdentityFixture.Run(async f=>
 {
  var first=Output();var second=Output();
  try{
   var service=new LocalOperatorBootstrap(f.Database);var id=await service.InitializeAsync(first);
   var subject=await f.Scalar("SELECT subject FROM operator_owner");
   await f.Sql("WITH time AS (SELECT clock_timestamp()-interval '31 minutes' AS at) UPDATE local_credential_tokens SET created_at=time.at,expires_at=time.at+interval '30 minutes' FROM time");
   var method=typeof(LocalOperatorBootstrap).GetMethod("RecoverAsync");Assert.NotNull(method);
   Assert.Equal(id,await (Task<Guid>)method!.Invoke(service,new object[]{second,CancellationToken.None})!);
   Assert.Equal(subject,await f.Scalar("SELECT subject FROM operator_owner"));
   Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM local_credential_tokens WHERE revoked_at IS NULL"));
   Assert.Equal("Setup",await f.Scalar("SELECT purpose FROM local_credential_tokens WHERE revoked_at IS NULL"));
   Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{id}'"));
  }finally{foreach(var file in new[]{first,second})if(File.Exists(file))File.Delete(file);}
 });
 [Fact] public async Task OnlyOneOfflineOwner()=>await IdentityFixture.Run(async f=>
 {
  var files=new[]{Output(),Output()};var bootstrap=new LocalOperatorBootstrap(f.Database);
  try{
   async Task<Guid?> Run(string file){try{return await bootstrap.InitializeAsync(file);}catch(InvalidOperationException){return null;}}
   var results=await Task.WhenAll(files.Select(Run));Assert.Single(results,x=>x.HasValue);
   var owner=results.Single(x=>x.HasValue)!.Value;
   Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM operator_owner"));
   Assert.Equal(0L,await f.Scalar($"SELECT count(*) FROM memberships WHERE user_id='{owner}'"));
   Assert.Equal(true,await f.Scalar($"SELECT username IS NULL AND password_hash IS NULL FROM local_credentials WHERE user_id='{owner}'"));
   Assert.Equal("https://brie.local/identity",await f.Scalar("SELECT issuer FROM operator_owner"));
   var file=files.Single(File.Exists);var text=await File.ReadAllTextAsync(file);Assert.Contains("/setup-account#token=",text);
   if(OperatingSystem.IsWindows())
   {
    Assert.True(new FileInfo(file).GetAccessControl().AreAccessRulesProtected);
    var currentUser=WindowsIdentity.GetCurrent().User;
    foreach(FileSystemAccessRule rule in new FileInfo(file).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier)))Assert.Equal(currentUser,rule.IdentityReference);
   }
   await Assert.ThrowsAsync<InvalidOperationException>(()=>bootstrap.InitializeAsync(Output()));
   Assert.Equal(owner,await f.Scalar("SELECT user_id FROM operator_owner"));
  }finally{foreach(var file in files)if(File.Exists(file))File.Delete(file);}
 });
 [Fact] public async Task ExistingImmutableOwnerAndUnsafeOutputAreRefused()=>await IdentityFixture.Run(async f=>
 {
  var owner=await new OperatorBootstrap(f.Database).InitializeAsync("https://owner.example","original");
  var output=Output();
  await Assert.ThrowsAsync<InvalidOperationException>(()=>new LocalOperatorBootstrap(f.Database).InitializeAsync(output));
  Assert.False(File.Exists(output));Assert.Equal(owner.UserId,await f.Scalar("SELECT user_id FROM operator_owner"));
  await Assert.ThrowsAsync<ArgumentException>(()=>new LocalOperatorBootstrap(f.Database).InitializeAsync(Path.Combine(Environment.CurrentDirectory,"setup.txt")));
 });
 [Fact] public async Task ExistingOutputCannotBeOverwrittenAndProvisioningRollsBack()=>await IdentityFixture.Run(async f=>
 {
  var output=Output();Directory.CreateDirectory(Path.GetDirectoryName(output)!);await File.WriteAllTextAsync(output,"preserve");
  try{
   await Assert.ThrowsAsync<ArgumentException>(()=>new LocalOperatorBootstrap(f.Database).InitializeAsync(output));
   Assert.Equal("preserve",await File.ReadAllTextAsync(output));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM operator_owner"));
   await f.Sql("CREATE FUNCTION fail_local_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit failure'; END $$; CREATE TRIGGER fail_local_audit BEFORE INSERT ON account_audit FOR EACH ROW EXECUTE FUNCTION fail_local_audit()");
   File.Delete(output);
   await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>new LocalOperatorBootstrap(f.Database).InitializeAsync(output));
   Assert.False(File.Exists(output));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM operator_owner"));Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM users"));
  }finally{if(File.Exists(output))File.Delete(output);}
 });
 [Fact] public async Task OfflineCommandPrintsNoCredentialLink()=>await IdentityFixture.Run(async f=>
 {
  var output=Output();
  try{
   var start=new System.Diagnostics.ProcessStartInfo("C:/Users/sapro/.dotnet/dotnet.exe") {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=Path.GetTempPath()};
   start.ArgumentList.Add(typeof(DatabaseMigrations).Assembly.Location);start.ArgumentList.Add("--recover-local-operator");
   await new LocalOperatorBootstrap(f.Database).InitializeAsync(output+".initial");
   var connection=new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")!){SearchPath=new Npgsql.NpgsqlConnectionStringBuilder(f.Database.ConnectionString).SearchPath};
   start.Environment["ConnectionStrings__Knowledge"]=connection.ConnectionString;start.Environment["BRIE_OPERATOR_OUTPUT_FILE"]=output;
   using var process=System.Diagnostics.Process.Start(start)!;
   var stdoutTask=process.StandardOutput.ReadToEndAsync();var stderrTask=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
   var stdout=await stdoutTask;var stderr=await stderrTask;
   Assert.Equal(0,process.ExitCode);
   var content=(await File.ReadAllTextAsync(output)).Trim();Assert.StartsWith("http://localhost:5174/setup-account#token=",content);
   var raw=content.Split('=')[1];
   Assert.DoesNotContain(raw,stdout+stderr);Assert.DoesNotContain("#token=",stdout+stderr);Assert.DoesNotContain("https://localhost:5174",stdout+stderr);
  }finally{if(File.Exists(output))File.Delete(output);if(File.Exists(output+".initial"))File.Delete(output+".initial");}
 });
}
