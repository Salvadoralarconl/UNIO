using Clary.Api.Identity;
using System.Globalization;
using Npgsql;

namespace Clary.Api;
public sealed class KnowledgeRepository(NpgsqlDataSource database, double minSimilarity, int maxDocuments=3, int passagesPerDocument=2, double scoreWindow=.2)
{
    public IKnowledgeRepository ForCompany(CompanyContext context,bool broader=false)=>new Bound(broader?new(database,minSimilarity,int.MaxValue,Math.Max(passagesPerDocument,8),Math.Max(scoreWindow,.35)):this,context);
    private sealed class Bound(KnowledgeRepository scoped,CompanyContext context):IKnowledgeRepository {
        public Task<IReadOnlyList<Passage>> SearchAsync(float[] query,EmbeddingProfile profile,int limit,CancellationToken ct)=>scoped.SearchAsync(context,query,profile,limit,ct);
        public Task<IReadOnlyList<Passage>> SearchKeywordsAsync(string question,EmbeddingProfile profile,int limit,CancellationToken ct)=>scoped.SearchKeywordsAsync(context,question,profile,limit,ct);
        public Task<Passage?> GetApprovedPassageAsync(Guid id,CancellationToken ct)=>scoped.GetApprovedPassageAsync(context,id,ct);
        public Task<IReadOnlyList<DocumentInfo>> DocumentsAsync(CancellationToken ct)=>scoped.DocumentsAsync(context,ct);
    }
    public static string Vector(float[] values) => "[" + string.Join(",", values.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + "]";
    public async Task<IReadOnlyList<Passage>> SearchAsync(CompanyContext context,float[] query, EmbeddingProfile profile, int limit, CancellationToken ct)
    {
        await EnsureReadAsync(context,ct);
        if (query.Length != profile.Dimensions) throw new InvalidOperationException("Query embedding dimensions mismatch; reindex required.");
        await using var connection = await database.OpenConnectionAsync(ct);
        await using (var check = CompanyCommand.Create(context,"SELECT EXISTS(SELECT 1 FROM documents WHERE company_id=@company AND NOT archived AND approval='approved' AND indexing='ready' AND (provider<>@p OR model<>@m OR dimensions<>@d OR chunking_version<>@c))", connection))
        {
            ProfileParameters(check, profile);
            if ((bool)(await check.ExecuteScalarAsync(ct))!) throw new InvalidOperationException("Approved indexes use a different embedding profile. Reindex sample documents.");
        }
        // A materialized CTE filters profiles before vector comparison, including dimension changes.
        await EnsureReadAsync(context,ct);
        // A short wiki or uploaded guide is a bounded unit of evidence. Expanding it here keeps
        // numbered procedures intact without changing the embedding index/profile.
        // Citation reads use the same expansion and approval filters below.
        await using var cmd = CompanyCommand.Create(context,"""
            WITH eligible AS MATERIALIZED (
              SELECT p.id,p.revision_id,d.title,d.version,CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN 'Full document' ELSE p.location END AS location,CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN d.canonical_text ELSE p.text END AS text,d.conflict_group,d.supported_models,d.filename,p.embedding
              FROM passages p JOIN documents d ON d.id=p.revision_id
              WHERE d.company_id=@company AND p.company_id=@company AND NOT d.archived AND d.approval='approved' AND d.indexing='ready' AND d.provider=@p AND d.model=@m AND d.dimensions=@d AND d.chunking_version=@c
            ), scored AS MATERIALIZED (
              SELECT *,1-(embedding <=> @q::vector) AS score FROM eligible
            ), document_scores AS (
              SELECT revision_id,max(score) AS score FROM scored GROUP BY revision_id
            ), chosen_documents AS (
              SELECT revision_id FROM document_scores
              WHERE score>=@threshold AND score>=(SELECT max(score) FROM document_scores)-@window
              ORDER BY score DESC,revision_id LIMIT @documents
            ), ranked_passages AS (
              SELECT s.*,row_number() OVER(PARTITION BY s.revision_id ORDER BY s.score DESC,s.id) AS position
              FROM scored s JOIN chosen_documents d ON d.revision_id=s.revision_id
              WHERE s.score>=@threshold AND s.score>=(SELECT max(score) FROM document_scores)-@window
            ) SELECT id,revision_id,title,version,location,text,conflict_group,supported_models,filename FROM ranked_passages
              WHERE position<=@perdocument ORDER BY score DESC,id LIMIT @limit
            """, connection);
        ProfileParameters(cmd, profile);
        cmd.Parameters.AddWithValue("q", Vector(query)); cmd.Parameters.AddWithValue("threshold", minSimilarity); cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("documents",maxDocuments);cmd.Parameters.AddWithValue("perdocument",passagesPerDocument);cmd.Parameters.AddWithValue("window",scoreWindow);
        var result = new List<Passage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        await reader.DisposeAsync();
        // An adjacent chunk can contain the remainder of a procedure cut at the size boundary.
        if(result.Count>0) {
            await using var nearby=CompanyCommand.Create(context,"""
                WITH ordered AS (
                  SELECT p.*,row_number() OVER(ORDER BY p.start_offset,p.id) AS n
                  FROM passages p JOIN documents d ON d.id=p.revision_id
                  WHERE d.company_id=@company AND p.company_id=@company AND d.id=@revision AND NOT d.archived AND d.approval='approved' AND d.indexing='ready'
                    AND d.provider=@p AND d.model=@m AND d.dimensions=@d AND d.chunking_version=@c
                ) SELECT o.id,o.revision_id,d.title,d.version,CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN 'Full document' ELSE o.location END AS location,CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN d.canonical_text ELSE o.text END AS text,d.conflict_group,d.supported_models,d.filename
                FROM ordered o JOIN documents d ON d.id=o.revision_id
                WHERE d.company_id=@company AND abs(o.n-(SELECT n FROM ordered WHERE id=@anchor))=1
                  AND o.start_offset<>(SELECT start_offset FROM ordered WHERE id=@anchor) ORDER BY o.start_offset
                """,connection);
            ProfileParameters(nearby,profile);nearby.Parameters.AddWithValue("revision",result[0].RevisionId);nearby.Parameters.AddWithValue("anchor",result[0].Id);
            await using var neighbors=await nearby.ExecuteReaderAsync(ct);
            while(await neighbors.ReadAsync(ct)) {var p=Read(neighbors);if(result.All(r=>r.Id!=p.Id))result.Add(p);}
        }
        // Several matching chunks can expand to the same complete guide.
        return result.DistinctBy(p=>(p.RevisionId,p.Text)).ToList();
    }
    public async Task<IReadOnlyList<Passage>> SearchKeywordsAsync(CompanyContext context,string question,EmbeddingProfile profile,int limit,CancellationToken ct)
    {
        await EnsureReadAsync(context,ct);
        var keywords=KnowledgeKeywords.Query(question);
        if(keywords.Length==0)return [];
        // Each term is normalized from user/search text before joining; no raw tsquery syntax.
        keywords=string.Join(" | ",keywords.Split(' ',StringSplitOptions.RemoveEmptyEntries));
        await using var cmd=CompanyCommand.Create(context,"""
            WITH matching AS (
              SELECT d.*,ts_rank_cd(setweight(to_tsvector('english',d.title),'A')||to_tsvector('english',d.canonical_text),to_tsquery('english',@keywords)) AS score
              FROM documents d
              WHERE d.company_id=@company AND NOT d.archived AND d.approval='approved' AND d.indexing='ready'
                AND d.provider=@p AND d.model=@m AND d.dimensions=@d AND d.chunking_version=@c
                AND (to_tsvector('english',d.title)||to_tsvector('english',d.canonical_text)) @@ to_tsquery('english',@keywords)
            ) SELECT p.id,p.revision_id,d.title,d.version,
                CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN 'Full document' ELSE p.location END,
                CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN d.canonical_text ELSE p.text END,
                d.conflict_group,d.supported_models,d.filename
              FROM matching d JOIN LATERAL (
                SELECT p.* FROM passages p WHERE p.company_id=@company AND p.revision_id=d.id
                ORDER BY ts_rank_cd(to_tsvector('english',p.text),to_tsquery('english',@keywords)) DESC,p.start_offset,p.id LIMIT 1
              ) p ON true WHERE d.score>=(SELECT max(score) FROM matching)*.5
              ORDER BY d.score DESC,d.id LIMIT @limit
            """,database);
        ProfileParameters(cmd,profile);cmd.Parameters.AddWithValue("keywords",keywords);cmd.Parameters.AddWithValue("limit",Math.Clamp(limit,1,8));
        var result=new List<Passage>();await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(Read(reader));
        return result;
    }
    public async Task<Passage?> GetApprovedPassageAsync(CompanyContext context,Guid id, CancellationToken ct)
    {
        await EnsureReadAsync(context,ct);
        await using var cmd = CompanyCommand.Create(context,"""
            SELECT p.id,p.revision_id,d.title,d.version,CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN 'Full document' ELSE p.location END AS location,CASE WHEN (d.filename LIKE 'wiki/%' OR d.filename LIKE 'uploads/%') AND length(d.canonical_text) BETWEEN 1 AND 4000 THEN d.canonical_text ELSE p.text END AS text,d.conflict_group,d.supported_models,d.filename
            FROM passages p JOIN documents d ON d.id=p.revision_id
            WHERE d.company_id=@company AND p.company_id=@company AND p.id=@id AND NOT d.archived AND d.approval='approved' AND d.indexing='ready'
            """,database);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<IReadOnlyList<DocumentInfo>> DocumentsAsync(CompanyContext context,CancellationToken ct)
    {
        await EnsureReadAsync(context,ct);
        await using var cmd = CompanyCommand.Create(context,"SELECT id,title,version,approval,indexing,error,filename FROM documents WHERE company_id=@company AND NOT archived ORDER BY title",database);
        var result = new List<DocumentInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5),(reader.GetString(6).StartsWith("uploads/",StringComparison.Ordinal)||reader.GetString(6).StartsWith("wiki/",StringComparison.Ordinal)),reader.GetString(6).StartsWith("wiki/",StringComparison.Ordinal)));
        return result;
    }
    private async Task EnsureReadAsync(CompanyContext context,CancellationToken ct)
    {
        await using var connection=await database.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        await AccountPermissions.RevalidateAsync(connection,transaction,context,CompanyCapability.Read,ct);
    }
    internal static void ProfileParameters(NpgsqlCommand cmd, EmbeddingProfile profile)
    {
        cmd.Parameters.AddWithValue("p",profile.Provider);cmd.Parameters.AddWithValue("m",profile.Model);cmd.Parameters.AddWithValue("d",profile.Dimensions);cmd.Parameters.AddWithValue("c",profile.ChunkingVersion);
    }
    private static Passage Read(NpgsqlDataReader r) => new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.GetFieldValue<string[]>(7),r.GetString(8).StartsWith("uploads/",StringComparison.Ordinal));
}



