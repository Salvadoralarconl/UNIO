using Clary.Api.Identity;
using Clary.Api;
using Npgsql;
using Xunit;

public class RetrievalIntegrationTests
{
    [Fact]
    public async Task ApprovedRetrievalRevocationAndProfileMismatchUseRealPgvector()
    {
        var connection = Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION") ?? throw new InvalidOperationException("Set CLARY_TEST_CONNECTION to a local PostgreSQL test connection. This check requires real pgvector.");
        var schema = "clary_test_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(connection);
        await using (var create = admin.CreateCommand($"CREATE SCHEMA {schema}")) await create.ExecuteNonQueryAsync();
        try {
            var builder = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema + ",public" };
            await using var db = NpgsqlDataSource.Create(builder.ConnectionString);
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql"));
            await using (var init = db.CreateCommand(sql)) await init.ExecuteNonQueryAsync();await FixtureCompany.MigrateAsync(db);
            var approved=Guid.NewGuid();var draft=Guid.NewGuid();var passageId=Guid.NewGuid();var draftPassage=Guid.NewGuid();
            await using (var insert=db.CreateCommand("""
                INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,provider,model,dimensions,chunking_version)
                VALUES(@a,'VPN','1','approved','ready','vpn.md','a','VPN evidence','Test','fixture',2,'test'),
                (@d,'Draft','1','draft','ready','draft.md','b','Bad advice','Test','fixture',2,'test');
                INSERT INTO passages(id,revision_id,text,location,start_offset,embedding) VALUES(@p,@a,'VPN evidence','section',0,'[1,0]'),(@dp,@d,'Bad advice','section',0,'[1,0]');
                """)) {insert.Parameters.AddWithValue("a",approved);insert.Parameters.AddWithValue("d",draft);insert.Parameters.AddWithValue("p",passageId);insert.Parameters.AddWithValue("dp",draftPassage);await insert.ExecuteNonQueryAsync();}
            var repository=new KnowledgeRepository(db,.3);var profile=new EmbeddingProfile("Test","fixture",2,"test");
            var result=await repository.SearchAsync(FixtureCompany.Context,[1,0],profile,5,default);
            Assert.Single(result);Assert.Equal(passageId,result[0].Id);
            var neighborId=Guid.NewGuid();
            await using(var neighbor=db.CreateCommand("INSERT INTO passages(id,revision_id,text,location,start_offset,embedding) VALUES(@id,@rev,'Procedure continuation','next section',1100,'[0,1]')")) {
                neighbor.Parameters.AddWithValue("id",neighborId);neighbor.Parameters.AddWithValue("rev",approved);await neighbor.ExecuteNonQueryAsync();
            }
            var expanded=await repository.SearchAsync(FixtureCompany.Context,[1,0],profile,5,default);
            Assert.Contains(expanded,p=>p.Id==neighborId); // Below similarity threshold, but adjacent to an approved hit.
            Assert.DoesNotContain(expanded,p=>p.Id==draftPassage);
            Assert.Null(await repository.GetApprovedPassageAsync(FixtureCompany.Context,draftPassage,default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SearchAsync(FixtureCompany.Context,[1,0],profile with {Model="changed"},5,default));
            var loader=new SampleLoader(db,new FixtureEmbeddings(profile));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>loader.IndexAsync(FixtureCompany.Context,approved,"Changed revision text",default));
            Assert.NotNull(await repository.GetApprovedPassageAsync(FixtureCompany.Context,passageId,default));
            var brokenLoader=new SampleLoader(db,new FixtureEmbeddings(profile,true));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>brokenLoader.IndexAsync(FixtureCompany.Context,approved,"VPN evidence",default));
            Assert.Empty(await repository.SearchAsync(FixtureCompany.Context,[1,0],profile,5,default));
            // Multiple close matches: rank documents first so one long document cannot monopolize context.
            var rankedIds=Enumerable.Range(0,4).Select(_=>Guid.NewGuid()).ToArray();
            for(var i=0;i<rankedIds.Length;i++) {
                await using(var document=db.CreateCommand("INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,provider,model,dimensions,chunking_version) VALUES(@id,'Ranked','1','approved','ready','test.md','test','text','Test','fixture',2,'test')")) {
                    document.Parameters.AddWithValue("id",rankedIds[i]);await document.ExecuteNonQueryAsync();
                }
                for(var j=0;j<4;j++) {
                    await using var chunk=db.CreateCommand("INSERT INTO passages(id,revision_id,text,location,start_offset,embedding) VALUES(@id,@rev,'text','test',0,@vector::vector)");
                    chunk.Parameters.AddWithValue("id",Guid.NewGuid());chunk.Parameters.AddWithValue("rev",rankedIds[i]);chunk.Parameters.AddWithValue("vector",KnowledgeRepository.Vector([1,i*.15f]));await chunk.ExecuteNonQueryAsync();
                }
            }
            var selected=await repository.SearchAsync(FixtureCompany.Context,[1,0],profile,5,default);
            Assert.Equal(3,selected.Select(p=>p.RevisionId).Distinct().Count());
            Assert.All(selected.GroupBy(p=>p.RevisionId),group=>Assert.InRange(group.Count(),1,2));
            Assert.DoesNotContain(selected,p=>p.RevisionId==rankedIds[3]);
            await using(var cleanup=db.CreateCommand("DELETE FROM passages WHERE revision_id=ANY(@ids); DELETE FROM documents WHERE id=ANY(@ids)")) {
                cleanup.Parameters.AddWithValue("ids",rankedIds);await cleanup.ExecuteNonQueryAsync();
            }
            await using(var failed=db.CreateCommand("SELECT indexing FROM documents WHERE id=@id")) {failed.Parameters.AddWithValue("id",approved);Assert.Equal("failed",await failed.ExecuteScalarAsync());}
            await loader.IndexAsync(FixtureCompany.Context,approved,"VPN evidence",default);
            // Reconnect through a fresh data source: published vectors must persist beyond a connection lifetime.
            await using(var fresh=NpgsqlDataSource.Create(builder.ConnectionString)) {
                var persisted=await new KnowledgeRepository(fresh,.3).SearchAsync(FixtureCompany.Context,[1,0],profile,5,default);
                Assert.Single(persisted);passageId=persisted[0].Id;
            }
            await using(var revoke=db.CreateCommand("UPDATE documents SET approval='revoked' WHERE id=@id")){revoke.Parameters.AddWithValue("id",approved);await revoke.ExecuteNonQueryAsync();}
            Assert.Null(await repository.GetApprovedPassageAsync(FixtureCompany.Context,passageId,default));
            Assert.Empty(await repository.SearchAsync(FixtureCompany.Context,[1,0],profile,5,default));
        } finally { await using var drop=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await drop.ExecuteNonQueryAsync(); }
    }
    [Fact] public async Task RevokedSampleDoesNotBlockReindexingOtherApprovedSamples()
    {
        var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("CLARY_TEST_CONNECTION is required.");
        var schema="clary_test_"+Guid.NewGuid().ToString("N");
        await using var admin=NpgsqlDataSource.Create(connection);
        await using(var create=admin.CreateCommand($"CREATE SCHEMA {schema}"))await create.ExecuteNonQueryAsync();
        try {
            var builder=new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"};
            await using var db=NpgsqlDataSource.Create(builder.ConnectionString);
            var initSql=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql"));
            await using(var init=db.CreateCommand(initSql))await init.ExecuteNonQueryAsync();await FixtureCompany.MigrateAsync(db);
            await using(var insert=db.CreateCommand("INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text) VALUES('10000000-0000-0000-0000-000000000001','VPN','2.1','revoked','pending','vpn.md','old','old')"))await insert.ExecuteNonQueryAsync();
            await new SampleLoader(db,new FixtureEmbeddings(new("Test","fixture",2,"test"))).LoadAsync(FixtureCompany.Context,default);
            var documents=await new KnowledgeRepository(db,.3).DocumentsAsync(FixtureCompany.Context,default);
            Assert.Equal("revoked",documents.Single(d=>d.Id==Guid.Parse("10000000-0000-0000-0000-000000000001")).Approval);
            Assert.Equal(11,documents.Count(d=>d.Approval=="approved"&&d.Indexing=="ready"));
        } finally {await using var drop=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await drop.ExecuteNonQueryAsync();}
    }
    private sealed class FixtureEmbeddings(EmbeddingProfile profile,bool fail=false):IEmbeddingProvider {
        public EmbeddingProfile Profile=>profile;
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct) {
            if(fail)throw new InvalidOperationException("Simulated batch failure");
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_=>new float[]{1,0}).ToArray());
        }
    }
}





