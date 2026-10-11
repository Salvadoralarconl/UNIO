using System.Text.RegularExpressions;
namespace Clary.Api;
// Only explicit source predicates or a reply to a saved source-bound question
// establish a prerequisite. A model's relevance judgment cannot establish it.
public static class SourcePrerequisites {
 public const string Question="Does the condition quoted from the approved source apply to what you observed? Please answer yes, no, or tell me what you observed.";
 public const string CompletionPrefix="Completion of the source prerequisite: ";
 public static string QuestionFor(string condition)=>condition.Contains(CompletionPrefix,StringComparison.Ordinal)?"Have you completed the prerequisite quoted from the approved source? Tell me what was completed, or say no or not sure.":Question;
 // Explicit cross-item ordering is source authority, not a model relevance judgment.
 // Match the named activity only; an independent later emergency branch stays available.
 public static string? OrderedCondition(AdviceStep step){
  if(step.Citations.Count!=1)return null;
  var blocks=Regex.Matches(step.Citations[0].Quote,@"(?m)^\s*\d+[.)][ \t]+(?<body>[\s\S]*?)(?=^\s*\d+[.)][ \t]+|\z)").Select(m=>m.Groups["body"].Value.Trim()).ToArray();
  var selected=Array.FindIndex(blocks,b=>step.Text.Contains(b,StringComparison.Ordinal));
  if(selected<0)return null;
  static string Stem(string word)=>Regex.Replace(word.ToLowerInvariant(),@"(?:ing|ments?|s)$","");
  static string[] Words(string text)=>Regex.Matches(text,@"[\p{L}]+").Select(m=>Stem(m.Value)).Where(w=>w.Length>=4&&w is not ("before" or "after" or "approved" or "process" or "following" or "complete")).Distinct().ToArray();
  var selectedAction=Regex.Replace(blocks[selected],@"^(?:If|When|Once|Provided that|Only if)\s+[^\r\n,:]+[,:]\s*","",RegexOptions.IgnoreCase);
  var activities=Words(selectedAction);var requirements=new List<string>();
  foreach(var earlier in blocks.Take(selected)){
   foreach(Match sentence in Regex.Matches(earlier,@"(?:^|(?<=[.!?])\s+)Before\s+(?<activity>[^,:.!?\r\n]+)[,:]\s*(?<required>[^.!?\r\n]+)",RegexOptions.IgnoreCase)){
    if(Words(sentence.Groups["activity"].Value).Intersect(activities,StringComparer.Ordinal).Any())requirements.Add(CompletionPrefix+sentence.Groups["required"].Value.Trim());
   }
   foreach(Match sentence in Regex.Matches(earlier,@"(?:^|(?<=[.!?])\s+)(?<required>[^.!?\r\n]+?)\s+before\s+(?<activity>[^.!?\r\n]+)",RegexOptions.IgnoreCase)){
    if(Words(sentence.Groups["activity"].Value).Intersect(activities,StringComparer.Ordinal).Any())requirements.Add(CompletionPrefix+sentence.Groups["required"].Value.Trim());
   }
  }
  return requirements.Count==0?null:string.Join(" and ",requirements.Distinct(StringComparer.Ordinal));
 }
 public static string? EffectiveCondition(AdviceStep step){var conditional=Condition(step);if(conditional is null&&HasConditionalLanguage(step))return null;var ordered=OrderedCondition(step);return conditional is null?ordered:ordered is null?conditional:conditional+" and "+ordered;}
 public static bool CanObserve(string condition,string facts){
  if(!condition.Contains(CompletionPrefix,StringComparison.Ordinal))return true;
  var latest=facts.Split('\n',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"";
  return Regex.IsMatch(latest,@"\b(?:completed?|finished|performed|verified|done)\b",RegexOptions.IgnoreCase)&&
   !Regex.IsMatch(latest,@"\b(?:not|never|cannot|can't|couldn't|don't|didn't|haven't|isn't|wasn't|unsure|unknown|maybe|possibly|might|if|whether|earlier|previously|yesterday|says|said)\b|\?|[""“”]|'[^'\r\n]+'",RegexOptions.IgnoreCase);
 }
 public static bool? Confirmation(string message)=>Regex.IsMatch(message.Trim(),@"^(?:yes|yes,? (?:it does|that applies|that is what I observed)|confirmed)[.!\s]*$",RegexOptions.IgnoreCase)?true:Regex.IsMatch(message.Trim(),@"^(?:no|no,? (?:it does not|that does not apply|that doesn't apply))[.!\s]*$",RegexOptions.IgnoreCase)?false:null;
 public static bool IsReply(string message)=>Confirmation(message) is not null||Regex.IsMatch(message.Trim(),@"^(?:(?:yes|no),?\s+(?:but\s+)?(?:I\s+(?:am\s+)?)?)?(?:not sure|unsure|I don't know|I do not know|unknown)[.!\s]*$",RegexOptions.IgnoreCase);
 public static bool RetainsConfirmation(string message)=>Regex.IsMatch(message.Trim(),@"^(?:what(?: is)? next|next|(?:please )?repeat (?:that|the step)|(?:please )?explain (?:that|the current step))[.!?\s]*$",RegexOptions.IgnoreCase);
 public static string? Condition(AdviceStep step){
  var matches=Regex.Matches(step.Text,@"(?m)^(?:If|When|Once|Provided that|Only if)\s+([^\r\n,:]+)[,:]",RegexOptions.IgnoreCase).ToArray();
  if(matches.Any(m=>Regex.IsMatch(step.Text[(m.Index+m.Length)..],@"^\s*(?:and|but|while|if|when)\b",RegexOptions.IgnoreCase)))return null;
  var clauses=matches.Select(m=>m.Groups[1].Value.Trim()).ToArray();
  if(Regex.Matches(step.Text,@"\b(?:if|when|once|unless|provided that)\b",RegexOptions.IgnoreCase).Any(marker=>!matches.Any(m=>marker.Index>=m.Index&&marker.Index<m.Index+m.Length)))return null;
  if(clauses.Length>0)return string.Join(" and ",clauses);
  return null;
 }
 public static bool HasConditionalLanguage(AdviceStep step)=>Regex.IsMatch(step.Text,@"\b(?:if|when|once|unless|provided that)\b",RegexOptions.IgnoreCase);
 static string Normalize(string value)=>Regex.Replace(Regex.Replace(value.ToLowerInvariant().Replace("can't","cannot").Replace("doesn't","does not").Replace("isn't","is not"),@"[^\p{L}\p{N}]+"," "),@"\b(?:a|an|the|my|our|is|are|was|were)\b","").Split(' ',StringSplitOptions.RemoveEmptyEntries) is {} words?string.Join(" ",words):"";
 public static bool LiteralSatisfied(string condition,string facts){
  var clauses=Regex.Split(condition,@"\s+(?:and|while|but)\s+",RegexOptions.IgnoreCase);
  var observations=Regex.Split(facts,@"[\r\n.!;]+",RegexOptions.None).Where(s=>s.Length>0).ToArray();
  if(observations.LastOrDefault() is {} latestReply&&Regex.IsMatch(latestReply,@"\b(?:not sure|unsure|don't know|do not know|unknown)\b",RegexOptions.IgnoreCase))return false;
  return clauses.All(clause=>{
   var predicate=Normalize(clause);if(predicate.Split(' ').Length<2)return false;
   var subject=string.Join(" ",predicate.Split(' ').Take(2));
   var latest=observations.LastOrDefault(s=>Normalize(s).Contains(subject,StringComparison.Ordinal));
   if(latest is null||latest.Contains('?')||Regex.IsMatch(latest,@"\b(?:if|when|whether|maybe|possibly|might|unsure|not sure|don't know|do not know|used to|yesterday|previously|earlier)\b|[""“”]|'[^'\r\n]+'",RegexOptions.IgnoreCase))return false;
   var normalized=Normalize(latest);
   if(Regex.IsMatch(normalized,@"\b(?:not|never|cannot)\b")&&!Regex.IsMatch(predicate,@"\b(?:not|never|cannot)\b"))return false;
   if(Regex.Matches(normalized,@"\b(?:not|never|cannot)\b").Count!=Regex.Matches(predicate,@"\b(?:not|never|cannot)\b").Count)return false;
   return Regex.IsMatch(normalized,@"(?:^| )"+Regex.Escape(predicate)+@"(?: (?:normally|currently|now))?$",RegexOptions.CultureInvariant);
  });
 }
 public static bool Satisfied(string condition,ConversationContext? context,string facts){
  // This observation is already collected by the existing trusted scope question.
  var scopeObservation=facts.Split('\n',StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
  if(scopeObservation is not null&&!SourceObservationQuestions.HasApplicationScope(scopeObservation))scopeObservation=null;
  if(context?.AnsweredApplicationScope==true&&scopeObservation is not null){
   var normalized=Normalize(condition);
   if(normalized=="only one application slow"&&!Regex.IsMatch(scopeObservation,@"\bwhole computer\b",RegexOptions.IgnoreCase))return true;
   if(normalized=="whole computer slow"&&Regex.IsMatch(scopeObservation,@"\bwhole computer\s+(?:is|runs|feels) slow\b",RegexOptions.IgnoreCase))return true;
  }
  return LiteralSatisfied(condition,facts);
 }
}
