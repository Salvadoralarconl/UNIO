using Clary.Api;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Xunit;
internal sealed class KnowledgeIsolationFixture:IAsyncDisposable {
 internal NpgsqlDataSource Db=null!;NpgsqlDataSource admin=null!;string schema="knowledge_isolation_"+Guid.NewGuid().ToString("N");
 internal CompanyContext A=FixtureCompany.Context,B=FixtureCompany.Context with{CompanyId=Guid.NewGuid(),UserId=Guid.NewGuid()};
 internal string Folder=Path.Combine(Path.GetTempPath(),"clary-isolation-"+Guid.NewGuid().ToString("N"));
 internal static async Task Run(Func<KnowledgeIsolationFixture,Task> test){await using var f=new KnowledgeIsolationFixture();var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")!;f.admin=NpgsqlDataSource.Create(connection);await using(var q=f.admin.CreateCommand($"CREATE SCHEMA {f.schema}"))await q.ExecuteNonQueryAsync();f.Db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=f.schema+",public"}.ConnectionString);await f.Sql(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql")));await FixtureCompany.MigrateAsync(f.Db);await f.Sql($"INSERT INTO companies(id,name) VALUES('{f.B.CompanyId}','Foreign company')");await FixtureCompany.AddActorAsync(f.Db,f.B);await test(f);}
 internal async Task Sql(string sql){await using var q=Db.CreateCommand(sql);await q.ExecuteNonQueryAsync();}
 internal UploadService Uploads=>new(Db,new SampleLoader(Db,new TestEmbedding()),new TestEnvironment(Folder));
 internal CompanyFiles Files=>new(Db,new TestEnvironment(Folder));
 public async ValueTask DisposeAsync(){if(Db is not null)await Db.DisposeAsync();if(admin is not null){await using var q=admin.CreateCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE");await q.ExecuteNonQueryAsync();await admin.DisposeAsync();}if(Directory.Exists(Folder)&&Path.GetFullPath(Folder).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(Folder,true);}
 internal sealed class TestEmbedding:IEmbeddingProvider {public EmbeddingProfile Profile=>new("Test","fixture",2,"test");public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct)=>Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_=>new float[]{1,0}).ToArray());}
 internal sealed class TestEnvironment(string folder):IWebHostEnvironment {public string ApplicationName{get;set;}="Test";public string EnvironmentName{get;set;}="Test";public string ContentRootPath{get;set;}=folder;public string WebRootPath{get;set;}=folder;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
}
