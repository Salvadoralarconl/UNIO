using Clary.Api;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Http;
using System.Text;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
public class CompanyKnowledgeIsolationTests {
 [Fact] public async Task SearchOnlyReturnsCurrentCompany()=>await KnowledgeIsolationFixture.Run(async f=>{
  var ops=new LibraryOperations(f.Db);var a=await ops.NewPageAsync(f.A,new("Same title","Own sentinel USB",null),default);var b=await ops.NewPageAsync(f.B,new("Same title","Foreign sentinel USB",null),default);
  await f.Uploads.ApproveAndIndexAsync(f.A,a.Id,default);await f.Uploads.ApproveAndIndexAsync(f.B,b.Id,default);
  var repository=new KnowledgeRepository(f.Db,.3);var bound=repository.ForCompany(f.A);var profile=new KnowledgeIsolationFixture.TestEmbedding().Profile;
  var result=await bound.SearchAsync([1,0],profile,8,default);Assert.Equal(a.Id,Assert.Single(result).RevisionId);Assert.DoesNotContain(result,p=>p.Text.Contains("Foreign"));var foreign=Assert.Single(await repository.SearchAsync(f.B,[1,0],profile,8,default));Assert.Null(await bound.GetApprovedPassageAsync(foreign.Id,default));Assert.Null(await ops.ContentAsync(f.A,b.Id,default));Assert.Null(await ops.ResolveAsync(f.A,b.Id,default));Assert.Equal(a.Id,Assert.Single(await repository.DocumentsAsync(f.A,default)).Id);
  var answer=await new TroubleshootingService(new KnowledgeIsolationFixture.TestEmbedding(),new EchoAnswer(),bound,"Test",broaderRepository:repository.ForCompany(f.A,true)).AskAsync("How do I connect VPN?",default);Assert.Equal("supported",answer.Status);Assert.All(answer.Steps.SelectMany(step=>step.Citations),citation=>Assert.Equal(result[0].Id,citation.PassageId));Assert.DoesNotContain("Foreign sentinel",System.Text.Json.JsonSerializer.Serialize(answer));
  // A foreign profile mismatch must not block the current company, including broader/nearby paths.
  await f.Sql($"UPDATE documents SET model='foreign-other-model' WHERE id='{b.Id}'");Assert.Single(await repository.ForCompany(f.A,true).SearchAsync([1,0],profile,8,default));
 });
 [Fact] public async Task ApprovalAndMoveCannotCrossCompanies()=>await KnowledgeIsolationFixture.Run(async f=>{
  var ops=new LibraryOperations(f.Db);var library=new LibraryRepository(f.Db);var own=await ops.NewPageAsync(f.A,new("Same title","Own sentinel",null),default);var foreign=await ops.NewPageAsync(f.B,new("Same title","Foreign sentinel",null),default);var ownFolder=await library.CreateAsync(f.A,"Same folder",null,default);var foreignFolder=await library.CreateAsync(f.B,"Same folder",null,default);
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>f.Uploads.ApproveAndIndexAsync(f.A,foreign.Id,default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>library.MoveDocumentAsync(f.A,foreign.Id,ownFolder.Id,default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>library.MoveDocumentAsync(f.A,own.Id,foreignFolder.Id,default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>library.CreateAsync(f.A,"child",foreignFolder.Id,default));
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>ops.ApplyAsync(f.A,foreignFolder.Id,new("delete",null,null),default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>ops.ApplyAsync(f.A,ownFolder.Id,new("move",foreignFolder.Id,null),default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>ops.FileAsync(f.A,foreign.Id,new("delete",null,null),default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>ops.FileAsync(f.A,own.Id,new("move",foreignFolder.Id,null),default));
  Assert.Equal("draft",Assert.Single(await new KnowledgeRepository(f.Db,.3).DocumentsAsync(f.B,default)).Approval);Assert.Equal("Foreign sentinel",await f.Uploads.TextAsync(f.B,foreign.Id,default));Assert.Empty((await library.ListAsync(f.A,default)).Placements);Assert.Single((await library.ListAsync(f.B,default)).Folders);
 });
 [Fact] public async Task FilesAndImageReferencesRequireOwnedMetadata()=>await KnowledgeIsolationFixture.Run(async f=>{
  using var bytes=new MemoryStream(Encoding.UTF8.GetBytes("Own document"));var uploaded=await f.Uploads.UploadAsync(f.A,new FormFile(bytes,0,bytes.Length,"file","guide.md"),"Guide","1",default);Assert.NotNull(await f.Files.OpenAsync(f.A,uploaded.Id,default));Assert.Null(await f.Files.OpenAsync(f.B,uploaded.Id,default));await f.Sql($"UPDATE owned_files SET quarantined=true WHERE id='{uploaded.Id}'");Assert.Null(await f.Files.OpenAsync(f.A,uploaded.Id,default));
  var image=Guid.NewGuid();await f.Sql($"INSERT INTO owned_files(id,company_id,storage_path,kind) VALUES('{image}','{f.B.CompanyId}','uploads/images/{image}.bin','wiki_image')");var content="[{\"type\":\"img\",\"src\":\"/api/library/images/"+image+"\"}]";
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>new LibraryOperations(f.Db).NewPageAsync(f.A,new("Foreign image","",null,content),default));
 });
 [Fact] public async Task ExistingInventoryMappingCannotHideForeignSelection()=>await KnowledgeIsolationFixture.Run(async f=>{
  await new InventoryRepository(f.Db).CreateAsync(f.A,new("A","Own"),default);
  var foreign=await new LibraryOperations(f.Db).NewPageAsync(f.B,new("foreign","foreign",null),default);
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>new InventoryDocumentation(f.Db).LinkAsync(f.A,foreign.Id,false,default));
 });
 [Fact] public async Task EditingCannotAcceptForeignDestination()=>await KnowledgeIsolationFixture.Run(async f=>{
  var operations=new LibraryOperations(f.Db);var own=await operations.NewPageAsync(f.A,new("Own","Own",null),default);var folder=await new LibraryRepository(f.Db).CreateAsync(f.B,"Foreign",null,default);
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>operations.EditAsync(f.A,own.Id,new("Own","",folder.Id,"[{\"type\":\"p\",\"children\":[{\"type\":\"text\",\"text\":\"Changed\"}]}]"),default));
  Assert.Equal("Own",await f.Uploads.TextAsync(f.A,own.Id,default));
 });
 [Fact] public async Task RawImageUrlsRequireAuthenticationAndOwnership()=>await IdentityFixture.Run(async f=>{
  var id=Guid.NewGuid();var other=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{other}','Foreign'); INSERT INTO owned_files(id,company_id,storage_path,kind) VALUES('{id}','{other}','uploads/images/{id}.bin','wiki_image')");
  Assert.Equal(401,(int)(await f.Client.GetAsync("/api/library/images/"+id)).StatusCode);Assert.Equal(401,(int)(await f.Client.GetAsync("/api/files/"+id)).StatusCode);
  await f.Login();Assert.Equal(404,(int)(await f.Client.GetAsync("/api/library/images/"+id)).StatusCode);Assert.Equal(404,(int)(await f.Client.GetAsync("/api/files/"+id)).StatusCode);
 }); private sealed class EchoAnswer:IAnswerProvider {
  public Task<Answer> GenerateAsync(string question,IReadOnlyList<Passage> passages,CancellationToken ct){var p=passages[0];return Task.FromResult(new Answer("supported",[],[new(p.Text,[new(p.Id,p.Text)])],[],"Test",null));}
 }
 [Fact] public async Task OwnedImageSurvivesCopiesAndRevisions()=>await KnowledgeIsolationFixture.Run(async f=>{
  var id=Guid.NewGuid();Directory.CreateDirectory(Path.Combine(f.Folder,"uploads","images"));await File.WriteAllBytesAsync(Path.Combine(f.Folder,"uploads","images",id+".bin"),[137,80,78,71,13,10,26,10]);await f.Sql($"INSERT INTO owned_files(id,company_id,storage_path,kind) VALUES('{id}','{f.A.CompanyId}','uploads/images/{id}.bin','wiki_image')");
  var content="[{\"type\":\"img\",\"src\":\"/api/library/images/"+id+"\"}]";var operations=new LibraryOperations(f.Db);var page=await operations.NewPageAsync(f.A,new("Image page","",null,content),default);await operations.FileAsync(f.A,page.Id,new("copy",null,null),default);var revision=await operations.EditAsync(f.A,page.Id,new("Revised","",null,content),default);Assert.NotNull(await f.Files.OpenAsync(f.A,id,default));
  await using(var q=f.Db.CreateCommand($"SELECT count(*) FROM document_files WHERE company_id='{f.A.CompanyId}' AND file_id='{id}'"))Assert.Equal(3L,await q.ExecuteScalarAsync());
  await f.Sql($"UPDATE owned_files SET storage_path='uploads/../outside' WHERE id='{id}'");Assert.Null(await f.Files.OpenAsync(f.A,id,default));Assert.Null(await f.Files.OpenAsync(f.A,Guid.NewGuid(),default));
 }); [Theory][InlineData(false)][InlineData(true)]
 public async Task EscapedForeignImageNodesAreRejectedTransactionally(bool edit)=>await KnowledgeIsolationFixture.Run(async f=>{
  var image=Guid.NewGuid();await f.Sql($"INSERT INTO owned_files(id,company_id,storage_path,kind) VALUES('{image}','{f.B.CompanyId}','uploads/images/{image}.bin','wiki_image')");
  var operations=new LibraryOperations(f.Db);var original=edit?await operations.NewPageAsync(f.A,new("Original","Preserved prose",null),default):null;
  var content=$$"""[{"type":"p","children":[{"type":"strong","children":[{"type":"img","src":"\u002fapi/library/images/{{image}}"}]}]}]""";
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>edit?operations.EditAsync(f.A,original!.Id,new("Attack","",null,content),default):operations.NewPageAsync(f.A,new("Attack","",null,content),default));
  await using(var q=f.Db.CreateCommand($"SELECT count(*) FROM documents WHERE company_id='{f.A.CompanyId}'"))Assert.Equal(edit?1L:0L,await q.ExecuteScalarAsync());
  if(edit){Assert.Equal("Preserved prose",await f.Uploads.TextAsync(f.A,original!.Id,default));Assert.Equal(original.Id,await operations.ResolveAsync(f.A,original.Id,default));}
 });
 [Theory][InlineData(false)][InlineData(true)]
 public async Task EscapedOwnedImageNodesAreLinked(bool edit)=>await KnowledgeIsolationFixture.Run(async f=>{
  var image=Guid.NewGuid();await f.Sql($"INSERT INTO owned_files(id,company_id,storage_path,kind) VALUES('{image}','{f.A.CompanyId}','uploads/images/{image}.bin','wiki_image')");
  var operations=new LibraryOperations(f.Db);var original=edit?await operations.NewPageAsync(f.A,new("Original","Preserved prose",null),default):null;
  var content=$$"""[{"type":"p","children":[{"type":"strong","children":[{"type":"img","src":"\u002fapi/library/images/{{image}}"}]}]}]""";
  var saved=edit?await operations.EditAsync(f.A,original!.Id,new("Owned","",null,content),default):await operations.NewPageAsync(f.A,new("Owned","",null,content),default);
  await using var q=f.Db.CreateCommand($"SELECT count(*) FROM document_files WHERE company_id='{f.A.CompanyId}' AND document_id='{saved.Id}' AND file_id='{image}'");Assert.Equal(1L,await q.ExecuteScalarAsync());
 });
 [Fact] public async Task PlainTextImageUrlMentionsDoNotRequireOwnedImages()=>await KnowledgeIsolationFixture.Run(async f=>{
  var operations=new LibraryOperations(f.Db);var url="/api/library/images/"+Guid.NewGuid();var plain=await operations.NewPageAsync(f.A,new("Plain text","Mention "+url,null),default);
  var content=System.Text.Json.JsonSerializer.Serialize(new[]{new{type="p",children=new[]{new{type="text",text="Mention "+url}}}});
  var rich=await operations.NewPageAsync(f.A,new("Rich text","",null,content),default);await operations.EditAsync(f.A,rich.Id,new("Edited text","",null,content),default);
  Assert.Contains(url,(await operations.ContentAsync(f.A,rich.Id,default))!);await using var q=f.Db.CreateCommand($"SELECT count(*) FROM document_files WHERE company_id='{f.A.CompanyId}'");Assert.Equal(0L,await q.ExecuteScalarAsync());
 });}




