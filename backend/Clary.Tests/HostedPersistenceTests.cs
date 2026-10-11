using Clary.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class HostedPersistenceTests
{
    [Fact]
    public void HostedKeysDecryptAfterIndependentProviderRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"unio-key-test-"+Guid.NewGuid());
        try
        {
            string Protect(bool encrypt,string value)
            {
                var builder=WebApplication.CreateBuilder();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Hosting:DataProtectionPath",path}});
                builder.AddPersistentKeys();
                using var app=builder.Build();
                var protector=app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("hosting-cookie-regression");
                return encrypt?protector.Protect(value):protector.Unprotect(value);
            }
            var protectedValue=Protect(true,"synthetic-session");
            Assert.Equal("synthetic-session",Protect(false,protectedValue));
            Assert.NotEmpty(Directory.GetFiles(path,"key-*.xml"));
        }
        finally { if(Directory.Exists(path))Directory.Delete(path,true); }
    }

    [Fact]
    public async Task FreshHostedDatabaseAppliesAllMigrationsAndRefusesSecondBootstrap()
    {
        var cs=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("Dedicated test connection required.");
        var schema="hosted_bootstrap_"+Guid.NewGuid().ToString("N");
        await using var admin=Npgsql.NpgsqlDataSource.Create(cs);
        await using(var create=admin.CreateCommand($"CREATE SCHEMA {schema}"))await create.ExecuteNonQueryAsync();
        try
        {
            await using var db=Npgsql.NpgsqlDataSource.Create(new Npgsql.NpgsqlConnectionStringBuilder(cs){SearchPath=schema+",public"}.ConnectionString);
            await DatabaseStartup.FreshBootstrapAsync(db,default);
            var expected=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"database","migrations"),"*.sql").Length;
            await using var count=db.CreateCommand("SELECT count(*) FROM schema_migrations");
            Assert.Equal((long)expected,await count.ExecuteScalarAsync());
            await DatabaseMigrations.ApplyAsync(db,default); // idempotent upgrade check
            await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseStartup.FreshBootstrapAsync(db,default));
            Assert.Equal((long)expected,await count.ExecuteScalarAsync());
        }
        finally { await using var drop=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE"); await drop.ExecuteNonQueryAsync(); }
    }
}
