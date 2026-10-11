using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace Clary.Api;

public static class HostedDeployment
{
    public static void AddPersistentKeys(this WebApplicationBuilder builder)
    {
        var path = builder.Configuration["Hosting:DataProtectionPath"];
        if (string.IsNullOrWhiteSpace(path)) return; // Preserve existing local startup.
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Use an absolute private Data Protection directory.");
        Directory.CreateDirectory(path);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(path)).SetApplicationName("UNIO");
    }

    public static void MapHostedReadiness(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Hosting:ReadinessEnabled")) return;
        // Private Docker endpoint. Nginx explicitly blocks /internal/ and the API
        // has no published port. This check never sends text or requests to AI.
        app.MapGet("/internal/ready", async (NpgsqlDataSource database, CancellationToken ct) =>
        {
            try
            {
                var expected = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"database","migrations"),"*.sql").Length;
                await using var command = database.CreateCommand("SELECT count(*) FROM schema_migrations");
                command.CommandTimeout = 3;
                var applied = (long)(await command.ExecuteScalarAsync(ct))!;
                return applied == expected ? Results.Ok(new { status="ready", database="ready", ai="not-probed" }) : Results.StatusCode(503);
            }
            catch (Exception e) when (e is NpgsqlException or IOException or OperationCanceledException)
            {
                return Results.StatusCode(503);
            }
        });
    }
}
