using Clary.Api;
using Xunit;
public class ConversationTests
{
    [Theory]
    [InlineData("It is not USB; it uses Ethernet")]
    [InlineData("It uses Ethernet, not USB")]
    [InlineData("I switched from USB to Ethernet")]
    public async Task CorrectedConnectionReplacesOldConnection(string correction){
        ConversationContext? context=null;var store=new ConversationStore((q,c,ct)=>{context=c;return Task.FromResult(new Answer("insufficient",[],[],[],"Test",null));},"Test");
        var first=await store.ReplyAsync(new("TM-T88V USB not printing"),default);
        await store.ReplyAsync(new(correction,first.ConversationId),default);
        Assert.Contains("Ethernet",context!.ScopeQuestion);Assert.DoesNotContain("USB",context.ScopeQuestion,StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("It now appears in Windows Settings; how do I print a test page?")]
    [InlineData("It now appears in Windows Settings. Print a test page please.")]
    public async Task ResolvedMissingPrinterDoesNotBlockTestPage(string update){
        ConversationContext? context=null;var store=new ConversationStore((q,c,ct)=>{context=c;return Task.FromResult(new Answer("insufficient",[],[],[],"Test",null));},"Test");
        var first=await store.ReplyAsync(new("TM-T88V USB not appearing in Windows Settings"),default);
        await store.ReplyAsync(new(update,first.ConversationId),default);
        Assert.False(ProcedureApplicability.MissingWindowsPrinter(context!.ScopeQuestion));Assert.Contains("test page",context.RetrievalQuestion);
    }
    [Fact] public async Task FirstQuestionContainingItDoesNotBecomeAFollowupSearch(){
        const string question="My Epson TM-T88V is connected by USB. The receipt paper is jammed. How should I clear it safely?";
        string? retrieval=null;var store=new ConversationStore((q,c,ct)=>{retrieval=c.RetrievalQuestion;return Task.FromResult(new Answer("insufficient",[],[],[],"Test",null));},"Test");
        await store.ReplyAsync(new(question),default);Assert.Equal(question,retrieval);
    }
    [Theory]
    [InlineData("My Epson TM-T88V is connected by USB. The printer will not turn on. What should I check?")]
    [InlineData("My Epson TM-T88V is connected by USB. It makes grinding noises. What should I do?")]
    [InlineData("My Epson TM-T88V is connected by USB. How should Feed / No Cut be configured?")]
    [InlineData("My Epson TM-T88V is connected by USB. The receipt is upside down. What should I check?")]
    public async Task SpecificPrinterRequestsReachDocumentationInsteadOfGenericSymptomQuestion(string question){
        var called=false;var store=new ConversationStore((q,c,ct)=>{called=true;return Task.FromResult(new Answer("supported",[],[],[],"Test",null));},"Test");
        var answer=await store.ReplyAsync(new(question),default);Assert.True(called);Assert.DoesNotContain(DiagnosticQuestions.All[7],answer.Questions);
    }
    [Fact] public async Task GreenLightAndUnavailableAreRecognizedWithoutRepeatingSymptomQuestion() {
        var store=new ConversationStore((q,c,ct)=>Task.FromResult(new Answer("insufficient",[],[],[],"Test",null)),"Test");
        var first=await store.ReplyAsync(new("My printer is not working"),default);
        var second=await store.ReplyAsync(new("There is onyl one green light nothing else and is does nothing wheni hit print",first.ConversationId),default);
        Assert.Equal(DiagnosticQuestions.All[6],Assert.Single(second.Questions));
        var third=await store.ReplyAsync(new("the printer appears as disables or unavailable something like that",first.ConversationId),default);
        Assert.DoesNotContain(DiagnosticQuestions.All[9],third.Questions);
    }
    [Fact] public async Task VagueFallbackCannotRepeatForever() {
        var store=new ConversationStore((q,c,ct)=>Task.FromResult(new Answer("clarify",[DiagnosticQuestions.All[9]],[],[],"Test",null)),"Test");
        var first=await store.ReplyAsync(new("My printer is not working"),default);
        var second=await store.ReplyAsync(new("I don't know",first.ConversationId),default);
        var third=await store.ReplyAsync(new("I still don't know",first.ConversationId),default);
        Assert.DoesNotContain(DiagnosticQuestions.All[9],third.Questions);
    }
    [Fact] public async Task SuccessfulConnectionCheckStillRequiresDocumentedEvidence() {
        var store=new ConversationStore((q,c,ct)=>Task.FromResult(new Answer("insufficient",[],[],[],"Test",null)),"Test");
        var first=await store.ReplyAsync(new("TM-T88V USB connection checked and now works"),default);
        var next=await store.ReplyAsync(new("Why wasn't it working?",first.ConversationId),default);
        Assert.Equal("insufficient",next.Status);Assert.Empty(next.Steps);Assert.Empty(next.Citations);
        Assert.Equal(BrieAnswerPolicy.MissingDocumentation,next.EvidenceNote);
    }
    [Fact] public async Task AttemptWithoutResultAsksWhatDeviceManagerShowed() {
        var calls=0;
        var store=new ConversationStore((q,c,ct)=>{calls++;return Task.FromResult(new Answer("supported",[],[new AdviceStep("Check Device Manager.",[])],[],"Test",null));},"Test");
        var first=await store.ReplyAsync(new("TM-T88V USB not appearing in Windows Settings"),default);
        var next=await store.ReplyAsync(new("I did that already but it still does not appear in settings",first.ConversationId),default);
        Assert.Equal("clarify",next.Status);Assert.Contains("What did Device Manager show",next.Questions[0]);Assert.Equal(1,calls);
    }
    [Fact] public void CompletePassageIsNotRejectedForEndingAtNextSectionIntroduction() {
        Assert.True(CitationSupport.IsSubstantive("Check for unknown USB devices.\n\nNext section:\n\nOpen:"));
        Assert.False(CitationSupport.IsSubstantive("Open:"));
        Assert.False(CitationSupport.IsSubstantive("4. Check Device Manager"));
    }
    [Fact] public async Task VaguePrinterRepliesKeepClarifyingAndPreserveContext() {
        var calls=0;ConversationContext? context=null;
        var store=new ConversationStore((q,c,ct)=>{calls++;context=c;return Task.FromResult(new Answer("insufficient",[],[],[],"Test",null));},"Test");
        var first=await store.ReplyAsync(new("My printer is not working"),default);
        Assert.Equal("clarify",first.Status);Assert.Single(first.Questions);
        Assert.Contains("try to print",first.Questions[0]);
        var second=await store.ReplyAsync(new("Not appearing in Windows Settings",first.ConversationId),default);
        Assert.Equal("clarify",second.Status);Assert.Contains("model",second.Questions[0]);
        var third=await store.ReplyAsync(new("TM-T88V",first.ConversationId),default);
        Assert.Equal("clarify",third.Status);Assert.Contains("connected",third.Questions[0]);Assert.Equal(0,calls);
        var fourth=await store.ReplyAsync(new("USB",first.ConversationId),default);
        Assert.Equal("insufficient",fourth.Status);Assert.Equal(1,calls);
        Assert.Contains("USB",context!.RetrievalQuestion);Assert.Contains("Settings",context.RetrievalQuestion);
        Assert.Contains("TM-T88V",context.ScopeQuestion);Assert.Contains("Assistant asked",context.ModelQuestion);
        Assert.Equal(first.ConversationId,fourth.ConversationId);
    }
    [Fact] public async Task UnknownConversationCannotInjectHistory() {
        var store=new ConversationStore((q,c,ct)=>throw new Exception("must not generate"),"Test");
        await Assert.ThrowsAsync<ArgumentException>(()=>store.ReplyAsync(new("USB",Guid.NewGuid()),default));
    }
    [Fact] public async Task SeparateConversationsDoNotShareDeviceDetails() {
        var store=new ConversationStore((q,c,ct)=>Task.FromResult(new Answer("insufficient",[],[],[],"Test",null)),"Test");
        var a=await store.ReplyAsync(new("My printer isn't working"),default);
        await store.ReplyAsync(new("TM-T88V USB",a.ConversationId),default);
        var b=await store.ReplyAsync(new("My printer isn't working"),default);
        Assert.NotEqual(a.ConversationId,b.ConversationId);Assert.Contains("try to print",b.Questions[0]);
    }
    [Fact] public async Task VagueReplyAsksForElaborationInsteadOfGuessing() {
        var store=new ConversationStore((q,c,ct)=>throw new Exception("must not generate"),"Test");
        var first=await store.ReplyAsync(new("My printer isn't working"),default);
        var next=await store.ReplyAsync(new("I don't know",first.ConversationId),default);
        Assert.Equal("clarify",next.Status);Assert.Contains("describe",next.Questions[0]);
    }
 [Fact] public async Task ModelAndConnectionReplyKeepsEarlierSymptomInRetrieval(){ConversationContext? captured=null;var store=new ConversationStore((q,c,ct)=>{captured=c;return Task.FromResult(new Answer("insufficient",[],[],[],"Test",null));},"Test");var first=await store.ReplyAsync(new("My printer isnt working"),default);await store.ReplyAsync(new("so nothing happend and the green light is on",first.ConversationId),default);await store.ReplyAsync(new("its an epson 88v and im connected through usb",first.ConversationId),default);Assert.Contains("green light",captured!.RetrievalQuestion);Assert.Contains("nothing happend",captured.RetrievalQuestion);Assert.Contains("88v",captured.RetrievalQuestion);}
}
