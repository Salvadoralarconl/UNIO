using Clary.Api.Identity;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Clary.Api;
public sealed record SampleDocument(Guid Id, string Title, string Version, string File, string Approval, string? ConflictGroup);
public sealed class SampleLoader(NpgsqlDataSource database, IEmbeddingProvider embeddings)
{
    private readonly SemaphoreSlim gate = new(1,1);
    public async Task LoadAsync(CompanyContext context,CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("Sample indexing is already running.");
        try {
            if(context.CompanyId!=Guid.Parse("00000000-0000-0000-0000-000000000001"))throw new ArgumentException("Samples belong to the explicit legacy company.");
            var folder = Path.Combine(AppContext.BaseDirectory, "samples");
            var samples = JsonSerializer.Deserialize<SampleDocument[]>(await File.ReadAllTextAsync(Path.Combine(folder,"manifest.json"),ct), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            foreach (var sample in samples)
            {
                var text = await File.ReadAllTextAsync(Path.Combine(folder, sample.File), ct);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                await using (var cmd = CompanyCommand.Create(context,"""
                    INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,conflict_group)
                    VALUES(@company,@id,@title,@version,@approval,'pending',@file,@hash,@text,@conflict)
                    ON CONFLICT(id) DO NOTHING
                    """,database)) {
                    cmd.Parameters.AddWithValue("id",sample.Id);cmd.Parameters.AddWithValue("title",sample.Title);cmd.Parameters.AddWithValue("version",sample.Version);cmd.Parameters.AddWithValue("approval",sample.Approval);cmd.Parameters.AddWithValue("file",sample.File);cmd.Parameters.AddWithValue("hash",hash);cmd.Parameters.AddWithValue("text",text);cmd.Parameters.AddWithValue("conflict",(object?)sample.ConflictGroup??DBNull.Value);
                    await AccountPermissions.ExecuteAsync(database,cmd,context,CompanyCapability.Index,ct);
                }
                await using(var approval=CompanyCommand.Create(context,"SELECT approval FROM documents WHERE company_id=@company AND id=@id",database))
                {
                    approval.Parameters.AddWithValue("id",sample.Id);
                    if(await approval.ExecuteScalarAsync(ct) as string != "approved")continue;
                }
                await IndexAsync(context,sample.Id,text,ct);
            }
        } finally { gate.Release(); }
    }
    public async Task IndexAsync(CompanyContext context,Guid revisionId, string text, CancellationToken ct)
    {
        var profile = embeddings.Profile;
        await using(var check=CompanyCommand.Create(context,"SELECT canonical_text FROM documents WHERE company_id=@company AND id=@id AND approval='approved'",database))
        {
            check.Parameters.AddWithValue("id",revisionId);
            var canonical=await check.ExecuteScalarAsync(ct) as string;
            if(canonical is null || !string.Equals(canonical,text,StringComparison.Ordinal))
                throw new InvalidOperationException("Indexing requires an approved immutable revision. Changed content needs a new revision ID.");
        }
        await using (var mark = CompanyCommand.Create(context,"UPDATE documents SET indexing='indexing',error=NULL WHERE company_id=@company AND id=@id",database)) { mark.Parameters.AddWithValue("id",revisionId); await AccountPermissions.ExecuteAsync(database,mark,context,CompanyCapability.Index,ct); }
        try {
            var chunks = Chunker.Split(text);
            if (chunks.Count == 0) throw new InvalidOperationException("No text to index.");
            var vectors = await embeddings.EmbedAsync(chunks.Select(c=>c.Text).ToArray(),ct);
            if (vectors.Count != chunks.Count || vectors.Any(v=>v.Length!=profile.Dimensions)) throw new InvalidOperationException("Embedding batch is incompatible.");
            await using var conn = await database.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await AccountPermissions.RevalidateAsync(conn,tx,context,CompanyCapability.Index,ct);
            await using (var clear = CompanyCommand.Create(context,"DELETE FROM passages WHERE company_id=@company AND revision_id=@id",conn,tx)) { clear.Parameters.AddWithValue("id",revisionId);await clear.ExecuteNonQueryAsync(ct); }
            for(var i=0;i<chunks.Count;i++) {
                await using var insert = CompanyCommand.Create(context,"INSERT INTO passages(company_id,id,revision_id,text,location,start_offset,embedding) VALUES(@company,@id,@rev,@text,@loc,@offset,@vector::vector)",conn,tx);
                insert.Parameters.AddWithValue("id",Guid.NewGuid());insert.Parameters.AddWithValue("rev",revisionId);insert.Parameters.AddWithValue("text",chunks[i].Text);insert.Parameters.AddWithValue("loc",$"Extracted text · characters {chunks[i].Start}–{chunks[i].Start+chunks[i].Text.Length}");insert.Parameters.AddWithValue("offset",chunks[i].Start);insert.Parameters.AddWithValue("vector",KnowledgeRepository.Vector(vectors[i]));await insert.ExecuteNonQueryAsync(ct);
            }
            await using var ready = CompanyCommand.Create(context,"UPDATE documents SET indexing='ready',supported_models=@models,provider=@p,model=@m,dimensions=@d,chunking_version=@c,error=NULL WHERE company_id=@company AND id=@id",conn,tx);
            KnowledgeRepository.ProfileParameters(ready,profile);ready.Parameters.AddWithValue("models",EpsonModels.Extract(text));ready.Parameters.AddWithValue("id",revisionId);await ready.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        } catch {
            await using var fail = CompanyCommand.Create(context,"UPDATE documents SET indexing='failed',error='Indexing failed. Check backend configuration and retry.' WHERE company_id=@company AND id=@id",database);fail.Parameters.AddWithValue("id",revisionId);try{await AccountPermissions.ExecuteAsync(database,fail,context,CompanyCapability.Index,CancellationToken.None);}catch(CompanyDenied){}throw;
        }
    }
}
