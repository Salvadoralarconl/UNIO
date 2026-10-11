using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;

namespace Clary.Api;

/// <summary>Explicit deployment operation. Normal application startup never invokes migrations.</summary>
public static class DatabaseMigrations
{
    public static Task ApplyAsync(NpgsqlDataSource database, CancellationToken ct) =>
        ApplyAsync(database, Path.Combine(AppContext.BaseDirectory,"database","migrations"), ct);

    public static async Task ApplyAsync(NpgsqlDataSource database, string directory, CancellationToken ct)
    {
        var scripts=Directory.GetFiles(directory,"*.sql").Select(path=>
        {
            var name=Path.GetFileName(path);
            var match=Regex.Match(name,@"^(\d+)-.+\.sql$");
            if(!match.Success || !int.TryParse(match.Groups[1].Value,out var version) || version<2)
                throw new InvalidOperationException($"Invalid migration filename: {name}");
            return (Path:path,Name:name,Version:version);
        }).OrderBy(x=>x.Version).ToArray();
        if(scripts.GroupBy(x=>x.Version).Any(g=>g.Count()!=1))throw new InvalidOperationException("Duplicate migration versions.");
        await using var connection=await database.OpenConnectionAsync(ct);
        // Include schema in the lock key so isolated test/tenant bootstrap schemas do not interfere.
        await Execute("SELECT pg_advisory_lock(hashtextextended(current_database()||':'||current_schema()||':clary_migrations',0))",null,ct);
        try
        {
            // Legacy replaced_by had no FK. Preserve and report dangling pointers before any schema write.
            await using(var exists=new NpgsqlCommand("SELECT to_regclass(current_schema()||'.documents') IS NOT NULL",connection))
            if((bool)(await exists.ExecuteScalarAsync(ct))!)
            {
                await using var check=new NpgsqlCommand("SELECT count(*) FROM documents d LEFT JOIN documents target ON target.id=d.replaced_by WHERE d.replaced_by IS NOT NULL AND target.id IS NULL",connection);
                var dangling=(long)(await check.ExecuteScalarAsync(ct))!;
                if(dangling>0)throw new InvalidOperationException($"Preservation preflight: {dangling} dangling document replacement link(s). Resolve explicitly from a reviewed backup/repair proposal before migration; no links were rewritten.");
            }
            await Execute("CREATE TABLE IF NOT EXISTS schema_migrations(version integer PRIMARY KEY,name text NOT NULL,checksum text NOT NULL,applied_at timestamptz NOT NULL DEFAULT now())",null,ct);
            foreach(var script in scripts)
            {
                var bytes=await File.ReadAllBytesAsync(script.Path,ct);
                var checksum=Convert.ToHexString(SHA256.HashData(bytes));
                await using var lookup=new NpgsqlCommand("SELECT checksum FROM schema_migrations WHERE version=@version",connection);
                lookup.Parameters.AddWithValue("version",script.Version);
                var previous=await lookup.ExecuteScalarAsync(ct);
                if(previous is string recorded)
                {
                    if(recorded!=checksum)throw new InvalidOperationException($"Migration checksum mismatch: {script.Name}");
                    continue;
                }
                await using var transaction=await connection.BeginTransactionAsync(ct);
                await Execute(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'),transaction,ct);
                await using var record=new NpgsqlCommand("INSERT INTO schema_migrations(version,name,checksum) VALUES(@version,@name,@checksum)",connection,transaction);
                record.Parameters.AddWithValue("version",script.Version);record.Parameters.AddWithValue("name",script.Name);record.Parameters.AddWithValue("checksum",checksum);
                await record.ExecuteNonQueryAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        finally
        {
            // Session locks must also be released when cancellation aborts a migration.
            await Execute("SELECT pg_advisory_unlock(hashtextextended(current_database()||':'||current_schema()||':clary_migrations',0))",null,CancellationToken.None);
        }
        async Task Execute(string sql,NpgsqlTransaction? transaction,CancellationToken token)
        {
            await using var command=new NpgsqlCommand(sql,connection,transaction);
            await command.ExecuteNonQueryAsync(token);
        }
    }
}
