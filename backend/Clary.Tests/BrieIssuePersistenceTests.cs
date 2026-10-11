using Clary.Api;
using Xunit;
public class BrieIssuePersistenceTests
{
 [Theory] [InlineData("hey")] [InlineData("I tried it")]
 public async Task LegacySavedChatCanContinueAndKeepFactsAfterStoreReload(string firstReply)=>await KnowledgeIsolationFixture.Run(async f=>{
  var repository=new ConversationRepository(f.Db);var id=Guid.NewGuid();
  var step=new Answer("supported",[],[new("Inspect the USB connection.",[])],[],"Test",null);
  await repository.SaveAsync(f.A,id,[new SavedTurn("Model Z7 is connected by USB and cannot print",step)],default);
  await f.Sql($"UPDATE conversations SET turns=jsonb_set(turns,'{{0,answer}}',(turns->0->'answer')-'issueId') WHERE id='{id}'");
  var seen=new List<ConversationContext>();
  ConversationStore Store()=>new((company,q,c,ct)=>{seen.Add(c);return Task.FromResult(new Answer("clarify",["What result appeared?"],[],[],"Test",null));},"Test",repository,true);
  await Store().ReplyAsync(f.A,new(firstReply,id),default);
  await Store().ReplyAsync(f.A,new("it still fails",id),default);
  Assert.Contains("Model Z7",seen.Last().ScopeQuestion);
  var saved=await repository.LoadAsync(f.A,id,default);Assert.NotNull(saved);Assert.NotNull(saved[0].Answer.IssueId);
  Assert.All(saved,turn=>Assert.Equal(saved[0].Answer.IssueId,turn.Answer.IssueId));
 });
}
