using Microsoft.Extensions.DependencyInjection;
using Clary.Api;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
public sealed class CompanyConversationIsolationTests {
 [Fact] public void ProductionConversationPersistenceRequiresTrustedCompanyContext(){
  var methods=typeof(ConversationRepository).GetMethods().Where(m=>m.DeclaringType==typeof(ConversationRepository)&&m.Name.EndsWith("Async")).ToArray();
  Assert.NotEmpty(methods);Assert.All(methods,m=>Assert.Equal(typeof(CompanyContext),m.GetParameters()[0].ParameterType));
 }
 [Fact] public void ProductionCacheRequiresCompanyAndActorContext(){
  Assert.Contains(typeof(ConversationStore).GetMethods(),m=>m.Name=="ReplyAsync"&&m.GetParameters()[0].ParameterType==typeof(CompanyContext));
  Assert.Contains(typeof(ConversationStore).GetMethods(),m=>m.Name=="Forget"&&m.GetParameters()[0].ParameterType==typeof(CompanyContext));
 }
 private static Answer Answer(string text)=>new("supported",[],[new(text,[new(Guid.NewGuid(),text)])],[],"Test",null);
 [Fact] public async Task SameConversationIdDoesNotReuseForeignCache()=>await KnowledgeIsolationFixture.Run(async f=>{
  var repository=new ConversationRepository(f.Db);var captured=new List<(CompanyContext Company,ConversationContext History)>();
  var store=new ConversationStore((company,q,c,ct)=>{captured.Add((company,c));return Task.FromResult(Answer(company.CompanyId==f.A.CompanyId?"Own citation":"Foreign citation"));},"Test",repository);
  var first=await store.ReplyAsync(f.A,new("How do I open OwnSentinel VPN?"),default);var id=first.ConversationId!.Value;
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>store.ReplyAsync(f.B,new("What next?",id),default));Assert.Single(captured);
  Assert.Null(await repository.LoadAsync(f.B,id,default));Assert.Empty(await repository.ListAsync(f.B,default));
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>repository.SaveAsync(f.B,id,[new("Foreign sentinel",Answer("Foreign citation"))],default));
  Assert.Contains("OwnSentinel",(await repository.LoadAsync(f.A,id,default))![0].Question);
  await repository.SaveAsync(f.A,id,[new("How do I open UpdatedSentinel VPN?",Answer("Own updated citation"))],default,await repository.LoadAsync(f.A,id,default));
  store.Forget(f.B,id);var continued=await store.ReplyAsync(f.A,new("What next?",id),default);Assert.DoesNotContain("Foreign",JsonSerializer.Serialize(continued));Assert.Equal(f.A,captured.Last().Company);Assert.Contains("UpdatedSentinel",captured.Last().History.ModelQuestion);Assert.DoesNotContain("OwnSentinel",captured.Last().History.ModelQuestion);
  await repository.SaveAsync(f.A,id,[new("How do I open UpdatedSentinel VPN?",Answer("Own updated citation"))],default,await repository.LoadAsync(f.A,id,default));store.Forget(f.A,id);await store.ReplyAsync(f.A,new("What next?",id),default);Assert.Contains("UpdatedSentinel",captured.Last().History.ModelQuestion);
 });
 [Fact] public async Task CacheSeparatesActorsWithinCompany()=>await KnowledgeIsolationFixture.Run(async f=>{
  var repository=new ConversationRepository(f.Db);ConversationContext? captured=null;
  var store=new ConversationStore((company,q,c,ct)=>{captured=c;return Task.FromResult(Answer("own citation"));},"Test",repository);
  var first=await store.ReplyAsync(f.A,new("How do I open OldActorSentinel VPN?"),default);var id=first.ConversationId!.Value;
  await repository.SaveAsync(f.A,id,[new("How do I open FreshActorSentinel VPN?",Answer("new own citation"))],default,await repository.LoadAsync(f.A,id,default));
  var other=f.A with{UserId=Guid.NewGuid(),Role=CompanyRole.Editor};await FixtureCompany.AddActorAsync(f.Db,other);
  await store.ReplyAsync(other,new("What next?",id),default);
  Assert.Contains("FreshActorSentinel",captured!.ModelQuestion);Assert.DoesNotContain("OldActorSentinel",captured.ModelQuestion);
 });
 [Fact] public async Task AlternatingActorsPreserveEveryCommittedTurn()=>await KnowledgeIsolationFixture.Run(async f=>{
  var r=new ConversationRepository(f.Db);var store=new ConversationStore((company,q,c,ct)=>Task.FromResult(Answer(q)),"Test",r);var b=f.A with{UserId=Guid.NewGuid(),Role=CompanyRole.Editor};await FixtureCompany.AddActorAsync(f.Db,b);
  var first=await store.ReplyAsync(f.A,new("How do I open VPN first?"),default);var id=first.ConversationId!.Value;
  await store.ReplyAsync(b,new("How do I configure VPN second?",id),default);await store.ReplyAsync(f.A,new("How do I verify VPN third?",id),default);
  Assert.Equal(new[]{"How do I open VPN first?","How do I configure VPN second?","How do I verify VPN third?"},(await r.LoadAsync(f.A,id,default))!.Select(t=>t.Question));
 });
 [Fact] public async Task IndependentStoresCannotLoseSuccessfulConcurrentTurns()=>await KnowledgeIsolationFixture.Run(async f=>{
  var r=new ConversationRepository(f.Db);var seed=new ConversationStore((company,q,c,ct)=>Task.FromResult(Answer(q)),"Test",r);var first=await seed.ReplyAsync(f.A,new("How do I open VPN first?"),default);var id=first.ConversationId!.Value;
  var arrived=0;var both=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  ConversationStore Store()=>new(async(company,q,c,ct)=>{if(Interlocked.Increment(ref arrived)==2)both.SetResult();await both.Task.WaitAsync(TimeSpan.FromSeconds(10));return Answer(q);},"Test",new ConversationRepository(f.Db));
  async Task<(string Question,bool Success)> Continue(ConversationStore store,string question){try{await store.ReplyAsync(f.A,new(question,id),default);return(question,true);}catch(ConversationConflict){return(question,false);}}
  var a=Store();var b=Store();var results=await Task.WhenAll(Continue(a,"How do I configure VPN concurrent A?"),Continue(b,"How do I verify VPN concurrent B?"));
  var saved=(await r.LoadAsync(f.A,id,default))!;foreach(var success in results.Where(x=>x.Success))Assert.Contains(saved,t=>t.Question==success.Question);Assert.Equal(1+results.Count(x=>x.Success),saved.Count);
  Assert.Single(results,x=>!x.Success);
  var failed=results.Single(x=>!x.Success);await (failed.Question.EndsWith("A?")?a:b).ReplyAsync(f.A,new("How do I retry VPN?",id),default);
  saved=(await r.LoadAsync(f.A,id,default))!;Assert.Equal(3,saved.Count);Assert.DoesNotContain(saved,t=>t.Question==failed.Question);Assert.Equal("How do I retry VPN?",saved.Last().Question);
 });
 [Fact] public async Task StaleQuestionEndpointReturnsHelpfulConflictAndRetryPreservesHistory(){
  var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  await IdentityFixture.Run(async f=>{
   await f.Login();var firstResponse=await f.Send(HttpMethod.Post,"/api/questions",new QuestionRequest("How do I open VPN first?"));var first=await firstResponse.Content.ReadFromJsonAsync<Answer>();var id=first!.ConversationId!.Value;
   var pending=f.Send(HttpMethod.Post,"/api/questions",new QuestionRequest("How do I configure VPN stale?",id));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
   var context=new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);var r=new ConversationRepository(f.Database);var expected=(await r.LoadAsync(context,id,default))!;
   await r.SaveAsync(context,id,expected.Append(new SavedTurn("Other committed question",Answer("Other committed answer"))).ToArray(),default,expected);release.SetResult();
   var response=await pending;Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.Contains("Retry your question",await response.Content.ReadAsStringAsync());
   Assert.Equal(2,(await r.LoadAsync(context,id,default))!.Count);
   var retry=await f.Send(HttpMethod.Post,"/api/questions",new QuestionRequest("How do I retry VPN?",id));Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
   var saved=(await r.LoadAsync(context,id,default))!;Assert.Equal(3,saved.Count);Assert.Contains(saved,t=>t.Question=="Other committed question");Assert.DoesNotContain(saved,t=>t.Question.Contains("stale"));
  },builder=>builder.Services.AddSingleton(sp=>new ConversationStore(async(company,q,c,ct)=>{if(q.Contains("stale")){entered.SetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(10));}return Answer(q);},"Test",sp.GetRequiredService<ConversationRepository>())));
 }
 [Fact] public async Task ForeignActionsAndDestinationsAreDenied()=>await KnowledgeIsolationFixture.Run(async f=>{
  var r=new ConversationRepository(f.Db);var own=Guid.NewGuid();var foreign=Guid.NewGuid();await r.SaveAsync(f.A,own,[new("Own",Answer("Own"))],default);await r.SaveAsync(f.B,foreign,[new("Foreign",Answer("Foreign"))],default);
  foreach(var operation in new[]{"rename","pin","unpin","archive","unarchive","folder","move","delete"})await Assert.ThrowsAsync<CompanyResourceMissing>(()=>r.ActionAsync(f.A,foreign,new(operation,"Changed"),default));
  var project=await r.CreateProjectAsync(f.B,"Same name",default);var folder=await r.CreatePinnedFolderAsync(f.B,"Same name",default);
  Assert.Empty(await r.ProjectsAsync(f.A,default));Assert.Empty(await r.PinnedFoldersAsync(f.A,default));
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>r.ActionAsync(f.A,own,new("move",ProjectId:project.Id),default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>r.ActionAsync(f.A,own,new("folder",FolderId:folder.Id),default));
  foreach(var operation in new[]{"rename","delete"})await Assert.ThrowsAsync<CompanyResourceMissing>(()=>r.PinnedFolderActionAsync(f.A,folder.Id,new(operation,"Changed"),default));
  Assert.Equal("Foreign",Assert.Single(await r.ListAsync(f.B,default)).Title);Assert.Null(Assert.Single(await r.ListAsync(f.A,default)).ProjectId);
 });
 [Fact] public async Task ForeignShareTokenIsDenied()=>await KnowledgeIsolationFixture.Run(async f=>{
  var r=new ConversationRepository(f.Db);var id=Guid.NewGuid();await r.SaveAsync(f.B,id,[new("Foreign sentinel",Answer("Foreign citation"))],default);var token=await r.ShareAsync(f.B,id,default);
  Assert.Null(await r.SharedAsync(f.A,token,default));await Assert.ThrowsAsync<CompanyResourceMissing>(()=>r.ShareAsync(f.A,id,default));Assert.NotNull(await r.SharedAsync(f.B,token,default));
 });
 [Fact] public async Task RevokedUserCannotContinueCachedChat()=>await IdentityFixture.Run(async f=>{
  await f.Login();var first=await f.Send(HttpMethod.Post,"/api/questions",new QuestionRequest("How do I open VPN?"));Assert.Equal(HttpStatusCode.OK,first.StatusCode);var answer=await first.Content.ReadFromJsonAsync<Answer>();
  await f.Sql("UPDATE memberships SET active=false");
  // Reuse the issued antiforgery token/cookie: revocation occurs before endpoint and cache access.
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/conversations/"+answer!.ConversationId)).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.PostAsJsonAsync("/api/questions",new QuestionRequest("What next?",answer.ConversationId))).StatusCode);
  await using var q=f.Database.CreateCommand("SELECT jsonb_array_length(turns) FROM conversations");Assert.Equal(1,await q.ExecuteScalarAsync());
 });
 [Fact] public async Task SharedEndpointRequiresMembershipAndOriginalCompanyVisibility()=>await IdentityFixture.Run(async f=>{
  var own=new CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);var r=new ConversationRepository(f.Database);var id=Guid.NewGuid();await r.SaveAsync(own,id,[new("Own sentinel",Answer("Own citation"))],default);var token=await r.ShareAsync(own,id,default);
  Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/shared/conversations/"+token)).StatusCode);await f.Login();Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/shared/conversations/"+token)).StatusCode);
  var other=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{other}','Other'); UPDATE memberships SET company_id='{other}' WHERE user_id='{IdentityFixture.User}'");
  Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/company",new{companyId=other})).StatusCode);
  var denied=await f.Client.GetAsync("/api/shared/conversations/"+token);Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);Assert.DoesNotContain("Own sentinel",await denied.Content.ReadAsStringAsync());
  await f.Sql("UPDATE memberships SET active=false");Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/shared/conversations/"+token)).StatusCode);
 });
 [Fact] public async Task UploadRollbackLeavesNoReadableFile()=>await KnowledgeIsolationFixture.Run(async f=>{
  await f.Sql("ALTER TABLE owned_files ADD CONSTRAINT reject_test_upload CHECK (kind <> 'document')");
  using var bytes=new MemoryStream(Encoding.UTF8.GetBytes("Own upload sentinel"));await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Uploads.UploadAsync(f.A,new FormFile(bytes,0,bytes.Length,"file","guide.md"),"Guide","1",default));
  Assert.Empty(Directory.GetFiles(Path.Combine(f.Folder,"uploads")));await using var q=f.Db.CreateCommand("SELECT (SELECT count(*) FROM documents)+(SELECT count(*) FROM owned_files)+(SELECT count(*) FROM document_files)");Assert.Equal(0L,await q.ExecuteScalarAsync());
  var orphan=Guid.NewGuid();await File.WriteAllTextAsync(Path.Combine(f.Folder,"uploads",orphan+".md"),"Orphan sentinel");Assert.Null(await f.Files.OpenAsync(f.A,orphan,default));Assert.Null(await f.Files.OpenAsync(f.B,orphan,default));
 });
}
