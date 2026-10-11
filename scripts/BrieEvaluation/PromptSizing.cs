using Clary.Api;
using System.Text.Json;

// Offline experiment only; does not change the deployed model prompt.
internal static class PromptSizing
{
 public static void Run()
 {
  using var manifest=JsonDocument.Parse(File.ReadAllText("docs/it-training-test/manifest.json"));
  var passages=manifest.RootElement.EnumerateArray().Where(d=>d.GetProperty("title").GetString()!.Contains("Webcam",StringComparison.OrdinalIgnoreCase)||d.GetProperty("title").GetString()!.Contains("Camera works",StringComparison.OrdinalIgnoreCase)).Select(d=>new Passage(Guid.NewGuid(),Guid.NewGuid(),d.GetProperty("title").GetString()!,"1","Full document",d.GetProperty("text").GetString()!,null)).ToArray();
  var options=CitationReferences.ProcedureOptions(passages);
  var original=JsonSerializer.Serialize(passages.Select(p=>new{p.Id,p.Title,sourceText=p.Text,citationOptions=options.Where(o=>o.PassageId==p.Id).Select(o=>new{o.Reference,o.Text})}));
  var candidate=JsonSerializer.Serialize(passages.Select(p=>{
   var choices=options.Where(o=>o.PassageId==p.Id).ToArray();
   var events=new List<(int Position,string Marker)>();
   foreach(var choice in choices){var index=p.Text.IndexOf(choice.Text,StringComparison.Ordinal);if(index<0)throw new InvalidOperationException("An exact option was not present in its source.");events.Add((index,"\n[Selectable guidance "+choice.Reference+"]\n"));events.Add((index+choice.Text.Length,"\n[End selectable guidance "+choice.Reference+"]\n"));}
   var annotated=p.Text;foreach(var item in events.OrderByDescending(e=>e.Position))annotated=annotated.Insert(item.Position,item.Marker);
   return new{p.Id,p.Title,sourceText=annotated,citationOptions=choices.Select(o=>o.Reference)};
  }));
  var result=new{passages=passages.Length,choices=options.Count,originalCharacters=original.Length,candidateCharacters=candidate.Length,reductionPercent=Math.Round(100.0*(original.Length-candidate.Length)/original.Length,1),sourceContentRemoved=false,deployed=false};
  Console.WriteLine(JsonSerializer.Serialize(result));File.WriteAllText("artifacts/brie-prompt-size-experiment.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
 }
}
