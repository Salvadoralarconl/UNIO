using Clary.Api;
using Npgsql;
using Xunit;
public class DatabaseStartupTests
{
 [Fact] public async Task FreshBootstrapRefusesExistingDataWithoutChangingIt()
 {
  var cs=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("CLARY_TEST_CONNECTION required");
  var schema="startup_"+Guid.NewGuid().ToString("N");
  await using var admin=NpgsqlDataSource.Create(cs);
  await using(var q=admin.CreateCommand($"CREATE SCHEMA {schema}"))await q.ExecuteNonQueryAsync();
  try {
   await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs){SearchPath=schema+",public"}.ConnectionString);
   await using(var q=db.CreateCommand("CREATE TABLE documents(id int PRIMARY KEY); INSERT INTO documents VALUES(42)"))await q.ExecuteNonQueryAsync();
   await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseStartup.FreshBootstrapAsync(db,CancellationToken.None));
   await using var check=db.CreateCommand("SELECT id FROM documents");Assert.Equal(42,await check.ExecuteScalarAsync());
  } finally {await using var q=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await q.ExecuteNonQueryAsync();}
 }
 [Fact] public async Task FreshBootstrapCreatesCurrentSchemaAndCannotBeRepeated()
 {
  var cs=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("CLARY_TEST_CONNECTION required");
  var schema="startup_"+Guid.NewGuid().ToString("N");await using var admin=NpgsqlDataSource.Create(cs);
  await using(var q=admin.CreateCommand($"CREATE SCHEMA {schema}"))await q.ExecuteNonQueryAsync();
  try {
   await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs){SearchPath=schema+",public"}.ConnectionString);
   await DatabaseStartup.FreshBootstrapAsync(db,CancellationToken.None);
   await using(var q=db.CreateCommand("SELECT count(*) FROM schema_migrations"))Assert.Equal(8L,await q.ExecuteScalarAsync());
   await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseStartup.FreshBootstrapAsync(db,CancellationToken.None));
  }finally {await using var q=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await q.ExecuteNonQueryAsync();}
 }
}
