using Clary.Api;
using Xunit;
public class SourcePrerequisiteTests {
 [Theory][InlineData("Actually, neither input device works directly in the laptop either.",true)][InlineData("Actually, my webcam does not work.",false)]public void CorrectionKeepsTheCurrentIssueUnlessItNamesAnotherSubject(string latest,bool continuation){var id=Guid.NewGuid();var answer=new Answer("supported",[],[new("If both input devices work directly but not through the dock, record the dock model.",[])],[],"fixture",null){IssueId=id};var context=BrieConversation.Context([("My keyboard and mouse work directly, but neither works through the dock.",answer)],latest);Assert.Equal(continuation,context.IsContinuation);Assert.Equal(continuation,context.IssueId==id);}
 [Fact]public async Task SemanticObservationStillRequiresIndependentSupport(){var source=Source("If both input devices work directly but not through the dock, record the dock model.");var verifier=new ObservingVerifier();var service=new TroubleshootingService(new Embedding(),new Provider(source),new Repository(source),"fixture",verifier,documentationDriven:true);var result=await service.AskAsync("My keyboard and mouse work directly, but neither works through the dock.",default);Assert.Empty(result.Steps);Assert.True(verifier.Observed);Assert.True(verifier.Verified);}
 sealed class ObservingVerifier:IAnswerVerifier {public bool Observed,Verified;public Task<bool?> ObservePrerequisiteAsync(string c,string f,CancellationToken ct){Observed=true;return Task.FromResult<bool?>(true);}public Task<SupportReview> VerifyAsync(string q,Answer a,IReadOnlyList<Passage> p,CancellationToken ct){Verified=true;return Task.FromResult(new SupportReview(false,"none"));}}
 [Fact]public void SuppliedMonitorObservationPreservesThePendingIssue(){var id=Guid.NewGuid();var source=Source("If the external monitor works while the built-in screen is black, record the backlight result.");var pending=new SourcePrerequisite(source.Id,source.RevisionId,source.Text,new("condition",[]),"the external monitor works while the built-in screen is black");var answer=new Answer("clarify",[SourcePrerequisites.Question],[],[],"fixture",null){IssueId=id,Prerequisite=pending};var context=BrieConversation.Context([("My laptop's built-in screen is black, but the power light is on.",answer)],"An external monitor works normally.");Assert.True(context.IsContinuation);Assert.Equal(id,context.IssueId);Assert.NotNull(context.PendingPrerequisite);}
 [Fact]public void CurrentStandaloneUncertaintyInvalidatesEarlierLiteralObservation()=>Assert.False(SourcePrerequisites.LiteralSatisfied("the phone works on the same Wi-Fi","The phone works on the same Wi-Fi.\nI am not sure anymore."));
 [Fact]public void LaterScopeCorrectionCannotReuseEarlierApplicationObservation(){var context=new ConversationContext("","",""){AnsweredApplicationScope=true};Assert.False(SourcePrerequisites.Satisfied("only one application is slow",context,"Only one application is slow\nIt is no longer only one application; now multiple applications are slow"));}
 [Fact]public void NestedConditionalCannotDropALaterPrerequisite()=>Assert.Null(SourcePrerequisites.Condition(new AdviceStep("If external monitor works, inspect the cable only if power is disconnected.",[])));
 [Fact]public async Task ConfirmationStillRequiresSemanticSupport(){
  var source=Source("If the indicator is blue, record the serial number.");var verifier=new RejectingVerifier();var service=new TroubleshootingService(new Embedding(),new Provider(source),new Repository(source),"fixture",verifier,documentationDriven:true);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);var next=await store.ReplyAsync(new("yes",first.ConversationId),default);Assert.Empty(next.Steps);Assert.True(verifier.Called);
 }
 sealed class RejectingVerifier:IAnswerVerifier {public bool Called;public Task<SupportReview> VerifyAsync(string q,Answer a,IReadOnlyList<Passage> p,CancellationToken ct){Called=true;return Task.FromResult(new SupportReview(false,"none"));}}
 [Theory]
 [InlineData("My laptop's built-in screen is black, but the power light is on.","If the external monitor works while the built-in screen is black, record whether the built-in screen has a visible backlight.")]
 [InlineData("My laptop is connected to Wi-Fi but cannot browse.","If the phone on the same Wi-Fi works but the computer cannot browse, record whether the problem affects one browser or every application.")]
 public async Task MissingPrerequisiteObservationCannotDisplayTheConditionalAction(string question,string text){
  var source=Source(text);var result=await Service(source).AskAsync(question,default,BrieConversation.Context([],question));
  Assert.True(result.Status=="clarify",result.Error??result.Status);Assert.Empty(result.Steps);Assert.Single(result.Questions);Assert.Single(result.Citations);
 }
 [Theory][InlineData("yes","supported")][InlineData("no","clarify")][InlineData("not sure","clarify")][InlineData("yes, but I am not sure","clarify")]
 public async Task SavedQuestionAdmitsOnlyClearAffirmativeObservation(string reply,string status){
  var source=Source("If the indicator is blue, record the serial number.");var service=Service(source);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);Assert.Empty(first.Steps);
  var next=await store.ReplyAsync(new(reply,first.ConversationId),default);Assert.Equal(status,next.Status);Assert.Equal(first.IssueId,next.IssueId);
  if(status=="supported"){Assert.Single(next.Steps);Assert.True(next.Prerequisite!.Confirmed);}else Assert.Empty(next.Steps);
 }
 [Fact]public async Task RevokedSourceCannotUseSavedConfirmation(){
  var source=Source("If the indicator is blue, record the serial number.");var repository=new Repository(source);var service=Service(source,repository);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);repository.Revoked=true;
  var next=await store.ReplyAsync(new("yes",first.ConversationId),default);Assert.Equal("error",next.Status);Assert.Empty(next.Steps);
 }
 [Fact]public async Task NewIssueCannotReuseSavedConfirmation(){
  var source=Source("If the indicator is blue, record the serial number.");var service=Service(source);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);
  var next=await store.ReplyAsync(new("New issue: yes",first.ConversationId),default);Assert.NotEqual(first.IssueId,next.IssueId);Assert.Empty(next.Steps);
 }
 [Theory][InlineData("The external monitor works. The built-in screen is black.",true)][InlineData("The external monitor works.",false)][InlineData("The external monitor does not work. The built-in screen is black.",false)][InlineData("Maybe the external monitor works. The built-in screen is black.",false)][InlineData("The external monitor works. The built-in screen is black. The external monitor does not work.",false)][InlineData("Does the external monitor works? The built-in screen is black.",false)]
 public void EveryLiteralConjunctRequiresAPositiveLatestObservation(string facts,bool expected)=>Assert.Equal(expected,SourcePrerequisites.LiteralSatisfied("the external monitor works while the built-in screen is black",facts));
 [Fact]public async Task UnparsedConditionalCannotBecomeAnActionOrConfirmation(){var source=Source("Record the serial number if the indicator is blue.");var result=await Service(source).AskAsync("The device is unavailable",default,BrieConversation.Context([],"The device is unavailable"));Assert.Empty(result.Steps);Assert.Null(result.Prerequisite);Assert.Equal(BrieConversation.ClearerProcedureQuestion,Assert.Single(result.Questions));}
 [Fact]public void LegacyAnswersOmitNullPrerequisiteMetadata(){var json=System.Text.Json.JsonSerializer.Serialize(new Answer("clarify",[],[],[],"fixture",null),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));Assert.DoesNotContain("prerequisite",json);}
 [Fact]public async Task NewlyConflictingEvidenceCannotUseSavedConfirmation(){
  var source=Source("If the indicator is blue, record the serial number.") with{ConflictGroup="fixture-policy"};var repository=new Repository(source);var service=Service(source,repository);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);repository.Additional=Source("Do not record the serial number.") with{ConflictGroup="fixture-policy"};
  var next=await store.ReplyAsync(new("yes",first.ConversationId),default);Assert.Equal("conflicting",next.Status);Assert.Empty(next.Steps);Assert.Equal(2,next.Citations.Count);
 }
 [Fact]public async Task ChangedSourceCannotUseSavedConfirmation(){
  var source=Source("If the indicator is blue, record the serial number.");var repository=new Repository(source);var service=Service(source,repository);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);repository.Current=source with{Text="1. If the indicator is blue, record the model."};
  var next=await store.ReplyAsync(new("yes",first.ConversationId),default);Assert.Equal("error",next.Status);Assert.Empty(next.Steps);
 }
 [Theory][InlineData("The indicator is blue but actually red")][InlineData("Yesterday the indicator was blue")][InlineData("The indicator is not not blue")][InlineData("The note says 'the indicator is blue'")]
 public void AmbiguousContradictoryOrQuotedObservationsDoNotEstablishTheCondition(string facts)=>Assert.False(SourcePrerequisites.LiteralSatisfied("the indicator is blue",facts));
 [Fact]public void CommaSeparatedConjunctionCannotDropARequiredPredicate(){var step=new AdviceStep("If the indicator is blue, and the switch is on, record the serial number.",[]);Assert.Null(SourcePrerequisites.Condition(step));Assert.True(SourcePrerequisites.HasConditionalLanguage(step));}
 [Fact]public async Task SavedConditionMustMatchTheActualSourcePredicate(){var source=Source("If the indicator is blue, record the serial number.");var option=AtomicSourceActions.Options([source]).Single();var pending=new SourcePrerequisite(source.Id,source.RevisionId,source.Text,new(option.Text,[new(source.Id,option.EvidenceQuote)]),"the device is available");var context=BrieConversation.Context([],"yes") with{PendingPrerequisite=pending};var result=await Service(source).AskAsync("yes",default,context);Assert.Equal("error",result.Status);Assert.Empty(result.Steps);}
 [Fact]public async Task NewObservationInvalidatesEarlierAffirmativeState(){
  var source=Source("If the indicator is blue, record the serial number.");var service=Service(source);var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
  var first=await store.ReplyAsync(new("The device is unavailable"),default);var confirmed=await store.ReplyAsync(new("yes",first.ConversationId),default);Assert.Single(confirmed.Steps);
  var next=await store.ReplyAsync(new("It is not blue now",first.ConversationId),default);Assert.Empty(next.Steps);Assert.Equal("clarify",next.Status);
 }
 static Passage Source(string text)=>new(Guid.NewGuid(),Guid.NewGuid(),"Synthetic guide","1","Procedure","1. "+text,null);
 static TroubleshootingService Service(Passage source,Repository? repository=null)=>new(new Embedding(),new Provider(source),repository??new Repository(source),"fixture",new Verifier(),documentationDriven:true);
 sealed class Verifier:IAnswerVerifier {public Task<SupportReview> VerifyAsync(string q,Answer a,IReadOnlyList<Passage> p,CancellationToken ct)=>Task.FromResult(new SupportReview(true,"complete"));}
 sealed class Embedding:IEmbeddingProvider {public EmbeddingProfile Profile=>new("fixture","fixture",2,"fixture");public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> q,CancellationToken ct)=>Task.FromResult<IReadOnlyList<float[]>>([[1,0]]);}
 sealed class Provider(Passage source):IAnswerProvider {public Task<Answer> GenerateAsync(string q,IReadOnlyList<Passage> p,CancellationToken ct){var option=AtomicSourceActions.Options([source]).Single();return Task.FromResult(new Answer("supported",[],[new(option.Text,[new(source.Id,option.EvidenceQuote)])],[],"fixture",null));}}
 sealed class Repository(Passage source):IKnowledgeRepository {public bool Revoked;public Passage? Additional;public Passage? Current;public Task<IReadOnlyList<Passage>> SearchAsync(float[] q,EmbeddingProfile p,int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Passage>>(Additional is null?[source]:[source,Additional]);public Task<Passage?> GetApprovedPassageAsync(Guid id,CancellationToken ct)=>Task.FromResult<Passage?>(Revoked?null:Additional?.Id==id?Additional:Current??source);}
}
