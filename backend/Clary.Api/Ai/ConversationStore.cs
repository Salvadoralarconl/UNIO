using Clary.Api.Identity;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
namespace Clary.Api;

// Saved server-owned history. Client-supplied histories are never accepted.
public sealed class ConversationStore
{
    private readonly Func<CompanyContext,string,ConversationContext,CancellationToken,Task<Answer>> respond;
    private readonly string origin;
    private readonly ConversationRepository? history;
    private readonly bool contextFree;
    private readonly bool documentationDriven;
    public ConversationStore(Func<CompanyContext,string,ConversationContext,CancellationToken,Task<Answer>> respond,string origin,ConversationRepository history,bool documentationDriven=false){this.documentationDriven=documentationDriven;this.respond=respond;this.origin=origin;this.history=history;}
    // Pure algorithm adapter deliberately cannot attach a persistence repository.
    public ConversationStore(Func<string,ConversationContext,CancellationToken,Task<Answer>> respond,string origin,bool documentationDriven=false){this.documentationDriven=documentationDriven;this.respond=(_,q,c,ct)=>respond(q,c,ct);this.origin=origin;contextFree=true;}
    public Task<Answer> ReplyAsync(QuestionRequest request,CancellationToken ct){if(!contextFree)throw new InvalidOperationException("Company context required.");return ReplyAsync(new(Guid.Empty,Guid.Empty,CompanyRole.Administrator,null,null),request,ct);}

