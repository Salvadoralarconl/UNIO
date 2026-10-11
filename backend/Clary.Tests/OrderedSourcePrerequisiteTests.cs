using Clary.Api;
using Xunit;

public class OrderedSourcePrerequisiteTests
{
    const string Procedure = "1. Record the exact error.\n2. Use the approved identity verification process before resetting credentials or enrollment. A display name alone is not identity proof.\n3. Direct the verified employee to the approved reset/enrollment flow.\n4. If an unsolicited prompt appears, stop and report possible compromise.";
    static AdviceStep Step(int index)
    {
        var passage = new Passage(Guid.NewGuid(), Guid.NewGuid(), "Synthetic", "1", "Procedure", Procedure, null);
        var option = AtomicSourceActions.Options([passage])[index];
        return new(option.Text, [new(passage.Id, option.EvidenceQuote)]);
    }
    [Fact] public void LaterResetRequiresEarlierSourceVerification() => Assert.Equal("Completion of the source prerequisite: Use the approved identity verification process", SourcePrerequisites.OrderedCondition(Step(2)));
    [Fact] public void EarlierInformationCollectionDoesNotRequireLaterVerification() => Assert.Null(SourcePrerequisites.OrderedCondition(Step(0)));
    [Fact] public void IndependentUrgentBranchRemainsAvailable() => Assert.Null(SourcePrerequisites.OrderedCondition(Step(3)));
    [Theory][InlineData("Purpose and symptoms")][InlineData("Scope")][InlineData("Basis")]
    public void ContextSectionsCannotBecomeProcedureActions(string heading)
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure","# Guide\n\n## "+heading+"\nUse for enrollment problems.\n\n## Procedure\n1. Record which system is failing and the exact error.",null);
        var choices=AtomicSourceActions.Options([source]);Assert.DoesNotContain(choices,o=>o.Text=="Use for enrollment problems.");Assert.Contains(choices,o=>o.Text.Contains("Record which system",StringComparison.Ordinal));
    }
    [Fact] public void ActivityMentionedOnlyInAnUrgentConditionDoesNotGateReporting()
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure",Procedure.Replace("If an unsolicited prompt appears, stop and report possible compromise.","If enrollment produces an unsolicited prompt, stop and report possible compromise."),null);
        var option=AtomicSourceActions.Options([source])[3];Assert.Null(SourcePrerequisites.OrderedCondition(new(option.Text,[new(source.Id,option.EvidenceQuote)])));
    }
    [Fact] public void LeadingBeforeWordingAlsoPreservesOrdering()
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure","1. Before starting the motor, disconnect the power supply.\n2. Start the motor for the documented test.\n3. Record the enclosure serial number.",null);
        var option=AtomicSourceActions.Options([source])[1];Assert.Equal(SourcePrerequisites.CompletionPrefix+"disconnect the power supply",SourcePrerequisites.OrderedCondition(new(option.Text,[new(source.Id,option.EvidenceQuote)])));
    }
    [Fact] public void OrderedRequirementCannotHideAnUnparsedConditional()
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure",Procedure.Replace("approved reset/enrollment flow.","approved reset/enrollment flow unless the administrator declines."),null);
        var option=AtomicSourceActions.Options([source])[2];var step=new AdviceStep(option.Text,[new(source.Id,option.EvidenceQuote)]);
        Assert.NotNull(SourcePrerequisites.OrderedCondition(step));Assert.Null(SourcePrerequisites.EffectiveCondition(step));Assert.True(SourcePrerequisites.HasConditionalLanguage(step));
    }
    [Theory]
    [InlineData("The password is accepted, then verification goes to my old phone.")]
    [InlineData("I have not completed the verification process.")]
    [InlineData("I am not sure whether verification is completed.")]
    [InlineData("The guide says 'verification completed'.")]
    [InlineData("Verification was completed earlier.\nI have not completed it for this request.")]
    public async Task SymptomsCannotBypassOrderingEvenWithPermissiveVerifier(string reports)
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure",Procedure,null);
        var service=new TroubleshootingService(new Embedding(),new Provider(),new Repository(source),"fixture",new Verifier(),documentationDriven:true);
        var result=await service.AskAsync(reports,default);
        Assert.Equal("clarify",result.Status);Assert.Empty(result.Steps);
        Assert.Contains(SourcePrerequisites.CompletionPrefix,result.Prerequisite!.Condition);
        Assert.Equal(Procedure,Assert.Single(result.Citations).Quote);
    }
    [Theory][InlineData("yes","supported")][InlineData("no","clarify")][InlineData("not sure","clarify")]
    public async Task CompletionReplyRemainsBoundToSavedSource(string reply,string status)
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure",Procedure,null);
        var service=new TroubleshootingService(new Embedding(),new Provider(),new Repository(source),"fixture",new Verifier(),documentationDriven:true);
        var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
        var first=await store.ReplyAsync(new("Verification goes to my old phone"),default);
        var next=await store.ReplyAsync(new(reply,first.ConversationId),default);
        Assert.Equal(status,next.Status);Assert.Equal(first.IssueId,next.IssueId);
        if(status=="supported")Assert.Single(next.Steps);else Assert.Empty(next.Steps);
    }
    [Fact] public async Task ChangedSourceCannotReuseOrderedCompletion()
    {
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic","1","Procedure",Procedure,null);var repository=new Repository(source);
        var service=new TroubleshootingService(new Embedding(),new Provider(),repository,"fixture",new Verifier(),documentationDriven:true);
        var store=new ConversationStore((q,c,ct)=>service.AskAsync(q,ct,c),"fixture",documentationDriven:true);
        var first=await store.ReplyAsync(new("Verification goes to my old phone"),default);repository.Current=source with{Text=Procedure.Replace("approved identity","different identity")};
        var next=await store.ReplyAsync(new("yes",first.ConversationId),default);Assert.Equal("error",next.Status);Assert.Empty(next.Steps);
    }
    sealed class Embedding:IEmbeddingProvider {public EmbeddingProfile Profile=>new("fixture","fixture",2,"fixture");public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> q,CancellationToken ct)=>Task.FromResult<IReadOnlyList<float[]>>([[1,0]]);}
    sealed class Provider:IAnswerProvider {public Task<Answer> GenerateAsync(string q,IReadOnlyList<Passage> p,CancellationToken ct){var option=AtomicSourceActions.Options(p)[2];return Task.FromResult(new Answer("supported",[],[new(option.Text,[new(option.PassageId,option.EvidenceQuote)])],[],"fixture",null));}}
    sealed class Verifier:IAnswerVerifier {public Task<bool?> ObservePrerequisiteAsync(string c,string f,CancellationToken ct)=>Task.FromResult<bool?>(true);public Task<SupportReview> VerifyAsync(string q,Answer a,IReadOnlyList<Passage> p,CancellationToken ct)=>Task.FromResult(new SupportReview(true,"complete"));}
    sealed class Repository(Passage source):IKnowledgeRepository {public Passage? Current;public Task<IReadOnlyList<Passage>> SearchAsync(float[] q,EmbeddingProfile p,int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Passage>>([Current??source]);public Task<Passage?> GetApprovedPassageAsync(Guid id,CancellationToken ct)=>Task.FromResult<Passage?>(Current??source);}
}
