using Clary.Api;
using Xunit;
public class BrieFollowupTests
{
 [Theory] [InlineData("My camera failed")] [InlineData("New issue: the whole computer is slow")]
 public void PendingScopeQuestionDoesNotPreventExplicitTopicChange(string message){
  var id=Guid.NewGuid();var answer=new Answer("clarify",[SourceObservationQuestions.ApplicationScope],[],[],"Test",null){IssueId=id};
  var context=BrieConversation.Context([("My computer is slow",answer)],message);
  Assert.False(context.IsContinuation);Assert.NotEqual(id,context.IssueId);Assert.DoesNotContain("My computer is slow",context.ScopeQuestion);
 }
 [Theory]
 [InlineData("Only one application is slow; the rest of the computer works normally.")]
 [InlineData("The whole computer is slow, including File Explorer and my browser.")]
 [InlineData("It's only the browser; the other applications respond normally.")]
 public void DirectApplicationScopeAnswerRetainsIssueAndUserFacts(string message){
  var id=Guid.NewGuid();var answer=new Answer("clarify",[SourceObservationQuestions.ApplicationScope],[],[],"Test",null){IssueId=id};
  var context=BrieConversation.Context([("My computer is slow",answer)],message);
  Assert.True(context.IsContinuation);Assert.Equal(id,context.IssueId);Assert.Contains("My computer is slow",context.ScopeQuestion);
 }
 [Theory]
 [InlineData("It's only the browser; the other applications respond normally.",true)]
 [InlineData("Maybe it's only the browser; the other applications respond normally.",false)]
 [InlineData("It's not only the browser; the other applications respond normally.",false)]
 [InlineData("The browser is slow but I don't know about the other applications.",false)]
 public void BrowserScopeAnswerRequiresAffirmativeReportedScope(string message,bool expected)=>Assert.Equal(expected,SourceObservationQuestions.HasApplicationScope(message));
 [Theory] [InlineData("It still has no internet.")] [InlineData("It still freezes.")]
 public void PersistentSymptomAfterSourceStepNeedsReportedResultWithoutInventingOutcome(string message){
  var step=new AdviceStep("Record whether the problem affects one browser or every application.",[]);
  var id=Guid.NewGuid();var context=BrieConversation.Context([("My computer cannot browse",new Answer("supported",[],[step],[],"Test",null){IssueId=id})],message);
  Assert.True(context.IsContinuation);Assert.True(context.NeedsProgress);Assert.Equal(id,context.IssueId);
  Assert.Equal(StepOutcome.Unknown,context.CurrentProgress!.ReportedOutcome);Assert.Equal(step,context.CurrentProgress.Suggested);
 }
 [Theory] [InlineData("An external monitor works normally.",true)] [InlineData("My camera failed.",false)] [InlineData("New issue: an external monitor works normally.",false)]
 public void ObservationAboutSourceStepSubjectRetainsOnlyRelatedIssue(string message,bool expected){
  var id=Guid.NewGuid();var step=new AdviceStep("If the external monitor works while the built-in screen is black, record whether the screen has a visible backlight.",[]);
  var context=BrieConversation.Context([("My laptop's built-in screen is black",new Answer("supported",[],[step],[],"Test",null){IssueId=id})],message);
  Assert.Equal(expected,context.IsContinuation);Assert.Equal(expected,context.IssueId==id);
  if(expected){Assert.False(context.NeedsProgress);Assert.Equal(StepOutcome.Unknown,context.CurrentProgress!.ReportedOutcome);Assert.Contains("built-in screen is black",context.ScopeQuestion);}
 }
 [Theory]
 [InlineData("Hey, I'm having trouble with my computer: it shows a blue screen")]
 [InlineData("I'm having trouble with my printer, no output and a blinking red light")]
 [InlineData("Can you help me create a new asset?")]
 [InlineData("What is the computer troubleshooting policy?")]
 public void DetailedSymptomsAndTasksAreNotGenericDeviceTrouble(string message)=>Assert.False(BrieConversation.VagueInitial(message,BrieConversation.Context([],message)));
 [Fact] public void NaturalDeviceSwitchDuringClarificationStartsNewIssue(){
  var id=Guid.NewGuid();var prior=new Answer("clarify",[BrieConversation.SymptomQuestion],[],[],"Test",null){IssueId=id};
  var context=BrieConversation.Context([("My printer is not working",prior)],"My camera failed");
  Assert.DoesNotContain("printer",context.ScopeQuestion);Assert.False(context.IsContinuation);Assert.NotEqual(id,context.IssueId);
 }
 [Fact] public void MixedGreetingAndNewIssueKeepsFactsButFocusesRetrievalOnIssue(){
  const string issue="Model Z7 USB, no output, steady green light; not working.";
  var context=BrieConversation.Context([],"Hey, new issue: "+issue);
  Assert.Equal(issue,context.RetrievalQuestion);Assert.Contains("Model Z7",context.ScopeQuestion);
 }
 [Fact] public void DeviceAndSymptomClausesBeforeFailureAreNotVague(){
  const string message="Model Z7 USB, no output, steady green light; not working";
  Assert.False(BrieConversation.VagueInitial(message,BrieConversation.Context([],message)));
 }
 [Theory] [InlineData("I completed step 1",StepOutcome.Completed)] [InlineData("I finished that check",StepOutcome.Completed)] [InlineData("I fixed it",StepOutcome.Resolved)]
 public void PositiveOutcomeRetainsSourceBoundCurrentProgress(string message,StepOutcome expected){
  var step=new AdviceStep("Inspect the USB connection.",[new Citation(Guid.NewGuid(),"Inspect the USB connection.")]);
  var context=BrieConversation.Context([("Printer failed",new Answer("supported",[],[step],[],"Test",null))],message);
  Assert.Equal(step,Assert.Single(context.ActiveSourceSteps));Assert.Equal(expected,context.CurrentProgress!.ReportedOutcome);Assert.True(context.NeedsProgress);
 }
 [Theory] [InlineData("My camera has a black preview")] [InlineData("My camera failed")]
 public async Task NaturalTopicBoundaryPersistsWhenNewIssueOnlyClarifies(string newIssue){
  var seen=new List<ConversationContext>();
  var store=new ConversationStore((string q,ConversationContext c,CancellationToken ct)=>{
   seen.Add(c);return Task.FromResult(q.StartsWith("Printer")?new Answer("supported",[],[new("Inspect the USB connection.",[])],[],"Test",null):new Answer("clarify",["What error appeared?"],[],[],"Test",null));
  },"Test",documentationDriven:true);
  var first=await store.ReplyAsync(new("Printer is USB and cannot print",null),default);
  var second=await store.ReplyAsync(new(newIssue,first.ConversationId),default);
  await store.ReplyAsync(new("it still fails",second.ConversationId),default);
  Assert.DoesNotContain("Printer is USB",seen[2].ScopeQuestion);Assert.Empty(seen[2].ActiveSourceSteps);Assert.Contains("camera",seen[2].RetrievalQuestion);
 }
 [Fact] public void DetailBeforeFailurePhraseIsNotVague(){
  const string question="My camera shows a black preview and is not working";
  Assert.False(BrieConversation.VagueInitial(question,BrieConversation.Context([],question)));
 }
 [Fact] public void ExplanationOfCurrentCheckRetainsTheSelectedSource(){
  var answer=new Answer("supported",[],[new("Inspect the USB connection.",[new(Guid.NewGuid(),"Inspect the USB connection.")])],[],"Test",null);
  Assert.NotEmpty(BrieConversation.Context([("Printer failed",answer)],"Can you explain the current check?").ActiveSourceSteps);
 }
 [Theory]
 [InlineData("OK",StepOutcome.Unknown)] [InlineData("I tried",StepOutcome.Attempted)]
 [InlineData("I completed step 1",StepOutcome.Completed)] [InlineData("I tried it but it did not work",StepOutcome.Failed)]
 [InlineData("I couldn't complete it",StepOutcome.Unable)] [InlineData("it works now",StepOutcome.Resolved)]
 [InlineData("I have not completed step 1",StepOutcome.Unknown)] [InlineData("I haven't tried it",StepOutcome.Unknown)]
 [InlineData("I don't know if it works now",StepOutcome.Unknown)]
 public void ReportedOutcomeDoesNotConfuseAttemptWithSuccess(string message,StepOutcome expected){
  Assert.Equal(expected,BrieConversation.ReportedOutcome(message));
 }
 [Fact] public void FollowupAfterExplicitIssueBoundaryDoesNotReviveOlderFacts(){
  var printer=new Answer("supported",[],[new("Inspect the USB connection.",[])],[],"Test",null);
  var camera=new Answer("clarify",["What happens in the preview?"],[],[],"Test",null);
  var context=BrieConversation.Context([("Printer is USB",printer),("New issue: camera fails",camera)],"It shows a black preview");
  Assert.DoesNotContain("Printer is USB",context.ScopeQuestion);Assert.Empty(context.ActiveSourceSteps);
 }
 [Theory] [InlineData("My webcam is not working, black preview")] [InlineData("Printer not working, green light on")]
 public void DetailedShortSymptomsAreNotVague(string question){
  Assert.False(BrieConversation.VagueInitial(question,BrieConversation.Context([],question)));
 }
 [Fact] public void ExplicitNewIssueClearsPriorSelectedProcedure(){
  var step=new Answer("supported",[],[new("Inspect the USB connection.",[new(Guid.NewGuid(),"Inspect the USB connection.")])],[],"Test",null);
  var context=BrieConversation.Context([("Printer will not print",step)],"New issue: my camera is black and it fails in meetings");
  Assert.Equal("my camera is black and it fails in meetings",context.RetrievalQuestion);
  Assert.Equal("New issue: my camera is black and it fails in meetings",context.ScopeQuestion);
  Assert.Empty(context.ActiveSourceSteps);Assert.Empty(context.PreviousAdvice!);
 }
 [Theory] [InlineData("OK")] [InlineData("I tried")] [InlineData("I couldn't complete it")] [InlineData("it works now")]
 public void ShortOutcomesRetainIssue(string reply){
  var answer=new Answer("supported",[],[new("Inspect the USB connection.",[])],[],"Test",null);
  var context=BrieConversation.Context([("USB printer does not print",answer)],reply);
  Assert.Contains("USB printer",context.RetrievalQuestion);Assert.True(context.NeedsProgress);
 }
 [Fact] public void EarlierDeviceFactsSurviveSeveralFollowups(){
  var step=new Answer("supported",[],[new("Check printer power.",[])],[],"Test",null);
  var turns=new List<(string User,Answer Answer)>{("Printer is not printing",step),("Epson TM-T88V connected by USB, green light only",step)};
  turns.AddRange(Enumerable.Range(1,7).Select(i=>($"I tried check {i}, still no print",step)));
  Assert.Contains("Epson TM-T88V",BrieConversation.Context(turns,"What is next?").ScopeQuestion);
 }
 [Fact] public async Task SameClarificationDoesNotTurnIntoMissingDocumentation(){
  var store=new ConversationStore((string q,ConversationContext c,CancellationToken ct)=>Task.FromResult(new Answer("clarify",["What result did you see?"],[],[],"Test",null)),"Test",documentationDriven:true);
  var first=await store.ReplyAsync(new("My printer failed",null),default);
  var next=await store.ReplyAsync(new("what do you mean",first.ConversationId),default);
  Assert.Equal("clarify",next.Status);Assert.Single(next.Questions);
 }
 [Fact] public void NestedAtomicItemsAreNotLegacyBundledAdvice(){
  var answer=new Answer("supported",[],[new("Inspect the connection.\n   1. Read the label.\n   2. Compare the documented label.",[])],[],"Test",null);
  Assert.False(BrieConversation.AmbiguousLegacyProgress([("My printer fails",answer)],"Step 1 done whats next"));
 }
 [Fact] public void ExplicitNewTopicDoesNotTriggerOldLegacyClarification(){
  var answer=new Answer("supported",[],[new("1. Check printer power.\n2. Check USB.",[])],[],"Test",null);
  Assert.False(BrieConversation.AmbiguousLegacyProgress([("My printer fails",answer)],"For employee onboarding how do I complete step 1?"));
 }
 [Theory]
 [InlineData("HEY!!!",true)] [InlineData("Good morning.",true)]
 [InlineData("hey my printer won't print",false)] [InlineData("high priority ticket",false)]
 public void GreetingMatchesOnlyStandaloneSocialMessage(string message,bool expected)=>Assert.Equal(expected,BrieConversation.IsGreeting(message));
 [Fact] public async Task RepeatedStandaloneGreetingsRemainNatural(){
  var store=new ConversationStore((string q,ConversationContext c,CancellationToken ct)=>throw new Exception("Greeting must not query knowledge"),"Test",documentationDriven:true);
  var first=await store.ReplyAsync(new("hello",null),default);
  var second=await store.ReplyAsync(new("Hi!",first.ConversationId),default);
  Assert.Equal("Hi! How can I help you?",Assert.Single(first.Questions));
  Assert.Equal(first.Questions,second.Questions);Assert.Empty(second.Steps);
 }
 [Fact] public async Task GreetingDoesNotBecomeKnowledgeQuery(){
  var calls=0;
  var store=new ConversationStore((string q,ConversationContext c,CancellationToken ct)=>{
   calls++;return Task.FromResult(new Answer("supported",[],[new("Check printer power.",[])],[],"Test",null));
  },"Test",documentationDriven:true);
  var first=await store.ReplyAsync(new("My Epson printer uses USB and only its green light is on",null),default);
  var greeting=await store.ReplyAsync(new("hey",first.ConversationId),default);
  Assert.Equal(1,calls);Assert.Equal("clarify",greeting.Status);Assert.Empty(greeting.Steps);
  Assert.DoesNotContain("document",Assert.Single(greeting.Questions),StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public void ProgressAfterGreetingRetainsIssueAndActualOutcome(){
  var step=new Answer("supported",[],[new("Check printer power.",[])],[],"Test",null);
  var social=new Answer("clarify",["How is the printer check going?"],[],[],"Test",null);
  var context=BrieConversation.Context([("Epson TM-T88V USB green light nothing prints",step),("hey",social)],"i managed ti do step 1 whats next");
  Assert.Contains("TM-T88V",context.RetrievalQuestion);
  Assert.Contains("i managed ti do step 1",context.ModelQuestion);
  Assert.DoesNotContain("reportedOutcome\":\"hey",context.ModelQuestion);
 }
 [Fact] public async Task LegacyBundledStepAsksWhichActionWasCompleted(){
  var calls=0;
  var store=new ConversationStore((string q,ConversationContext c,CancellationToken ct)=>{
   calls++;return Task.FromResult(new Answer("supported",[],[new("1. Check power.\n2. Check USB.\n3. Test printing.",[])],[],"Test",null));
  },"Test",documentationDriven:true);
  var first=await store.ReplyAsync(new("Printer will not print",null),default);
  var next=await store.ReplyAsync(new("I did step 1 whats next",first.ConversationId),default);
  Assert.Equal(1,calls);Assert.Equal("clarify",next.Status);Assert.Contains("action",Assert.Single(next.Questions));
 }
}
