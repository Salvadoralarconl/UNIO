using Npgsql;
namespace Clary.Api;
/// <summary>Offline fresh-database bootstrap. Never run legacy SQL on an existing schema.</summary>
public static class DatabaseStartup
{
 public static async Task FreshBootstrapAsync(NpgsqlDataSource database,CancellationToken ct)
 {
  await using var connection=await database.OpenConnectionAsync(ct);
  await using var transaction=await connection.BeginTransactionAsync(ct);
  await using(var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(current_database()||':'||current_schema()||':clary_fresh_bootstrap',0))",connection,transaction))await gate.ExecuteNonQueryAsync(ct);
  await using(var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace=current_schema()::regnamespace AND relkind IN ('r','p','v','m','f','S'))",connection,transaction))
   if((bool)(await check.ExecuteScalarAsync(ct))!)throw new InvalidOperationException("Fresh bootstrap requires an empty schema. Use --migrate for an existing database.");
  await using(var initial=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql"),ct),connection,transaction))await initial.ExecuteNonQueryAsync(ct);
  await transaction.CommitAsync(ct);
  await DatabaseMigrations.ApplyAsync(database,ct);
 }
}
