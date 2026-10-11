using Clary.Api;
using Npgsql;
using Xunit;

public class AccountMigrationTests
{
 internal static string Migration => Path.Combine(AppContext.BaseDirectory,"database","migrations","006-account-management.sql");
 internal static async Task Run(Func<NpgsqlDataSource,Task> action)
 {
  var cs=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("CLARY_TEST_CONNECTION required");
  var schema="accounts_test_"+Guid.NewGuid().ToString("N");await using var admin=NpgsqlDataSource.Create(cs);await Sql(admin,$"CREATE SCHEMA {schema}");
  try {await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs){SearchPath=schema+",public"}.ConnectionString);
   await Sql(db,await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql")));
   foreach(var path in Directory.GetFiles(Path.GetDirectoryName(Migration)!,"*.sql").Order().Where(p=>string.CompareOrdinal(Path.GetFileName(p),"006-")<0))await Sql(db,await File.ReadAllTextAsync(path));
   await action(db);
  } finally {await Sql(admin,$"DROP SCHEMA {schema} CASCADE");}
 }
 internal static async Task Sql(NpgsqlDataSource db,string sql){await using var q=db.CreateCommand(sql);await q.ExecuteNonQueryAsync();}
 internal static async Task<object?> Scalar(NpgsqlDataSource db,string sql){await using var q=db.CreateCommand(sql);return await q.ExecuteScalarAsync();}
 internal static async Task Apply(NpgsqlDataSource db){Assert.True(File.Exists(Migration),"Account migration must exist");await Sql(db,"BEGIN;"+await File.ReadAllTextAsync(Migration)+"COMMIT;");}
 [Fact] public async Task LegacyRolesPreserveIdentityAndAssignedVisibility()=>await Run(async db=>{
  await Sql(db,"INSERT INTO users(id,issuer,identity_subject,display_name) SELECT gen_random_uuid(),'test',r,r FROM unnest(ARRAY['Admin','Technician','Manager','Employee']) r; INSERT INTO memberships(company_id,user_id,role) SELECT c.id,u.id,u.identity_subject FROM companies c CROSS JOIN users u;");
  var before=await Scalar(db,"SELECT jsonb_agg(to_jsonb(u) ORDER BY id)::text FROM users u");await Apply(db);
  Assert.Equal(before,await Scalar(db,"SELECT jsonb_agg(to_jsonb(u) ORDER BY id)::text FROM users u"));
  Assert.Equal("Administrator:Company,Editor:Company,Viewer:Company,Viewer:AssignedOnly",await Scalar(db,"SELECT string_agg(m.role||':'||m.visibility_scope,',' ORDER BY CASE u.identity_subject WHEN 'Admin' THEN 1 WHEN 'Technician' THEN 2 WHEN 'Manager' THEN 3 ELSE 4 END) FROM memberships m JOIN users u ON u.id=m.user_id"));
  Assert.Equal("Unlimited",await Scalar(db,"SELECT plan_kind FROM companies"));
 });
 [Theory][InlineData(true)][InlineData(false)] public async Task AmbiguousLegacyAccountsAbort(bool multipleCompanies)=>await Run(async db=>{
  await Sql(db,"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('10000000-0000-0000-0000-000000000001','test','one','One'); INSERT INTO memberships(company_id,user_id,role) SELECT id,'10000000-0000-0000-0000-000000000001','Admin' FROM companies;");
  if(multipleCompanies)await Sql(db,"INSERT INTO companies(id,name) VALUES('20000000-0000-0000-0000-000000000001','Other'); INSERT INTO memberships(company_id,user_id,role) VALUES('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','Employee');");
  else await Sql(db,"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('10000000-0000-0000-0000-000000000002','test','two','Two'); INSERT INTO memberships(company_id,user_id,role) SELECT id,'10000000-0000-0000-0000-000000000002','Admin' FROM companies;");
  var ex=await Assert.ThrowsAsync<PostgresException>(()=>Apply(db));Assert.Contains(multipleCompanies?"multiple company":"multiple enabled Administrators",ex.MessageText);
 });

 [Theory][InlineData("Unknown",null)][InlineData("Custom",100)][InlineData("Ten",null)][InlineData("Unlimited",10)]
 public async Task UnsupportedPlansAreRejected(string kind,int? limit)=>await Run(async db=>{
  await Apply(db);var ex=await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,$"UPDATE companies SET plan_kind='{kind}',seat_limit={limit?.ToString()??"NULL"}"));Assert.Equal(PostgresErrorCodes.CheckViolation,ex.SqlState);
 });
 [Fact] public async Task ConcurrentAdministratorActivationAllowsExactlyOne()=>await Run(async db=>{
  await Apply(db);await Sql(db,"INSERT INTO users(id,issuer,identity_subject,display_name) SELECT gen_random_uuid(),'test',r,r FROM unnest(ARRAY['one','two']) r; INSERT INTO memberships(company_id,user_id,role,active) SELECT c.id,u.id,'Administrator',false FROM companies c CROSS JOIN users u;");
  async Task<bool> Activate(string sub){try{await Sql(db,$"UPDATE memberships SET active=true WHERE user_id=(SELECT id FROM users WHERE identity_subject='{sub}')");return true;}catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation){return false;}}
  Assert.Single(await Task.WhenAll(Activate("one"),Activate("two")),x=>x);
  Assert.Equal(1L,await Scalar(db,"SELECT count(*) FROM memberships WHERE active AND role='Administrator'"));
 });
 [Fact] public async Task AuditAndOwnerCannotBeTruncated()=>await Run(async db=>{
  await Apply(db);await new Clary.Api.Accounts.OperatorBootstrap(db).InitializeAsync("https://issuer.example","owner");
  await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,"TRUNCATE account_audit"));
  await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,"TRUNCATE operator_owner"));
 });
 [Theory][InlineData("https://user:pass@issuer.example")][InlineData("https://issuer.example?query")][InlineData("https://issuer.example#fragment")]
 public async Task DatabaseRejectsMalformedOwnerIssuer(string issuer)=>await Run(async db=>{
  await Apply(db);await Sql(db,$"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('10000000-0000-0000-0000-000000000001','{issuer}','owner','Owner')");
  var ex=await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,$"INSERT INTO operator_owner(user_id,issuer,subject) VALUES('10000000-0000-0000-0000-000000000001','{issuer}','owner')"));Assert.Equal(PostgresErrorCodes.CheckViolation,ex.SqlState);
 });
}
