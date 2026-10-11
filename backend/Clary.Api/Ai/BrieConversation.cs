using System.Text.Json;
using System.Text.RegularExpressions;
namespace Clary.Api;

public static class BrieConversation
{
 public const string SymptomQuestion="What happens when you try, including any error message or visible symptom?";
 public const string ClearerProcedureQuestion="Is there a clearer approved procedure for this issue that I should use?";
 public const string UnsegmentedNote="The related source groups several actions together, so I could not safely select a single complete step. Ask IT to review that procedure, or say ‘Create a support ticket’ for help escalating this issue.";
 public static StepOutcome ReportedOutcome(string message){
  bool Has(string pattern)=>Regex.IsMatch(message,pattern,RegexOptions.IgnoreCase);
  if(Has(@"\b(?:couldn't|could not|can't|cannot|unable to|didn't|did not)\s+(?:complete|finish|do)\b"))return StepOutcome.Unable;
  if(Has(@"\b(?:didn't|did not|doesn't|does not)\s+(?:work|help)|\bfail(?:s|ed|ing)?\b"))return StepOutcome.Failed;
  if(Has(@"\b(?:haven't|have not|didn't|did not|not yet)\s+(?:tried|try|attempted|attempt|completed|complete|finished|finish)\b|\b(?:don't know|do not know|not sure|unsure)\b"))return StepOutcome.Unknown;
  if(Has(@"\b(?:it|that)\s+works?\s+now\b|\bnow\s+works?\b|\b(?:fixed|resolved)\s+(?:it|the issue)\b"))return StepOutcome.Resolved;
  if(Has(@"\b(?:completed|finished)\b|\bdid\s+(?:step|that|it)\b|\bstep\s*\d+.*\bdone\b|^done[!?.\s]*$"))return StepOutcome.Completed;
  if(Has(@"\b(?:tried|attempted)\b"))return StepOutcome.Attempted;
  return StepOutcome.Unknown;
 }
 public static bool StartsNewIssue(string text)=>Regex.IsMatch(text,@"\b(?:new|different|another)\s+(?:issue|problem|question|topic)\b|\b(?:switch|change)\s+(?:the\s+)?topic\b",RegexOptions.IgnoreCase);
 private static IReadOnlyList<(string User,Answer Answer)> ActiveTurns(IReadOnlyList<(string User,Answer Answer)> turns){
  if(turns.LastOrDefault().Answer?.IssueId is { } issueId)return turns.Where(t=>t.Answer.IssueId==issueId).ToArray();
  var boundary=turns.Select((turn,index)=>(turn,index)).LastOrDefault(item=>StartsNewIssue(item.turn.User)).index;
  return turns.Skip(boundary).ToArray();
 }
 public static int ActiveTurnCount(IReadOnlyList<(string User,Answer Answer)> turns)=>ActiveTurns(turns).Count;
 public static bool CanInitializeLegacyFacts(IReadOnlyList<(string User,Answer Answer)> turns,string latest)=>turns.Count>0&&!IsGreeting(latest)&&!StartsNewIssue(latest)&&
  turns.All(t=>!StartsNewIssue(t.User)&&t.Answer.IssueId is null&&t.Answer.Status=="clarify"&&t.Answer.Questions.Count==0&&t.Answer.Steps.Count==0&&t.Answer.Prerequisite is null&&t.Answer.TicketTask is null)&&!NamesDifferentSubject(turns,latest);
 private static bool NamesDifferentSubject(IReadOnlyList<(string User,Answer Answer)> turns,string latest){
  if(turns.Count==0)return false;
  var subjectMessage=Regex.Replace(latest,@"^\s*(?:actually|correction|to correct that)\s*[:,]?\s*","",RegexOptions.IgnoreCase);
  var subject=Regex.Match(subjectMessage,@"^\s*(?:my|our|the)\s+(?<subject>[\p{L}][\p{L}-]+)\b",RegexOptions.IgnoreCase).Groups["subject"].Value;
  if(subject.Length==0||Regex.IsMatch(subject,@"^(?:issue|problem|check|step|result|model|connection|error|symptom|answer)$",RegexOptions.IgnoreCase))return false;
  var prior=string.Join("\n",turns.Select(t=>t.User).Concat(turns.SelectMany(t=>t.Answer.Steps.Select(s=>s.Text))).Concat(turns.Where(t=>t.Answer.Prerequisite is not null).Select(t=>t.Answer.Prerequisite!.Condition)));
  if(prior.Contains(subject,StringComparison.OrdinalIgnoreCase))return false;
  if(subject.Equals("camera",StringComparison.OrdinalIgnoreCase)&&prior.Contains("webcam",StringComparison.OrdinalIgnoreCase))return false;
  return true;
 }
 public static bool IsGreeting(string text)=>Regex.IsMatch(text.Trim(),@"^(?:hey|hi|hello|hiya|howdy|good\s+(?:morning|afternoon|evening))[!?.\s]*$",RegexOptions.IgnoreCase);
 public static bool AmbiguousLegacyProgress(IReadOnlyList<(string User,Answer Answer)> turns,string latest)=>
  Regex.IsMatch(latest,@"\bstep\s*\d+\b",RegexOptions.IgnoreCase)&&
  !StartsNewIssue(latest)&&!Regex.IsMatch(latest,@"\b(?:for|about|regarding)\s+\w+",RegexOptions.IgnoreCase)&&
  ActiveTurns(turns).LastOrDefault(t=>t.Answer.Steps.Count>0).Answer?.Steps.Any(s=>Regex.Matches(s.Text,@"(?m)^\d+[.)]\s+").Count>1)==true;
 public static ConversationContext Context(IReadOnlyList<(string User,Answer Answer)> turns,string latest){
  turns=ActiveTurns(turns);
  var observation=Regex.Match(latest,@"^\s*(?:(?:an?|the|my|our)\s+)?(?<subject>[\p{L}][\p{L} -]{1,60}?)\s+(?:works?|fails?|shows?|displays?)\b",RegexOptions.IgnoreCase);
  var sourceObservation=observation.Success&&(turns.LastOrDefault().Answer?.Steps.Any(s=>s.Text.Contains(observation.Groups["subject"].Value,StringComparison.OrdinalIgnoreCase))==true||turns.LastOrDefault().Answer?.Prerequisite?.Condition.Contains(observation.Groups["subject"].Value,StringComparison.OrdinalIgnoreCase)==true);
  var scopeReply=turns.LastOrDefault().Answer?.Questions.Contains(SourceObservationQuestions.ApplicationScope)==true&&
   Regex.IsMatch(latest,@"^\s*(?:only one application|(?:the )?whole computer)\b",RegexOptions.IgnoreCase);
  if(StartsNewIssue(latest)||!scopeReply&&NamesDifferentSubject(turns,latest)){turns=[];scopeReply=false;}
  // User facts and reported outcomes are context only. Old assistant text is never evidence.
  var messages=turns.Select(t=>t.User).Append(latest).Where(t=>!IsGreeting(t)).ToArray();
  var facts=string.Join("\n",messages.Distinct());
  if(facts.Length>6000)facts=facts[..4000]+"\n"+facts[^2000..];
  var pending=turns.LastOrDefault().Answer?.Prerequisite is {Confirmed:null} prerequisite&&turns.LastOrDefault().Answer!.Questions.Contains(SourcePrerequisites.QuestionFor(prerequisite.Condition))?prerequisite:null;
  var prerequisiteReply=pending is not null&&(SourcePrerequisites.IsReply(latest)||SourcePrerequisites.LiteralSatisfied(pending.Condition,facts));
  var progress=turns.Any(t=>t.Answer.Steps.Count>0)&&(ReportedOutcome(latest)!=StepOutcome.Unknown||Regex.IsMatch(latest,@"\bdid(?:n't| not)\s+(?:work|help)|\bfail(?:s|ed|ing)?\b|\btried\b|\bnext\b|\balready\b|\bcould(?:n't| not)\b|\bstep\s*\d+\b|\bworks? now\b|\bnow works?\b|\b(?:it|that|this)\s+still\b|^(?:ok|okay|done)[!?.\s]*$",RegexOptions.IgnoreCase));
  var referencesPrior=Regex.IsMatch(latest,@"\b(?:it|that|this|those|same|still|already|current)\b|^\s*(?:actually|correction|to correct that)\b",RegexOptions.IgnoreCase);
  // A detailed clarification answer must remain related to the active reports,
  // selected source or question; an unrelated request cannot inherit its evidence.
  var shortClarification=turns.LastOrDefault().Answer?.Questions.Count>0&&!Regex.IsMatch(latest,@"^\s*(?:how|what|why|when|where|who)\b",RegexOptions.IgnoreCase)&&
   (KnowledgeKeywords.Query(latest).Split(' ',StringSplitOptions.RemoveEmptyEntries).Length<=3||scopeReply||SharesClarificationContext(turns,latest));
  var follows=turns.Count>0&&!StartsNewIssue(latest)&&(progress||referencesPrior||shortClarification||sourceObservation||prerequisiteReply);
  var issueId=follows?turns.LastOrDefault().Answer?.IssueId??Guid.NewGuid():Guid.NewGuid();
  if(!follows)facts=latest;
  var attempts=turns.Select((turn,index)=>new{suggested=turn.Answer.Steps.Select(s=>s.Text).ToArray(),reportedOutcome=turns.Skip(index+1).Select(t=>t.User).Append(latest).FirstOrDefault(t=>!IsGreeting(t))??"Outcome not reported"}).Where(t=>follows&&t.suggested.Length>0).TakeLast(3).ToArray();
  var previous=turns.SelectMany(t=>t.Answer.Steps.Select(s=>s.Text)).Distinct().TakeLast(16).ToArray();
  var activeSteps=follows?(turns.LastOrDefault(t=>t.Answer.Steps.Count>0).Answer?.Steps??[]):[];
  var currentProgress=activeSteps.LastOrDefault() is { } step?new StepProgress(step,ReportedOutcome(latest),latest):null;
  var model="User facts (untrusted; latest corrections supersede older facts):\n"+facts+"\nPrior suggestions and user-reported outcomes (context, not evidence or confirmed diagnoses):\n"+JsonSerializer.Serialize(attempts)+"\nCurrent source-bound step and reported outcome (a report is not a verified result; Completed does not mean successful):\n"+JsonSerializer.Serialize(currentProgress is null?null:new{currentProgress.Suggested,reportedOutcome=currentProgress.ReportedOutcome.ToString(),currentProgress.UserReport})+"\nPreviously displayed steps (not evidence; do not repeat unless the user asks to explain):\n"+JsonSerializer.Serialize(follows?previous:[])+"\nLatest user message: "+latest;
  // A new explicit task must not be embedded together with unrelated old topics.
  // Referencing a prior result or answering a short clarification retains user facts.
  var retrieval=follows?facts:Regex.Replace(latest,@"^\s*(?:(?:hey|hi|hello)[!,.:\s]+)?(?:new|different|another)\s+(?:issue|problem|question|topic)\s*[:,-]\s*","",RegexOptions.IgnoreCase);
  return new(retrieval,facts,model,follows?previous:[],follows&&progress){ActiveSourceSteps=activeSteps,CurrentProgress=currentProgress,IssueId=issueId,IsContinuation=follows,AwaitingClarification=follows&&turns.LastOrDefault().Answer?.Status=="clarify"&&!IsGreeting(turns.LastOrDefault().User??""),PendingPrerequisite=follows?pending:null,PrerequisiteStates=follows?turns.Where(t=>t.Answer.Prerequisite is not null).Select(t=>t.Answer.Prerequisite!).Reverse().DistinctBy(p=>(p.PassageId,p.Condition)).Where(p=>p.Confirmed!=true||SourcePrerequisites.RetainsConfirmation(latest)).ToArray():[],AnsweredApplicationScope=follows&&turns.Any(t=>t.Answer.Questions.Contains(SourceObservationQuestions.ApplicationScope))};
 }
 private static bool GenericDeviceTrouble(string question){
  var text=Regex.Replace(question.Trim(),@"^(?:hey|hi|hello)[!,.:\s]+","",RegexOptions.IgnoreCase);
  text=Regex.Replace(text,@"[.]\s*(?:Can|Could|Would) (?:you|u) help(?: me)?[?!.\s]*$","",RegexOptions.IgnoreCase);
  text=Regex.Replace(text,@"(?:\s+(?:please|pls|plz))?[!?.\s]*$","",RegexOptions.IgnoreCase);
  const string device=@"(?:(?:my|mi|the|a|this)\s+)?(?:work\s+)?(?:computer|pc|laptop|printer|webcam|camera|monitor|keyboard|mouse|network|internet|device)";
  if(Regex.IsMatch(text,@"^(?:(?:can|could|would) (?:you|u) sort out "+device+@"(?: for me)?|"+device+@"(?:['’]s been| has been| is) acting (?:up|weird)(?: (?:all (?:morning|day)|since (?:today|yesterday)))?|"+device+@" (?:has )?stopped working)$",RegexOptions.IgnoreCase))return true;
  return Regex.IsMatch(text,@"^(?:(?:i(?:['’]?m| am)?\s+)?(?:am\s+)?having\s+(?:trouble|problems?|issues?)\s+(?:with|on)\s+"+device+@"|(?:can|could|would) (?:you|u) help(?: me)?(?: with)?\s+"+device+@"|(?:i )?(?:need|want) help(?: with)?\s+"+device+@"|I have (?:an? )?(?:problem|issue) with\s+"+device+@"|"+device+@"\s+(?:is acting up|has\s+(?:an?\s+)?(?:issue|problem)))$",RegexOptions.IgnoreCase);
 }
 public static bool VagueInitial(string question,ConversationContext? context)=>
  context?.PreviousAdvice?.Count==0 && context.CurrentProgress is null &&
  (GenericDeviceTrouble(question)&&(!context.IsContinuation||context.ScopeQuestion==question)||
  question.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length<=12 &&
  Regex.IsMatch(question,@"doesn.t work|not working|won.t work|need help|is broken",RegexOptions.IgnoreCase)&&
  !Regex.IsMatch(question,@"\b(?:shows?|displays?|reports?|returns?)\s+\S+\s+\S+",RegexOptions.IgnoreCase)&&
  Regex.Matches(Regex.Split(question,@"doesn.t work|not working|won.t work|need help|is broken",RegexOptions.IgnoreCase)[0],@"\b[\w-]+\b").Count<5&&
  Regex.Matches(Regex.Split(question,@"doesn.t work|not working|won.t work|need help|is broken",RegexOptions.IgnoreCase).Last(),@"\b\w+\b").Count<2);
 private static bool SharesClarificationContext(IReadOnlyList<(string User,Answer Answer)> turns,string latest){
  var prior=turns.LastOrDefault();if(prior.Answer is null)return false;
  // Compare normalized content words, allowing ordinary inflection rather than
  // a list of device-specific response phrases.
  static string Stem(string word)=>Regex.Replace(word,@"(?:ing|ed|s)$","");
  var scopeQuestion=prior.Answer.Questions.Contains(SourceObservationQuestions.ApplicationScope)?SourceObservationQuestions.ApplicationScope:"";
  var words=KnowledgeKeywords.Query(prior.User+" "+scopeQuestion+" "+prior.Answer.Prerequisite?.Condition+" "+string.Join(" ",prior.Answer.Steps.Select(s=>s.Text))).Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(Stem).Where(w=>w.Length>=3).ToArray();
  return KnowledgeKeywords.Query(latest).Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(Stem).Where(w=>w.Length>=3).Any(w=>words.Any(p=>w.StartsWith(p[..Math.Min(4,p.Length)],StringComparison.Ordinal)||p.StartsWith(w[..Math.Min(4,w.Length)],StringComparison.Ordinal)));
 }
}