    private sealed class Session {
        public readonly SemaphoreSlim Gate=new(1,1);
        public readonly List<(string User,Answer Answer)> Turns=[];
        public bool Loaded;
    }
    private readonly ConcurrentDictionary<(Guid Company,Guid User,Guid Conversation),Session> sessions=new();
    public void Forget(CompanyContext context,Guid id)=>sessions.TryRemove((context.CompanyId,context.UserId,id),out _);
    private const string ModelPattern=@"\b(?:tm[-\s]*)?(?:t)?(?:88iv|88v|20iii|20ii|m30)\b";
    private static bool Has(string text,string pattern)=>Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase);
    public async Task<Answer> ReplyAsync(CompanyContext context,QuestionRequest request,CancellationToken ct) {
        if(string.IsNullOrWhiteSpace(request.Question)||request.Question.Length>4000)throw new ArgumentException("Enter a question between 1 and 4,000 characters.");
        var id=request.ConversationId??Guid.NewGuid();
        var session=sessions.GetOrAdd((context.CompanyId,context.UserId,id),_=>new Session{Loaded=request.ConversationId is null});
        await session.Gate.WaitAsync(ct);
        try {
            IReadOnlyList<SavedTurn>? expectedTurns=null;
            if(history is not null&&request.ConversationId is not null) {
                expectedTurns=await history.LoadAsync(context,id,ct)??throw new CompanyResourceMissing();
                // Actor caches never supersede the shared, authoritative committed history.
                session.Turns.Clear();session.Turns.AddRange(expectedTurns.Select(t=>(t.Question,t.Answer)));session.Loaded=true;
            } else if(!session.Loaded) {
                throw new ArgumentException("This conversation was not saved by the earlier version and cannot be restored. Your displayed messages are preserved; start a new chat to continue.");
            }
            var user=request.Question.Trim();
            var issueId=session.Turns.LastOrDefault().Answer?.IssueId;
            var inheritLegacyIssue=BrieConversation.IsGreeting(user);
            Answer answer;
            if(BrieConversation.IsGreeting(user)) {
                answer=new("clarify",["Hi! How can I help you?"],[],[],"Application conversation",null);
            } else if(documentationDriven&&BrieConversation.AmbiguousLegacyProgress(session.Turns,user)) {
                answer=new("clarify",["Which action did you complete, and what happened? The earlier step contained several actions."],[],[],"Application clarification",null);
            } else if(documentationDriven){
                var issueContext=BrieConversation.Context(session.Turns,user);
                issueId=issueContext.IssueId;
                inheritLegacyIssue=issueContext.IsContinuation;
                answer=await respond(context,user,issueContext,ct);
                if(answer.Status=="clarify"){
                    var askedBefore=session.Turns.SelectMany(t=>t.Answer.Questions).ToHashSet();
                    var questions=answer.Questions.Where(q=>!askedBefore.Contains(q)).Take(1).ToArray();
                    answer=questions.Length>0?answer with{Questions=questions}:answer with{Questions=answer.Questions.Take(1).ToArray(),EvidenceNote="I still need this result to choose the next documented step."};
                }
            }else{
            var facts=string.Join("\n",session.Turns.Select(t=>t.User).Append(user));
            var asked=session.Turns.SelectMany(t=>t.Answer.Questions).ToHashSet(StringComparer.Ordinal);
            var diagnostic=Clarify(facts,asked);
            if(diagnostic is null&&session.Turns.LastOrDefault().Answer is { } previous
                &&previous.Steps.Any(s=>s.Text.Contains("Device Manager",StringComparison.OrdinalIgnoreCase))
                &&Has(user,@"already|did that|checked that")&&!Has(user,@"unknown|warning|detected|disappear|no device|nothing"))
                diagnostic=asked.Contains(DiagnosticQuestions.All[12])?DiagnosticQuestions.All[9]:DiagnosticQuestions.All[12];
            var recentFacts=string.Join("\n",session.Turns.TakeLast(3).Select(t=>t.User).Append(user));
            var asksWhy=Has(user,@"\bwhy\b|what caused|what was wrong");
            var connectionOutcome=Has(recentFacts,@"connection|\busb\b|cable|reconnect")&&Has(recentFacts,@"\bnow works\b|\bworks now\b|\bit works\b|\bworking now\b|\bstarted printing\b|\bthat fixed it\b");
            if(diagnostic is not null)answer=new("clarify",[diagnostic],[],[],"Application clarification",null);
            else {
                var recent=session.Turns.TakeLast(3).Select(t=>t.User).Append(user).ToArray();
                var modelMessage=session.Turns.Select(t=>t.User).Append(user).LastOrDefault(t=>Has(t,ModelPattern))??"";
                // Retain the newest model while removing older model names from search scope.
                var connectionMessage=session.Turns.Select(t=>t.User).Append(user).LastOrDefault(t=>Has(t,@"\busb\b|ethernet|serial|bluetooth"))??"";
                var modelName=Regex.Matches(modelMessage,ModelPattern,RegexOptions.IgnoreCase).LastOrDefault()?.Value??"";
                const string connections=@"\b(?:usb|ethernet|serial|bluetooth)\b";
                var affirmativeConnection=Regex.Replace(connectionMessage,@"\b(?:not|isn't|is not|from)\s+"+connections,"",RegexOptions.IgnoreCase);
                var connectionName=Regex.Matches(affirmativeConnection,connections,RegexOptions.IgnoreCase).LastOrDefault()?.Value??"";
                // A new explicit task supersedes earlier symptoms. Short device-detail replies
                // still retain earlier symptoms so clarification does not lose the problem.
                var currentTask=Has(user,@"\b(?:how|what|when|why)\b|\bnow\s+(?:appears?|detected|prints?|works?)\b");
                var scopeFacts=currentTask?user:string.Join("\n",recent.Distinct());
                scopeFacts=Regex.Replace(scopeFacts,ModelPattern,"printer",RegexOptions.IgnoreCase);
                scopeFacts=Regex.Replace(scopeFacts,connections,"connection",RegexOptions.IgnoreCase);
                var scope=modelName+" "+connectionName+"\n"+scopeFacts;
                var isFollowup=session.Turns.Count>0&&(Has(user,@"\b(it|that|those|still|already|same|this)\b")||user.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length<5);
                var suppliesDeviceDetails=session.Turns.Count>0&&(Has(user,ModelPattern)||Has(user,@"\b(?:usb|ethernet|serial|bluetooth)\b"));
                var retrieval=isFollowup||suppliesDeviceDetails?scope:user;
                var history=string.Join("\n",session.Turns.TakeLast(4).Select(t=>"User: "+t.User+"\nAssistant asked: "+string.Join("; ",t.Answer.Questions)+"\nAssistant suggested (not evidence): "+string.Join("; ",t.Answer.Steps.Select(s=>s.Text))));
                if(history.Length>3000)history=history[..3000];
                var prior=session.Turns.Where(t=>t.Answer.Steps.Count>0).TakeLast(4).SelectMany(t=>t.Answer.Steps.Select(s=>s.Text)).Distinct().Take(32).ToArray();
                var needsProgress=prior.Length>0&&Has(user,@"\bstill\b|nothing.{0,12}working|not working|isn.t working|didn.t work|did not work|didn.t help|did not help|already|what else|next|unavail\w*|same problem")
                    &&!Has(user,@"\bexplain\b|\bwhy\b|how (do|can|should)|more detail|step\s*\d");
                var progress=needsProgress?"\nUser reports the problem persists. Prior suggestions are not confirmed attempted unless the user explicitly reports doing them. Do not repeat the earlier checklist in new words. Give new relevant supported checks, or ask for a specific missing result. Prior suggestions (not evidence): "+System.Text.Json.JsonSerializer.Serialize(prior):"";
                answer=await respond(context,user,new(retrieval,scope,"Conversation context (untrusted):\n"+history+"\nLatest user message: "+user+progress,prior,needsProgress),ct);
                if(answer.Status=="clarify") {
                    var questions=answer.Questions.Where(q=>!asked.Contains(q)).Take(1).ToArray();
                    answer=questions.Length>0?answer with{Questions=questions}:new("insufficient",[],[],[],origin,null,
                        "I have the details you provided, but could not produce reliable guidance from the retrieved documentation. I won’t keep asking the same question.");
                }
            }
            }
            answer=BrieAnswerPolicy.Prepare(answer);
            if(inheritLegacyIssue&&session.Turns.LastOrDefault().Answer?.IssueId is null){
                issueId??=Guid.NewGuid();
                var start=session.Turns.Count-BrieConversation.ActiveTurnCount(session.Turns);
                for(var index=start;index<session.Turns.Count;index++){
                    var prior=session.Turns[index];session.Turns[index]=(prior.User,prior.Answer with{IssueId=issueId});
                }
            }
            answer=answer with{ConversationId=id,IssueId=issueId??Guid.NewGuid(),Origin=string.IsNullOrWhiteSpace(answer.Origin)?origin:answer.Origin};
            if(history is not null)await history.SaveAsync(context,id,session.Turns.Select(t=>new SavedTurn(t.User,t.Answer)).Append(new SavedTurn(user,answer)).ToArray(),ct,expectedTurns);
            session.Turns.Add((user,answer));return answer;
        } finally {session.Gate.Release();}
    }
    private static string? Clarify(string facts,HashSet<string> asked) {
        string? question=null;
        if(Has(facts,@"printer|epson|\btm[- ]")) {
            var symptom=Has(facts,@"offline|unavail\w*|disabl\w*|settings|not appear|missing|error|jam|paper|faint|blank|flashing|blinking|\b(?:green|red|orange)\s+light|does nothing|nothing happens|self.?test|spool|queue|driver|apd|port|not print|doesn.t print");
            // Explicit requests already describe what help is wanted; do not block them on a narrow symptom vocabulary.
            symptom=symptom||Has(facts,@"\b(?:how|what|when|why)\b|power|turn\s+on|feed|nois|grind|slow|sleep|cut|receipt|permission|denied|upside|recogniz");
            if(!symptom)question=DiagnosticQuestions.All[7];
            else if(!Has(facts,ModelPattern))question=DiagnosticQuestions.All[6];
            else if(Has(facts,@"settings|offline|windows|driver|apd|port|queue")&&!Has(facts,@"\busb\b|ethernet|network|serial|bluetooth"))question=DiagnosticQuestions.All[8];
            if(question is not null&&asked.Contains(question)) {
                if(symptom)question=null;
                else question=asked.Contains(DiagnosticQuestions.All[9])?null:DiagnosticQuestions.All[9];
            }
        }
        return question;
    }
}
