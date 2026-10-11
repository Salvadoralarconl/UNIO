namespace Clary.Api;

public sealed record EmbeddingProfile(string Provider, string Model, int Dimensions, string ChunkingVersion);
public sealed record Passage(Guid Id, Guid RevisionId, string Title, string Version, string Location, string Text, string? ConflictGroup, IReadOnlyList<string>? SupportedModels=null, bool IsUploaded=false);
public sealed record Citation(Guid PassageId, string Quote);
public sealed record AdviceStep(string Text, IReadOnlyList<Citation> Citations);
public sealed record Answer(string Status, IReadOnlyList<string> Questions, IReadOnlyList<AdviceStep> Steps, IReadOnlyList<Citation> Citations, string Origin, string? Error, string? EvidenceNote=null, Guid? ConversationId=null){ public BrieTicketResult? TicketTask {get;init;} [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public Guid? IssueId {get;init;} [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public SourcePrerequisite? Prerequisite {get;init;} }
public sealed record SourcePrerequisite(Guid PassageId,Guid RevisionId,string SourceText,AdviceStep Step,string Condition,bool? Confirmed=null,string? ConflictGroup=null,bool ModelInterpreted=false,string? UserReports=null);
public sealed record QuestionRequest(string Question, Guid? ConversationId=null);
public enum StepOutcome {Unknown,Attempted,Completed,Failed,Unable,Resolved}
public sealed record StepProgress(AdviceStep Suggested,StepOutcome ReportedOutcome,string UserReport);
public sealed record ConversationContext(string RetrievalQuestion, string ScopeQuestion, string ModelQuestion,IReadOnlyList<string>? PreviousAdvice=null,bool NeedsProgress=false){public IReadOnlyList<AdviceStep> ActiveSourceSteps {get;init;}=[];public StepProgress? CurrentProgress {get;init;} public Guid IssueId {get;init;} public bool IsContinuation {get;init;} public bool AwaitingClarification {get;init;} public SourcePrerequisite? PendingPrerequisite {get;init;} public IReadOnlyList<SourcePrerequisite> PrerequisiteStates {get;init;}=[];public bool AnsweredApplicationScope {get;init;} public string? ApplicationScopeHint {get;init;}}
public sealed record DocumentInfo(Guid Id, string Title, string Version, string Approval, string Indexing, string? Error, bool IsUploaded=false,bool IsWiki=false);

public interface IEmbeddingProvider
{
    EmbeddingProfile Profile { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
public interface IAnswerProvider
{
    Task<Answer> GenerateAsync(string question, IReadOnlyList<Passage> passages, CancellationToken ct);
    Task<Answer> GenerateWithContextAsync(string question,IReadOnlyList<Passage> passages,ConversationContext? context,CancellationToken ct)=>GenerateAsync(question,passages,ct);
}
public interface IAnswerVerifier
{
    Task<HelpInterpretation?> InterpretHelpAsync(string userReports,CancellationToken ct)=>Task.FromResult<HelpInterpretation?>(null);
    Task<SupportReview> VerifyAsync(string question, Answer answer, IReadOnlyList<Passage> passages, CancellationToken ct);
    Task<SupportReview> VerifyPrerequisiteAsync(string question,Answer answer,IReadOnlyList<Passage> passages,SourcePrerequisite prerequisite,CancellationToken ct)=>VerifyAsync(question,answer,passages,ct);
    Task<bool?> ObservePrerequisiteAsync(string condition,string userFacts,CancellationToken ct)=>Task.FromResult<bool?>(null);
}
public sealed record HelpInterpretation(bool NeedsSymptoms,string SearchQuery,string? ApplicationScopeHint=null);
public sealed record SupportReview(bool Supported,string Coverage,IReadOnlyList<int>? SupportedSteps=null);
public interface IKnowledgeRepository
{
    Task<IReadOnlyList<Passage>> SearchAsync(float[] query, EmbeddingProfile profile, int limit, CancellationToken ct);
    Task<IReadOnlyList<Passage>> SearchKeywordsAsync(string question,EmbeddingProfile profile,int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Passage>>([]);
    Task<Passage?> GetApprovedPassageAsync(Guid id, CancellationToken ct);
}
