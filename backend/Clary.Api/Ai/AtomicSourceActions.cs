using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clary.Api;

// Display one source item while retaining its complete original procedure as evidence.
// Conditions/warnings are source text; the application never generates instructions.
public static class AtomicSourceActions
{
 public sealed record Option(string Reference,string Text,Guid PassageId,string EvidenceQuote);
 private static Match[] WarningSections(string text)=>Regex.Matches(text,@"(?m)^#{1,6}[ \t]+(?:Warnings?|Important|Cautions?)\b[^\r\n]*(?:\r?\n(?!#{1,6}[ \t])[^\r\n]*)*",RegexOptions.IgnoreCase).Cast<Match>().ToArray();
 public static bool IsUnsegmented(string text)=>!Regex.IsMatch(text,@"(?m)^\s*\d+[.)][ \t]+")&&Regex.IsMatch(text,@"[.!?][ \t\r\n]+(?=[A-Za-z])|;[ \t]+\w");
 public static IReadOnlyList<Option> Options(IReadOnlyList<Passage> passages)
 {
  var choices=new List<(string Text,Guid PassageId,string Quote)>();
  foreach(var parent in CitationReferences.ProcedureOptions(passages)){
   var source=passages.Single(p=>p.Id==parent.PassageId);
   if(WarningSections(source.Text).Any(w=>w.Value.Contains(parent.Text,StringComparison.Ordinal)))continue;
   var numbers=Regex.Matches(parent.Text,@"(?m)^(?<indent>[ \t]*)\d+[.)][ \t]+").ToArray();
   if(numbers.Length==0){if(!IsUnsegmented(parent.Text))choices.Add((parent.Text,parent.PassageId,parent.Text));continue;}
   var indentation=numbers.Min(m=>m.Groups["indent"].Length);
   numbers=numbers.Where(m=>m.Groups["indent"].Length==indentation).ToArray();
   var prefix=parent.Text[..numbers[0].Index].Trim();
   var warningCandidates=EvidenceBlocks.Read(parent.Text).Where(m=>Regex.IsMatch(m.Value.Trim(),@"^(?:\*\*(?:Warning|Important|Caution|Never)|Do not\b|Never\b|Warning:|Important:|Caution:)",RegexOptions.IgnoreCase))
    .Concat(WarningSections(parent.Text)).Where(m=>m.Index>numbers[0].Index).ToArray();
   var warnings=warningCandidates.Where(m=>!warningCandidates.Any(other=>other.Index<=m.Index&&other.Index+other.Length>=m.Index+m.Length&&other.Length>m.Length)).DistinctBy(m=>m.Index).OrderBy(m=>m.Index).ToArray();
   for(var index=0;index<numbers.Length;index++){
    var start=numbers[index].Index+numbers[index].Length;
    var end=index+1<numbers.Length?numbers[index+1].Index:parent.Text.Length;
    var warning=warnings.FirstOrDefault(w=>w.Index>=start&&w.Index<end);if(warning is not null)end=warning.Index;
    var body=parent.Text[start..end].Trim();
    if(!CitationSupport.IsSubstantive(body))continue;
    var fragments=new[]{prefix,body}.Concat(warnings.Select(w=>w.Value.Trim())).Where(s=>s.Length>0).Distinct(StringComparer.Ordinal);
    choices.Add((string.Join("\n\n",fragments),parent.PassageId,parent.Text));
   }
  }
  var bound=choices.Select(c=>{
   var source=passages.Single(p=>p.Id==c.PassageId);var warnings=WarningSections(source.Text).Select(w=>w.Value.Trim()).ToArray();
   var missing=warnings.Where(w=>!c.Text.Contains(w,StringComparison.Ordinal)).ToArray();
   return (Text:string.Join("\n\n",new[]{c.Text}.Concat(missing)),c.PassageId,Quote:warnings.Any(w=>!c.Quote.Contains(w,StringComparison.Ordinal))?source.Text:c.Quote);
  });
  return bound.DistinctBy(c=>(c.PassageId,c.Text)).Select((c,index)=>new Option("@action:0:"+index,c.Text,c.PassageId,c.Quote)).ToArray();
 }
 public static JsonElement Schema(IReadOnlyList<Passage> passages,IReadOnlyList<Option>? allowed=null)
 {
  var schema=JsonNode.Parse(CitationReferences.ProcedureSchema(passages).GetRawText())!;
  var references=(allowed??Options(passages)).Select(o=>o.Reference).ToArray();
  if(references.Length>0)schema["properties"]!["steps"]!["items"]!["properties"]!["text"]=JsonSerializer.SerializeToNode(new{type="string",@enum=references});
  return JsonSerializer.SerializeToElement(schema);
 }
 public static Answer Expand(Answer answer,IReadOnlyList<Passage> passages)
 {
  var options=Options(passages).ToDictionary(o=>o.Reference);
  var steps=answer.Steps.Select(step=>{
   if(step.Citations.Count!=0||!options.TryGetValue(step.Text,out var option))throw new InvalidOperationException(OllamaAnswerProvider.Unreadable);
   return step with{Text=option.Text,Citations=[new Citation(option.PassageId,option.EvidenceQuote)]};
  }).DistinctBy(step=>step.Text).ToArray();
  return answer with{Steps=steps};
 }
 public static bool IsExactBoundStep(AdviceStep step,IReadOnlyList<Passage> passages)=>step.Citations.Count==1&&Options(passages).Any(option=>option.Text==step.Text&&option.PassageId==step.Citations[0].PassageId&&option.EvidenceQuote==step.Citations[0].Quote);
}
