using Clary.Api;
using Clary.Api.Identity;
using Clary.Api.Accounts;
using Microsoft.AspNetCore.Builder;
using Npgsql;
using System.Text.Json;

var company=Guid.Parse("e788cda2-f133-4464-9f7b-26b7ca51efbf");
var user=Guid.Parse("693d3198-ec08-46dc-b45f-9a3914065573");
var context=new CompanyContext(company,user,CompanyRole.Administrator,null,null);
await using var db=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("Connection required."));
await using(var check=db.CreateCommand("SELECT name FROM companies WHERE id=@company AND active")){check.Parameters.AddWithValue("company",company);if(await check.ExecuteScalarAsync() as string!="UNIO - Test")throw new InvalidOperationException("Unexpected target workspace.");}
var profile=new EmbeddingProfile("Ollama",Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL")??"embeddinggemma",int.Parse(Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_DIMENSIONS")??"768"),"chars-1200-160-v1");
using var client=new HttpClient{BaseAddress=new Uri((Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")??"http://127.0.0.1:11434").TrimEnd('/')+"/"),Timeout=TimeSpan.FromMinutes(3)};
var embeddings=new OllamaEmbeddingProvider(client,profile);
// Probe the real embedding service before changing company data.
await embeddings.EmbedAsync(["IT knowledge import readiness check"],CancellationToken.None);
var indexer=new SampleLoader(db,embeddings);
var environment=WebApplication.CreateBuilder().Environment;
var uploads=new UploadService(db,indexer,environment);
var folders=new LibraryRepository(db);var pages=new LibraryOperations(db);
var rows=JsonSerializer.Deserialize<TrainingDocument[]>(await File.ReadAllTextAsync(args[0]),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var library=await folders.ListAsync(context,CancellationToken.None);
var root=library.Folders.FirstOrDefault(f=>f.ParentId is null&&f.Name=="IT Training (Test)")??await folders.CreateAsync(context,"IT Training (Test)",null,CancellationToken.None);
var groups=new Dictionary<string,Guid>();foreach(var name in rows.Select(r=>r.Folder).Distinct()){var f=library.Folders.FirstOrDefault(f=>f.ParentId==root.Id&&f.Name==name)??await folders.CreateAsync(context,name,root.Id,CancellationToken.None);groups[name]=f.Id;}
var results=new List<object>();
foreach(var row in rows){
 Guid? id=null;string? currentText=null;string? approval=null;string? indexing=null;
 await using(var q=db.CreateCommand("SELECT d.id,d.canonical_text,d.approval,d.indexing FROM documents d JOIN document_folders p ON p.document_id=d.id AND p.company_id=d.company_id WHERE d.company_id=@company AND p.folder_id=@folder AND d.title=@title AND NOT d.archived")){q.Parameters.AddWithValue("company",company);q.Parameters.AddWithValue("folder",groups[row.Folder]);q.Parameters.AddWithValue("title",row.Title);await using var r=await q.ExecuteReaderAsync();if(await r.ReadAsync()){id=r.GetGuid(0);currentText=r.GetString(1);approval=r.GetString(2);indexing=r.GetString(3);}}
 if(id is not null&&currentText!=row.Text)throw new InvalidOperationException("Existing test document content differs; no overwrite performed: "+row.Title);
 if(id is null){var created=await pages.NewPageAsync(context,new NewWikiPage(row.Title,row.Text,groups[row.Folder]),CancellationToken.None);id=created.Id;}
 if(approval!="approved"||indexing!="ready")await uploads.ApproveAndIndexAsync(context,id.Value,CancellationToken.None);
 Console.WriteLine("Ready: "+row.Title);results.Add(new{row.Title,Id=id,Folder=row.Folder});
 await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(args[0])!,"import-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
}
await using(var verify=db.CreateCommand("SELECT count(DISTINCT d.id),count(p.id) FROM documents d JOIN document_folders f ON f.document_id=d.id AND f.company_id=d.company_id JOIN library_folders l ON l.id=f.folder_id AND l.company_id=f.company_id LEFT JOIN passages p ON p.revision_id=d.id AND p.company_id=d.company_id WHERE d.company_id=@company AND l.parent_id=@root AND d.approval='approved' AND d.indexing='ready' AND NOT d.archived")){verify.Parameters.AddWithValue("company",company);verify.Parameters.AddWithValue("root",root.Id);await using var r=await verify.ExecuteReaderAsync();await r.ReadAsync();Console.WriteLine($"VERIFIED: {r.GetInt64(0)} approved ready documents; {r.GetInt64(1)} indexed passages.");}
// Verify retrieval against the same semantic search path used by Brie.
var repo=new KnowledgeRepository(db,.45,3,2,.2);
foreach(var question in new[]{"My printer is offline and cannot be found", "My webcam works in Windows but not Teams", "What should IT prepare before a new employee starts?"}){var vector=(await embeddings.EmbedAsync([question],CancellationToken.None))[0];var hits=await repo.SearchAsync(context,vector,profile,5,CancellationToken.None);Console.WriteLine("RETRIEVAL: "+question+" => "+string.Join(" | ",hits.Select(h=>h.Title).Distinct()));if(hits.Count==0)throw new InvalidOperationException("Retrieval smoke test returned no passages.");}
record TrainingDocument(string Folder,string Title,string Text);

