using Clary.Api;
using Xunit;

public class BrieHelpInterpretationTests
{
 [Theory]
 [InlineData("{\"requestType\":\"troubleshooting\",\"symptomQuote\":\"invented symptom\",\"searchQuery\":\"camera\"}",null)]
 [InlineData("{\"requestType\":\"troubleshooting\",\"symptomQuote\":\"\",\"searchQuery\":\"camera\"}",true)]
 [InlineData("{\"requestType\":\"procedure\",\"symptomQuote\":\"\",\"searchQuery\":\"camera setup\"}",false)]
 public async Task InterpretationRequiresVerbatimObservationAndAllowsProceduralQuestions(string proposal,bool? expected){
  var verifier=new ModelSupportVerifier(new HttpClient(new Handler(proposal)){BaseAddress=new("http://localhost/")},"Ollama","fixture");
  Assert.Equal(expected,(await verifier.InterpretHelpAsync("Can you help with my camera?",default))?.NeedsSymptoms);
 }
 [Fact] public async Task SemanticVagueRequestAsksForObservationBeforeRetrieval(){
  var repository=new Repo();var verifier=new Review{Interpretation=new(true,"work computer")};
  var result=await new TroubleshootingService(new Embed(),new Provider(),repository,"Test",verifier,documentationDriven:true).AskAsync("Something seems off today. Could you look?",default,BrieConversation.Context([],"Something seems off today. Could you look?"));
  Assert.Equal("clarify",result.Status);Assert.Contains(BrieConversation.SymptomQuestion,result.Questions);Assert.Equal(0,repository.Searches);
 }
 [Fact] public async Task InterpretedSearchTermsDoNotReplaceUserReportsPassedToAnswerModel(){
  var repository=new Repo();var embed=new Embed();var provider=new Provider();var verifier=new Review{Interpretation=new(false,"account MFA replacement phone")};
  const string user="Password works; the next code goes to my old phone.";
  await new TroubleshootingService(embed,provider,repository,"Test",verifier,documentationDriven:true).AskAsync(user,default,BrieConversation.Context([],user));
  Assert.Equal("account MFA replacement phone",embed.Query);Assert.Contains(user,provider.Question);Assert.DoesNotContain("account MFA replacement phone",provider.Question);
 }
 [Fact] public async Task ConcreteInterpretedSymptomIsNotOverriddenByVagueWordingHeuristic(){
  var repository=new Repo();var verifier=new Review{Interpretation=new(false,"browser freezing","one")};
  var prior=new Answer("clarify",[BrieConversation.SymptomQuestion],[],[],"Test",null);
  const string message="Start with the laptop. Only the browser is freezing; the other apps work.";
  var context=BrieConversation.Context([("The laptop is dragging and the printer has stopped too. Where do we start?",prior)],message);
  var result=await new TroubleshootingService(new Embed(),new Provider(),repository,"Test",verifier,documentationDriven:true).AskAsync(message,default,context);
  Assert.True(repository.Searches>0);Assert.DoesNotContain(BrieConversation.SymptomQuestion,result.Questions);
 }
 [Fact] public async Task SelectionScopeHintCannotConfirmAnUnreportedSourcePrerequisite(){
  var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","Full document","## Procedure\n1. If only one application is slow, record its name.",null);
  var option=AtomicSourceActions.Options([source])[0];var step=new AdviceStep(option.Text,[new(source.Id,option.EvidenceQuote)]);
  var verifier=new Review{Interpretation=new(false,"computer slowness","one")};
  const string reports="The browser is freezing. I do not know about the other applications.";
  var result=await new TroubleshootingService(new Embed(),new Provider(new("supported",[],[step],[],"Test",null)),new Repo(source),"Test",verifier,documentationDriven:true).AskAsync(reports,default,BrieConversation.Context([],reports));
  Assert.Equal("clarify",result.Status);Assert.Empty(result.Steps);Assert.Null(result.Prerequisite!.Confirmed);
 }
 [Fact] public void LongAnswerToClarificationRetainsEarlierUserFacts(){
  var issue=Guid.NewGuid();var prior=new Answer("clarify",[BrieConversation.SymptomQuestion],[],[],"Test",null){IssueId=issue};
  var context=BrieConversation.Context([("My computer freezes",prior)],"Only the browser is freezing; the other apps work.");
  Assert.True(context.IsContinuation);Assert.Equal(issue,context.IssueId);Assert.Contains("My computer freezes",context.ScopeQuestion);
 }
 [Theory] [InlineData("How do I onboard an employee?")] [InlineData("Laptop freezes during startup")] [InlineData("Laptop shows an error during startup")]
 public void UnrelatedLongRequestDoesNotInheritClarificationIssue(string message){
  var issue=Guid.NewGuid();var prior=new Answer("clarify",[BrieConversation.SymptomQuestion],[],[],"Test",null){IssueId=issue};
  var context=BrieConversation.Context([("My camera has a black preview",prior)],message);
  Assert.False(context.IsContinuation);Assert.NotEqual(issue,context.IssueId);Assert.DoesNotContain("camera",context.ScopeQuestion);
 }
 sealed class Embed:IEmbeddingProvider {public EmbeddingProfile Profile=>new("Test","Test",2,"v1");public string? Query;public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> q,CancellationToken ct){Query=q[0];return Task.FromResult<IReadOnlyList<float[]>>([[1,0]]);}}
 sealed class Repo(Passage? current=null):IKnowledgeRepository {public int Searches;readonly Passage p=current??new(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","Full document","Record the sign-in stage.",null);public Task<IReadOnlyList<Passage>> SearchAsync(float[] q,EmbeddingProfile p,int l,CancellationToken ct){Searches++;return Task.FromResult<IReadOnlyList<Passage>>([this.p]);}public Task<Passage?> GetApprovedPassageAsync(Guid id,CancellationToken ct)=>Task.FromResult<Passage?>(p);}
 sealed class Provider(Answer? answer=null):IAnswerProvider {public string? Question;public Task<Answer> GenerateAsync(string q,IReadOnlyList<Passage> p,CancellationToken ct){Question=q;return Task.FromResult(answer??new Answer("insufficient",[],[],[],"Test",null));}}
 sealed class Review:IAnswerVerifier {public HelpInterpretation? Interpretation;public Task<HelpInterpretation?> InterpretHelpAsync(string reports,CancellationToken ct)=>Task.FromResult(Interpretation);public Task<SupportReview> VerifyAsync(string q,Answer a,IReadOnlyList<Passage> p,CancellationToken ct)=>Task.FromResult(new SupportReview(true,"complete"));}
 sealed class Handler(string proposal):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content=proposal}}))});}
}
