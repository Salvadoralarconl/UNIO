using Clary.Api;
using Xunit;

public class OrchestrationTests
{
    [Theory] [InlineData(false,"clarify")] [InlineData(true,"insufficient")]
    public async Task KnownIdentityNextRequestAsksMissingCurrentCheckResultOnlyWhileSourceApproved(bool revoked,string expected){
        var source=Source();var repo=new FakeRepository([]){Current=source,Revoked=revoked};var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        const string message="I already gave the error and connection. What next?";
        var context=BrieConversation.Context([("Printer offline by USB",new Answer("supported",[],[step],[],"Test",null))],message);
        var provider=new FakeAnswer(new("supported",[],[new("If the device appears, record the queue error.",[])],[],"Test",null));
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync(message,default,context);
        Assert.Equal(expected,result.Status);Assert.Empty(result.Steps);Assert.Equal(0,provider.Calls);
        if(!revoked)Assert.Contains("result",Assert.Single(result.Questions));
    }
    [Fact] public async Task ExplicitNewObservationWithNextRequestStillReachesDocumentedAnswerSelection(){
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Device guide","1","section","Record whether the device appears in Device Manager.",null);var repo=new FakeRepository([]){Current=source};var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        const string message="The device appears in Device Manager. What next?";
        var context=BrieConversation.Context([("Printer offline by USB",new Answer("supported",[],[step],[],"Test",null))],message);
        var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync(message,default,context);
        Assert.Equal(1,provider.Calls);
    }
    [Theory] [InlineData(false,"clarify")] [InlineData(true,"insufficient")]
    public async Task UnresolvedClarificationWithCurrentSourceAsksForAttemptsWithoutRevivingAnotherIssue(bool revoke,string expected){
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Camera guide","1","Full document","## Procedure\n1. Confirm the local preview.\n2. Record the selected device.",null);
        var repo=new FakeRepository([source]){Revoked=revoke};var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        var context=BrieConversation.Context([("My camera has a black preview",new Answer("clarify",[BrieConversation.SymptomQuestion],[],[],"Test",null))],"it still fails");
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync("it still fails",default,context);
        Assert.Equal(expected,result.Status);Assert.Empty(result.Steps);
    }
    [Fact] public async Task ReportedResolutionDoesNotAskForAlreadyProvidedOutcome(){
        var source=Source();var repo=new FakeRepository([]){Current=source};var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        var context=BrieConversation.Context([("Printer failed",new Answer("supported",[],[step],[],"Test",null))],"it works now");
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync("it works now",default,context);
        Assert.Contains("help",Assert.Single(result.Questions));Assert.Equal(0,provider.Calls);
    }
    [Fact] public async Task UnsegmentedProcedureClarificationPassesTheNormalAnswerValidator(){
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","Full document","Open the panel. Stop the service. Restart the service.",null);
        var provider=new FakeAnswer(new("clarify",["Is there a clearer approved procedure for this issue that I should use?"],[],[],"Application clarification",null));
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,new FakeRepository([source]),"Test",documentationDriven:true).AskAsync("Open the panel",default);
        Assert.Equal("clarify",result.Status);Assert.Null(result.Error);Assert.Contains("several actions",result.EvidenceNote);
    }
    [Fact] public async Task UnselectableNextStepDoesNotClaimCurrentApprovedProcedureIsMissing(){
        var source=Source();var repo=new FakeRepository([]){Current=source};var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        const string message="I tried that check. The device appears, but printing still fails. What is next?";
        var context=BrieConversation.Context([("Printer failed",new Answer("supported",[],[step],[],"Test",null))],message);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync(message,default,context);
        Assert.Equal("clarify",result.Status);Assert.Empty(result.Steps);Assert.Single(result.Questions);
    }
    [Fact] public async Task RequestToExplainCurrentCheckAsksWhichPartWithoutLosingSource(){
        var source=Source();var repo=new FakeRepository([]){Current=source};var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        const string message="Can you explain the current check?";
        var context=BrieConversation.Context([("Printer failed",new Answer("supported",[],[step],[],"Test",null))],message);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync(message,default,context);
        Assert.Equal("clarify",result.Status);Assert.Contains("part",Assert.Single(result.Questions));Assert.Equal(0,provider.Calls);
    }
    [Theory] [InlineData("OK")] [InlineData("I tried")] [InlineData("I couldn't complete it")]
    public async Task BriefProgressLocallyAsksMissingOutcomeAfterRevalidatingSource(string message){
        var source=Source();var repo=new FakeRepository([]){Current=source};
        var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        var context=BrieConversation.Context([("Printer does not print",new Answer("supported",[],[step],[],"Test",null))],message);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync(message,default,context);
        Assert.Equal("clarify",result.Status);Assert.Single(result.Questions);Assert.Empty(result.Steps);Assert.Equal(0,provider.Calls);
    }
    [Theory] [InlineData(false,"supported")] [InlineData(true,"insufficient")]
    public async Task FollowupRevalidatesPreviouslySelectedSourceEvenWhenSearchMisses(bool revoke,string expected){
        var source=Source();var repo=new FakeRepository([]){Current=source,Revoked=revoke};
        var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        var provider=new FakeAnswer(new("supported",[],[step],[],"Test",null));
        var context=BrieConversation.Context([("Printer does not print",new Answer("supported",[],[step],[],"Test",null))],"Can I repeat that check?");
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",documentationDriven:true).AskAsync("Can I repeat that check?",default,context);
        Assert.Equal(expected,result.Status);Assert.Equal(revoke?0:1,provider.Calls);
    }
    [Fact] public async Task KeywordFallbackUsesRetainedIssueInsteadOfBareProgress(){
        var repo=new FakeRepository([]);var context=new ConversationContext("USB printer no print, what next?","USB printer no print","what next?",[],true);
        await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(null),repo,"Test",documentationDriven:true).AskAsync("what next?",default,context);
        Assert.Equal(context.RetrievalQuestion,repo.KeywordQuestion);
    }
    [Fact] public async Task DocumentationProgressRepeatsAskForOutcomeInsteadOfRefusing(){
        var source=Source();var repo=new FakeRepository([source]);
        var provider=new FakeAnswer(new("supported",[],[new(source.Text,[new(source.Id,source.Text)])],[],"Test",null));
        var context=new ConversationContext("printer","printer","still not working",[source.Text],true);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",new FakeVerifier(true),repo,true).AskAsync("still not working",default,context);
        Assert.Equal("clarify",result.Status);Assert.Single(result.Questions);Assert.Empty(result.Steps);
    }
    [Fact] public async Task EmptyFirstSearchUsesBroaderApprovedRepository() {
        var source=Source();var broader=new FakeRepository([source]);
        var answer=new Answer("supported",[],[new(source.Text,[new(source.Id,source.Text)])],[],"fake",null);
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(answer),new FakeRepository([]),"Test",null,broader).AskAsync("printer",default);
        Assert.Equal("supported",result.Status);Assert.Equal(source.Id,result.Steps[0].Citations[0].PassageId);
    }
    [Fact] public async Task FailedSupportGetsOnlyOneBroaderAttemptThenAbstains() {
        var source=Source();var provider=new FakeAnswer(new("supported",[],[new("Disable antivirus.",[new(source.Id,source.Text)])],[],"fake",null));
        var repo=new FakeRepository([source]);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",new FakeVerifier(false),repo).AskAsync("printer",default);
        Assert.Equal("insufficient",result.Status);Assert.Empty(result.Steps);Assert.Equal(2,provider.Calls);
    }
    [Fact] public async Task RepeatedChecklistRetriesOnceThenAsksForResult() {
        var source=Source();var repo=new FakeRepository([source]);
        var provider=new FakeAnswer(new("supported",[],[new(source.Text,[new(source.Id,source.Text)])],[],"Test",null));
        var context=new ConversationContext("printer","printer","still not working",[source.Text],true);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,repo,"Test",new FakeVerifier(true),repo).AskAsync("still not working",default,context);
        Assert.Equal(2,provider.Calls);Assert.Equal("clarify",result.Status);Assert.Empty(result.Steps);Assert.Single(result.Questions);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task StillFailsRetainsApprovedStepAndAsksResultInsteadOfMissingDocumentation(bool rejected){
        var source=Source();var repo=new FakeRepository([]){Current=source};
        var step=new AdviceStep(source.Text,[new Citation(source.Id,source.Text)]);
        var context=BrieConversation.Context([("My camera failed",new Answer("supported",[],[step],[],"Test",null))],"it still fails");
        var answer=rejected?new Answer("supported",[],[step],[],"Test",null):new Answer("insufficient",[],[],[],"Test",null);
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(answer),repo,"Test",rejected?new FakeVerifier(false):null,documentationDriven:true).AskAsync("it still fails",default,context);
        Assert.Equal(StepOutcome.Failed,context.CurrentProgress!.ReportedOutcome);Assert.True(context.NeedsProgress);
        Assert.Equal("clarify",result.Status);Assert.Empty(result.Steps);Assert.Single(result.Questions);
    }
    [Theory]
    [InlineData(false,"clarify")] [InlineData(true,"insufficient")]
    public async Task ApprovedScopeGatheringBecomesNeutralQuestionOnlyAfterVerification(bool rejected,string expected){
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Slow computer","1","Full document","## Procedure\n1. Determine whether slowness affects only one application or the whole computer before choosing a branch.\n2. If only one application is slow, record its name and exact error.",null);
        var option=AtomicSourceActions.Options([source])[0];var step=new AdviceStep(option.Text,[new(option.PassageId,option.EvidenceQuote)]);
        var answer=new Answer("supported",[],[step],[],"Test",null);var repo=new FakeRepository([source]);
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(answer),repo,"Test",new FakeVerifier(!rejected),documentationDriven:true).AskAsync("My computer is slow",default,BrieConversation.Context([],"My computer is slow"));
        Assert.Equal(expected,result.Status);Assert.Empty(result.Steps);
        if(!rejected){Assert.Equal("Does this happen in one application or across the whole computer?",Assert.Single(result.Questions));Assert.Single(result.Citations);}
    }
    [Theory]
    [InlineData("im having trouble with mi computer")]
    [InlineData("im having trouble with my printer")]
    [InlineData("Hello, I am having problems with my laptop")]
    [InlineData("can you help me with my webcam please?")]
    [InlineData("can u help me with my computer pls?")]
    [InlineData("my computer has an issue")]
    [InlineData("I have a problem with my laptop")]
    [InlineData("my pc is acting up")]
    [InlineData("Hi, im having trouble with mi computer")]
    [InlineData("My work laptop’s been acting weird all morning. Can you help?")]
    [InlineData("Can you sort out this printer for me?")]
    [InlineData("My computer stopped working")]
    public async Task GenericDeviceTroubleAfterGreetingAsksSymptomsWithoutKnowledge(string message){
        var provider=new FakeAnswer(new("insufficient",[],[],[],"Test",null));
        var embedding=new FakeEmbedding();var service=new TroubleshootingService(embedding,provider,new FakeRepository([]),"Test",documentationDriven:true);
        var store=new ConversationStore((string q,ConversationContext c,CancellationToken ct)=>service.AskAsync(q,ct,c),"Test",documentationDriven:true);
        var greeting=await store.ReplyAsync(new("HEY",null),default);
        var result=await store.ReplyAsync(new(message,greeting.ConversationId),default);
        Assert.Equal("clarify",result.Status);Assert.Equal(BrieConversation.SymptomQuestion,Assert.Single(result.Questions));Assert.Empty(result.Steps);Assert.Equal(0,provider.Calls);Assert.Equal(0,embedding.Calls);
    }
    private static Passage Source(string? group = null) => new(Guid.NewGuid(), Guid.NewGuid(), "Guide", "1", "section", "Record the printer model.", group);
    [Fact] public async Task NoEvidenceAbstainsWithoutGeneration() {
        var provider = new FakeAnswer(null);
        var result = await new TroubleshootingService(new FakeEmbedding(),provider,new FakeRepository([]),"Demo workspace").AskAsync("unrelated",default);
        Assert.Equal("insufficient",result.Status); Assert.Empty(result.Steps); Assert.Equal(0,provider.Calls);
    }
    [Fact] public async Task RevokedSourceSuppressesGeneratedAdvice() {
        var source=Source();var repository=new FakeRepository([source]) { Revoked=true };
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(new("supported",[],[new("Record the printer model.",[new(source.Id,"Record the printer model.")])],[],"forged origin",null)),repository,"Live OpenAI").AskAsync("printer",default);
        Assert.Equal("error",result.Status);Assert.Empty(result.Steps);Assert.Equal("Live OpenAI",result.Origin);
    }
    [Fact] public async Task KnownConflictsNeverChooseAProcedure() {
        var provider=new FakeAnswer(null);
        var result=await new TroubleshootingService(new FakeEmbedding(),provider,new FakeRepository([Source("policy"),Source("policy")]),"Demo workspace").AskAsync("timeout",default);
        Assert.Equal("conflicting",result.Status);Assert.Empty(result.Steps);Assert.Equal(2,result.Citations.Count);Assert.Equal(0,provider.Calls);
    }
    [Fact] public async Task InjectionCannotCiteUnretrievedDraft() {
        var source=Source();var answer=new Answer("supported",[],[new("Disable antivirus",[new(Guid.NewGuid(),"draft procedure")])],[],"fake",null);
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(answer),new FakeRepository([source]),"Live OpenAI").AskAsync("printer",default);
        Assert.Equal("error",result.Status);Assert.Empty(result.Steps);
    }
    [Fact] public async Task UnsupportedParaphraseIsSuppressedBySupportCheck() {
        var source=Source();
        var answer=new Answer("supported",[],[new("Disable antivirus.",[new(source.Id,source.Text)])],[],"fake",null);
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(answer),new FakeRepository([source]),"Live Ollama",new FakeVerifier(false)).AskAsync("printer",default);
        Assert.Equal("insufficient",result.Status); Assert.Empty(result.Steps);
    }
    [Fact] public async Task SupportedParaphrasePreservesExactQuote() {
        var source=Source();
        var answer=new Answer("supported",[],[new("First, check which printer model you have.",[new(source.Id,source.Text)])],[],"fake",null);
        var result=await new TroubleshootingService(new FakeEmbedding(),new FakeAnswer(answer),new FakeRepository([source]),"Live Ollama",new FakeVerifier(true)).AskAsync("printer",default);
        Assert.Equal("supported",result.Status); Assert.NotEqual(result.Steps[0].Text,result.Steps[0].Citations[0].Quote);
        Assert.Equal(source.Text,result.Steps[0].Citations[0].Quote);
    }
    private sealed class FakeVerifier(bool supported):IAnswerVerifier {
        public Task<SupportReview> VerifyAsync(string question,Answer answer,IReadOnlyList<Passage> passages,CancellationToken ct)=>Task.FromResult(new SupportReview(supported,"partial"));
    }
    private sealed class FakeEmbedding:IEmbeddingProvider {
        public int Calls{get;private set;}
        public EmbeddingProfile Profile=>new("Test","test",2,"test");
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct){Calls++;return Task.FromResult<IReadOnlyList<float[]>>([[1,0]]);}
    }
    private sealed class FakeRepository(IReadOnlyList<Passage> passages):IKnowledgeRepository {
        public Passage? Current {get;set;}
        public string? KeywordQuestion {get;private set;}
        public Task<IReadOnlyList<Passage>> SearchKeywordsAsync(string question,EmbeddingProfile p,int limit,CancellationToken ct){KeywordQuestion=question;return Task.FromResult(passages);}
        public bool Revoked {get;set;}
        public Task<IReadOnlyList<Passage>> SearchAsync(float[] q,EmbeddingProfile p,int limit,CancellationToken ct)=>Task.FromResult(passages);
        public Task<Passage?> GetApprovedPassageAsync(Guid id,CancellationToken ct)=>Task.FromResult(Revoked?null:passages.FirstOrDefault(p=>p.Id==id)??(Current?.Id==id?Current:null));
    }
    private sealed class FakeAnswer(Answer? answer):IAnswerProvider {
        public int Calls{get;private set;}
        public Task<Answer> GenerateAsync(string q,IReadOnlyList<Passage> p,CancellationToken ct){Calls++;return Task.FromResult(answer!);}
    }
}
