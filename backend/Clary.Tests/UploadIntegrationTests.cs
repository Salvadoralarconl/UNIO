using Clary.Api.Identity;
using Clary.Api;
using Npgsql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using System.Text;
using Xunit;
public class UploadIntegrationTests
{
    [Fact] public async Task UploadedDraftBecomesSearchableOnlyAfterApprovalAndIndexing() {
        var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("Set CLARY_TEST_CONNECTION.");
        var schema="upload_test_"+Guid.NewGuid().ToString("N");
        var folder=Path.Combine(Path.GetTempPath(),"clary-upload-test-"+Guid.NewGuid().ToString("N"));
        await using var admin=NpgsqlDataSource.Create(connection);
        await using(var create=admin.CreateCommand($"CREATE SCHEMA {schema}"))await create.ExecuteNonQueryAsync();
        try {
            await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
            var sql=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql"));
            await using(var init=db.CreateCommand(sql))await init.ExecuteNonQueryAsync();await FixtureCompany.MigrateAsync(db);
            var embedding=new Embeddings();var uploads=new UploadService(db,new SampleLoader(db,embedding),new EnvironmentStub(folder));
            const string text="For Epson TM-T88V connected by USB, inspect Device Manager when the printer is absent from Windows Settings.";
            using var bytes=new MemoryStream(Encoding.UTF8.GetBytes(text));
            var doc=await uploads.UploadAsync(FixtureCompany.Context,new FormFile(bytes,0,bytes.Length,"file","guide.md"),"Uploaded guide","2.0",default);
            Assert.True(doc.IsUploaded);Assert.Equal("draft",doc.Approval);Assert.Equal(text,await uploads.TextAsync(FixtureCompany.Context,doc.Id,default));
            var repo=new KnowledgeRepository(db,.3);
            Assert.Empty(await repo.SearchAsync(FixtureCompany.Context,[1,0],embedding.Profile,5,default));Assert.Equal(0,embedding.Calls);
            await uploads.ApproveAndIndexAsync(FixtureCompany.Context,doc.Id,default);
            var results=await repo.SearchAsync(FixtureCompany.Context,[1,0],embedding.Profile,5,default);var passage=Assert.Single(results);
            Assert.Equal(doc.Id,passage.RevisionId);Assert.Contains("TM-T88V",passage.SupportedModels!);
            Assert.Single(EpsonEvidence.Select("TM-T88V USB",results).Passages);
            var saved=Assert.Single(await repo.DocumentsAsync(FixtureCompany.Context,default));Assert.Equal("approved",saved.Approval);Assert.Equal("ready",saved.Indexing);
            Assert.Single(Directory.GetFiles(Path.Combine(folder,"uploads")));
            var content="""[{"type":"p","children":[{"type":"text","text":"Revised USB guide"}]}]""";
            var revision=await new LibraryOperations(db).EditAsync(FixtureCompany.Context,doc.Id,new("Revised guide","",null,content),default);
            Assert.Null(await repo.GetApprovedPassageAsync(FixtureCompany.Context,passage.Id,default));
            await Assert.ThrowsAsync<CompanyResourceMissing>(()=>uploads.ApproveAndIndexAsync(FixtureCompany.Context,doc.Id,default));
            await using(var old=db.CreateCommand("SELECT approval FROM documents WHERE id=@id")){old.Parameters.AddWithValue("id",doc.Id);Assert.Equal("revoked",await old.ExecuteScalarAsync());}
            await uploads.ApproveAndIndexAsync(FixtureCompany.Context,revision.Id,default);
            Assert.Equal(revision.Id,Assert.Single(await repo.SearchAsync(FixtureCompany.Context,[1,0],embedding.Profile,5,default)).RevisionId);

        }finally {
            await using var drop=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await drop.ExecuteNonQueryAsync();
            if(Directory.Exists(folder)&&Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(folder,true);
        }
    }
    private sealed class Embeddings:IEmbeddingProvider {
        public int Calls;public EmbeddingProfile Profile=>new("Test","upload",2,"test");
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct){Calls++;return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_=>new float[]{1,0}).ToArray());}
    }
    private sealed class EnvironmentStub(string folder):IWebHostEnvironment {
        public string ApplicationName{get;set;}="Tests";public string EnvironmentName{get;set;}="Test";
        public string ContentRootPath{get;set;}=folder;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public string WebRootPath{get;set;}=folder;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}


