namespace Clary.Api;

public sealed class TroubleshootingService(IEmbeddingProvider embeddings, IAnswerProvider answers, IKnowledgeRepository repository, string origin, IAnswerVerifier? verifier=null,IKnowledgeRepository? broaderRepository=null,bool documentationDriven=false)
{
    public async Task<Answer> AskAsync(string question,CancellationToken ct,ConversationContext? context=null)=>BrieAnswerPolicy.Prepare(await AskCoreAsync(question,ct,context,false));
    private async Task<Answer> AskCoreAsync(string question, CancellationToken ct, ConversationContext? context,bool broader)
    {
        var CanBroaden=!broader&&broaderRepository is not null;
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4000) throw new ArgumentException("Enter a question between 1 and 4,000 characters.");
        var interpretation=documentationDriven&&verifier is not null?await verifier.InterpretHelpAsync(context?.ScopeQuestion??question,ct):null;
        if(interpretation?.NeedsSymptoms==true)return new("clarify",[BrieConversation.SymptomQuestion],[],[],"Application clarification",null);
        if(interpretation?.ApplicationScopeHint is "one" or "whole")context=(context??BrieConversation.Context([],question)) with{ApplicationScopeHint=interpretation.ApplicationScopeHint};
        if(documentationDriven&&BrieConversation.VagueInitial(question,context))return new("clarify",[BrieConversation.SymptomQuestion],[],[],"Application clarification",null);
        Answer AskPrerequisite(SourcePrerequisite prerequisite)=>new("clarify",[SourcePrerequisites.QuestionFor(prerequisite.Condition)],[],prerequisite.Step.Citations,"Application clarification",null,"The approved source's prerequisite is: "+prerequisite.Condition){Prerequisite=prerequisite};
        SourcePrerequisite? pendingAction=null;
        if(documentationDriven&&context?.PendingPrerequisite is {} pending){
            var current=await repository.GetApprovedPassageAsync(pending.PassageId,ct);
            if(current is null||current.RevisionId!=pending.RevisionId||current.Text!=pending.SourceText||current.ConflictGroup!=pending.ConflictGroup||SourcePrerequisites.EffectiveCondition(pending.Step)!=pending.Condition||!AtomicSourceActions.IsExactBoundStep(pending.Step,[current]))
                return Failure("The prerequisite's source changed or lost approval. Ask again for current guidance.");
            var confirmed=SourcePrerequisites.Confirmation(question);
            if(confirmed==false)return new("clarify",["Which part of the quoted condition does not match what you observed?"],[],pending.Step.Citations,"Application clarification",null){Prerequisite=pending with{Confirmed=false}};
            if(confirmed==true||SourcePrerequisites.Satisfied(pending.Condition,context,context.ScopeQuestion))pendingAction=pending with{Confirmed=true};
            else if(SourcePrerequisites.IsReply(question))return AskPrerequisite(pending);
            else if(verifier is not null&&SourcePrerequisites.CanObserve(pending.Condition,context.ScopeQuestion)&&await verifier.ObservePrerequisiteAsync(pending.Condition,context.ScopeQuestion,ct)==true)pendingAction=pending with{Confirmed=true,ModelInterpreted=true,UserReports=context.ScopeQuestion};
        }
        // Search paraphrases are retrieval hints only, never reported facts or authority.
        var retrievalQuestion=interpretation?.SearchQuery??context?.RetrievalQuestion??question;
        var scope=context?.ScopeQuestion??question;
        if(!documentationDriven)retrievalQuestion=SearchFocus.Apply(retrievalQuestion,scope);
        if(!documentationDriven&&context?.NeedsProgress==true&&System.Text.RegularExpressions.Regex.IsMatch(scope,@"printer|epson|t88v",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            retrievalQuestion+="\nSearch next checks: unavailable printer; USB detection in Device Manager; APD registration and port configuration.";
        // Broaden source coverage without replacing the user's specific task
        // with generic printer/USB terms that drown out physical or layout issues.
        var vector = (await embeddings.EmbedAsync([retrievalQuestion],ct))[0];
        var activeRepository=broader?broaderRepository!:repository;
        var retainedActiveSource=false;
        Answer NeedReportedResult()=>new("clarify",["What result or error did the last documented check show?"],[],[],"Application clarification",null,"I found the current approved procedure, but could not verify a next step from the reported result. I will not guess a fix.");
        var passages = await activeRepository.SearchAsync(vector,embeddings.Profile,broader?32:5,ct);
        // Natural-language embeddings can miss a guide whose title matches the user's
        // actual words. Fall back to scoped, approved lexical evidence before refusing.
        if(documentationDriven||passages.Count==0){
            var lexical=await activeRepository.SearchKeywordsAsync(retrievalQuestion,embeddings.Profile,3,ct);
            passages=passages.Concat(lexical).DistinctBy(p=>p.Id).Take(8).ToArray();
        }
        if(pendingAction is {} retainedPrerequisite){
            var current=await activeRepository.GetApprovedPassageAsync(retainedPrerequisite.PassageId,ct);
            if(current is null||current.RevisionId!=retainedPrerequisite.RevisionId||current.Text!=retainedPrerequisite.SourceText||current.ConflictGroup!=retainedPrerequisite.ConflictGroup)return Failure("The prerequisite's source changed or lost approval. Ask again for current guidance.");
            passages=new[]{current}.Concat(passages).DistinctBy(p=>p.Id).ToArray();
        }
        // Saved assistant text remains context. Only freshly authorized source text
        // can retain the selected procedure when a short follow-up search misses it.
        if(documentationDriven&&context?.ActiveSourceSteps.Count>0){
            var retained=new List<Passage>();
            foreach(var citation in context.ActiveSourceSteps.SelectMany(s=>s.Citations).DistinctBy(c=>c.PassageId)){
                var current=await activeRepository.GetApprovedPassageAsync(citation.PassageId,ct);
                if(current is not null&&current.Text.Contains(citation.Quote,StringComparison.Ordinal))retained.Add(current);
            }
            passages=retained.Concat(passages).DistinctBy(p=>p.Id).Take(broader?8:5).ToArray();
            retainedActiveSource=retained.Count>0;
            if(retained.Count>0&&context.CurrentProgress is { } progress){
                if(context.NeedsProgress&&progress.ReportedOutcome==StepOutcome.Unknown&&System.Text.RegularExpressions.Regex.IsMatch(question.Trim(),@"^(?:what(?: is)? next[!?]?|I already (?:gave|provided) (?:the )?(?:error|model|connection)(?: and (?:the )?(?:error|model|connection))*[.!?]\s*What(?: is)? next[!?]?)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return NeedReportedResult();
                if(progress.ReportedOutcome==StepOutcome.Resolved&&System.Text.RegularExpressions.Regex.IsMatch(question.Trim(),@"^(?:(?:it|that) works? now|(?:it|that) now works?|(?:that |I )?(?:fixed|resolved) (?:it|the issue))[!?.\s]*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return new("clarify",["Glad that worked. How else can I help?"],[],[],"Application conversation",null);
                if(System.Text.RegularExpressions.Regex.IsMatch(question.Trim(),@"^(?:(?:can|could|would) you )?(?:please )?explain (?:the )?(?:current|last|that|this) (?:check|step)[!?.\s]*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return new("clarify",["Which part of the current documented check would you like explained?"],[],[],"Application clarification",null);
                var brief=System.Text.RegularExpressions.Regex.IsMatch(question.Trim(),@"^(?:(?:ok|okay)|I (?:tried|attempted)(?: (?:it|that))?|(?:I |I still )?(?:couldn't|could not|can't|cannot|am unable to) (?:complete|do|finish) (?:it|that|the check|step\s*\d+))[!?.\s]*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if(brief&&progress.ReportedOutcome is StepOutcome.Unknown or StepOutcome.Attempted or StepOutcome.Unable)
                    return new("clarify",[progress.ReportedOutcome==StepOutcome.Unable?"Which part could you not complete, and what prevented it?":"What happened when you tried the last suggested check?"],[],[],"Application clarification",null);
            }
        }
        var scoped=documentationDriven?(NeedsModel:false,Passages:passages):EpsonEvidence.Select(context?.ScopeQuestion??question,passages);
        if(scoped.NeedsModel)return new("clarify",["What printer model and connection type are you using?"],[],[],origin,null);
        passages=documentationDriven?scoped.Passages:ProcedureApplicability.Select(scope,scoped.Passages);
        if(broader) {
            // Consider more documents without overflowing the model's context or duplicating a dominant guide.
            var groups=passages.GroupBy(p=>p.RevisionId).Select(g=>g.ToArray()).ToArray();
            var diverse=Enumerable.Range(0,groups.Select(g=>g.Length).DefaultIfEmpty(0).Max()).SelectMany(i=>groups.Where(g=>g.Length>i).Select(g=>g[i]));
            var selected=new List<Passage>();var characters=0;
            foreach(var p in diverse){if(selected.Count==8)break;if(characters+p.Text.Length>9000)continue;selected.Add(p);characters+=p.Text.Length;}
            passages=selected;
        }
        if (passages.Count == 0) return CanBroaden?await AskCoreAsync(question,ct,context,true):new("insufficient",[],[],[],origin,null,"No relevant information was found in the approved knowledge base for this question. I cannot recommend a documented fix.");
        Answer answer;
        var conflict=passages.Where(p=>p.ConflictGroup is not null).GroupBy(p=>p.ConflictGroup).FirstOrDefault(g=>g.Select(p=>p.RevisionId).Distinct().Count()>1);
        if(conflict is not null) answer=new("conflicting",[],[],conflict.Select(p=>new Citation(p.Id,p.Text)).ToArray(),origin,null);
        else if(pendingAction is {} confirmedPrerequisite){
            var current=await activeRepository.GetApprovedPassageAsync(confirmedPrerequisite.PassageId,ct);
            if(current is null||current.RevisionId!=confirmedPrerequisite.RevisionId||current.Text!=confirmedPrerequisite.SourceText||current.ConflictGroup!=confirmedPrerequisite.ConflictGroup||!AtomicSourceActions.IsExactBoundStep(confirmedPrerequisite.Step,[current]))return Failure("The prerequisite's source changed or lost approval. Ask again for current guidance.");
            answer=new("supported",[],[confirmedPrerequisite.Step],[],origin,null){Prerequisite=confirmedPrerequisite};
        }
        else answer=(await answers.GenerateWithContextAsync((context?.ModelQuestion??question)+(broader?"\nApplication retry: the first search did not yield verified guidance. Use this broader evidence set to give a concise relevant checklist. Preserve conditions and warnings. Explain what is supported without inventing a diagnosis.":""),passages,context,ct)) with { Origin=origin,Error=null,Prerequisite=null };
        if(answer.Status=="insufficient"&&CanBroaden)return await AskCoreAsync(question,ct,context,true);
        if(answer.Status=="insufficient"&&retainedActiveSource&&context?.NeedsProgress==true)return NeedReportedResult();
        if(answer.Status=="insufficient"&&documentationDriven&&context is {IsContinuation:true,AwaitingClarification:true}){
            foreach(var candidate in passages.Where(p=>AtomicSourceActions.Options([p]).Count>0)){
                var current=await activeRepository.GetApprovedPassageAsync(candidate.Id,ct);
                if(current is not null&&current.Text==candidate.Text)return new("clarify",[DiagnosticQuestions.All[11]],[],[],"Application clarification",null,"I could not choose a supported check yet. More specific results will help choose the applicable documented guidance.");
            }
        }
        if (!CitationValidator.IsValid(answer,passages,allowParaphrase:verifier is not null)) return Failure("Citation validation failed. No unvalidated guidance was displayed.");
        if(documentationDriven&&context?.NeedsProgress==true){
            var fresh=answer.Steps.Where(step=>!(context.PreviousAdvice??[]).Contains(step.Text,StringComparer.Ordinal)).ToArray();
            if(answer.Status=="supported"&&fresh.Length==0)return CanBroaden?await AskCoreAsync(question,ct,context,true):new("clarify",["What happened when you tried the last suggested check?"],[],[],"Application clarification",null);
            answer=answer with{Steps=fresh};
        }
        // Verify the single displayed step against the complete source context, including warnings.
        answer=BrieAnswerPolicy.Prepare(answer);
        if(!documentationDriven&&context?.NeedsProgress==true&&ConversationProgress.RepeatsChecklist(answer.Steps.Select(s=>s.Text).ToArray(),context.PreviousAdvice??[])) {
            if(CanBroaden)return await AskCoreAsync(question,ct,context,true);
            var printer=System.Text.RegularExpressions.Regex.IsMatch(scope,@"printer|epson|t88v",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return new("clarify",[DiagnosticQuestions.All[printer?12:11]],[],[],"Application clarification",null,
                "Those suggestions overlap the earlier checklist. I could not verify a new check yet; this result will help choose the next documented step.");
        }
        if(!documentationDriven&&!ProcedureApplicability.IsValid(scope,answer))return new("clarify",[DiagnosticQuestions.All[12]],[],[],"Application clarification",null,
            "I found related documentation, but a printer queue has not been confirmed. Please share the detection result so I can give relevant guidance instead of queue-based fixes.");
        if(answer.Steps.Any(s=>s.Citations.Any(c=>EpsonEvidence.IsCurated(passages.Single(p=>p.Id==c.PassageId))&&!EpsonEvidence.IsProcedure(c.Quote))))
            return Failure("Incomplete printer procedure was rejected. Ask a model-specific question.");
        foreach(var id in answer.Citations.Concat(answer.Steps.SelectMany(s=>s.Citations)).Select(c=>c.PassageId).Distinct())
        {
            var current=await activeRepository.GetApprovedPassageAsync(id,ct);
            if(current is null || current.Text!=passages.Single(p=>p.Id==id).Text) return Failure("A cited source changed or lost approval. Ask again after the knowledge base is reviewed.");
        }
        if(documentationDriven&&answer.Status=="supported"&&pendingAction is null)foreach(var step in answer.Steps){
            var condition=SourcePrerequisites.EffectiveCondition(step);
            if(condition is null){if(SourcePrerequisites.HasConditionalLanguage(step))return new("clarify",[BrieConversation.ClearerProcedureQuestion],[],step.Citations,"Application clarification",null,"The source's prerequisite could not be separated safely from its action.");continue;}
            if(!AtomicSourceActions.IsExactBoundStep(step,passages))return Failure("A conditional action was not bound to an approved source step.");
            var source=passages.Single(p=>p.Id==step.Citations[0].PassageId);
            var prior=context?.PrerequisiteStates.FirstOrDefault(p=>p.PassageId==source.Id&&p.RevisionId==source.RevisionId&&p.SourceText==source.Text&&p.ConflictGroup==source.ConflictGroup&&p.Condition==condition);
            if(prior?.Confirmed==false&&!SourcePrerequisites.LiteralSatisfied(condition,question))return new("clarify",["Which part of the quoted condition does not match what you observed?"],[],step.Citations,"Application clarification",null){Prerequisite=prior};
            if(prior?.Confirmed!=true&&!SourcePrerequisites.Satisfied(condition,context,scope)){
                var prerequisite=new SourcePrerequisite(source.Id,source.RevisionId,source.Text,step,condition,ConflictGroup:source.ConflictGroup);
                if(verifier is null||!SourcePrerequisites.CanObserve(condition,scope)||await verifier.ObservePrerequisiteAsync(condition,scope,ct)!=true)return AskPrerequisite(prerequisite);
                pendingAction=prerequisite with{Confirmed=true,ModelInterpreted=true,UserReports=scope};answer=answer with{Prerequisite=pendingAction};
            }
        }
        if(verifier is not null && answer.Steps.Count>0) {
            var review=pendingAction is {} confirmed?await verifier.VerifyPrerequisiteAsync(context?.ModelQuestion??question,answer,passages,confirmed,ct):await verifier.VerifyAsync(context?.ModelQuestion??question,answer,passages,ct);
            if(!review.Supported)return CanBroaden?await AskCoreAsync(question,ct,context,true):retainedActiveSource&&context?.NeedsProgress==true?NeedReportedResult():new("insufficient",[],[],[],origin,null,"I found related documentation, but could not verify a supported answer to this question. I will not fill the gaps with a guessed fix.");
            answer=answer with{EvidenceNote=review.Coverage=="partial"?"This is the related guidance I could find. It addresses only part of your question; I do not have enough documented information for the rest.":"These steps are based on the retrieved documentation. They do not establish a diagnosis or guarantee that the issue will be resolved."};
        } else answer=answer with{EvidenceNote=answer.Status=="insufficient"?"I found related passages, but not enough reliable support to answer your question or recommend a fix.":answer.Status=="clarify"&&answer.Questions.Contains(BrieConversation.ClearerProcedureQuestion)&&AtomicSourceActions.Options(passages).Count==0&&CitationReferences.ProcedureOptions(passages).Any(p=>AtomicSourceActions.IsUnsegmented(p.Text))?BrieConversation.UnsegmentedNote:null};
        // A model check takes time; recheck approval after it as well.
        if(verifier is not null)foreach(var id in answer.Steps.SelectMany(s=>s.Citations).Select(c=>c.PassageId).Distinct()) {
            var current=await activeRepository.GetApprovedPassageAsync(id,ct);
            if(current is null||current.Text!=passages.Single(p=>p.Id==id).Text)return Failure("A cited source changed or lost approval. Ask again.");
        }
        if(documentationDriven&&answer.Status=="supported"&&answer.Steps.Count==1&&
           AtomicSourceActions.IsExactBoundStep(answer.Steps[0],passages)&&
           SourceObservationQuestions.IsApplicationScopeCheck(answer.Steps[0].Text)&&!SourceObservationQuestions.HasApplicationScope(scope))
            return answer with{Status="clarify",Questions=[SourceObservationQuestions.ApplicationScope],Citations=answer.Steps[0].Citations,Steps=[],Origin="Application clarification",EvidenceNote=null};
        return verifier is null?GuidancePresentation.Prepare(answer,passages):answer;
    }
    private Answer Failure(string message)=>new("error",[],[],[],origin,message);
}
