using System.Text.Json;
using System.Text.Json.Nodes;
namespace Clary.Api;
// A compact wire reference is expanded only against the passages retrieved for this request.
// This does not repair invented quotes or retrieve arbitrary IDs supplied by the model.
public static class CitationReferences
{
    public const string FullPassage="@retrieved-passage";
    public sealed record Option(string Reference,string Text);
    // Reuse the existing quote-selection rules; compact mode changes the wire format only.
    public static IReadOnlyList<Option> Options(Passage passage) {
        var schema=GroundedAnswerContract.SchemaFor([passage]);
        var citation=schema.GetProperty("$defs").GetProperty("citation");
        if(!citation.TryGetProperty("anyOf",out var alternatives))return [];
        return alternatives[0].GetProperty("properties").GetProperty("quote").GetProperty("enum").EnumerateArray()
            .Select((q,i)=>q.GetString()==FullPassage?new Option(FullPassage,passage.Text):new Option("@quote:"+i,q.GetString()!)).ToArray();
    }
    public static JsonElement CompactSchema(IReadOnlyList<Passage> passages) {
        var schema=JsonNode.Parse(GroundedAnswerContract.SchemaFor(passages).GetRawText())!;
        if(schema["$defs"]!["citation"]!["anyOf"] is JsonArray alternatives)
            foreach(var alternative in alternatives) {
                var id=alternative!["properties"]!["passageId"]!["enum"]![0]!.GetValue<string>();
                var passage=passages.Single(p=>p.Id.ToString()==id);
                alternative["properties"]!["quote"]!["enum"]=JsonSerializer.SerializeToNode(Options(passage).Select(o=>o.Reference));
            }
        return JsonSerializer.SerializeToElement(schema);
    }
    public sealed record ProcedureOption(string Reference,string Text,Guid PassageId);
    public static IReadOnlyList<ProcedureOption> ProcedureOptions(IReadOnlyList<Passage> passages)=>passages.SelectMany((p,source)=>{
        // Uploaded evidence uses compact full-passage quotes on the wire. Procedure
        // selection still needs its exact paragraph blocks, just like wiki evidence.
        var options=Options(p).Concat(EvidenceBlocks.Read(p.Text)
            .Where(m=>!EpsonEvidence.IsCurated(p)||EpsonEvidence.IsProcedure(m.Value.Trim()))
            .Select((m,i)=>new Option("@block:"+i,m.Value.Trim()))).ToArray();
        var parts=EvidenceBlocks.Read(p.Text).Select(m=>m.Value).ToArray();
        // Overlapping extracted chunks start inside the preceding paragraph.
        // That leading fragment cannot be offered as a complete instruction.
        var overlap=System.Text.RegularExpressions.Regex.IsMatch(p.Location,@"characters [1-9]\d*[–-]");
        var blocks=parts.Skip(overlap?1:0).Select(s=>s.Trim()).ToHashSet(StringComparer.Ordinal);
        var spans=new List<Option>();
        var matches=EvidenceBlocks.Read(p.Text);
        for(var i=overlap?1:0;i<matches.Length;i++){
            if(!System.Text.RegularExpressions.Regex.IsMatch(matches[i].Value,@"^1[.)]\s"))continue;
            var end=i;
            while(end+1<matches.Length&&(matches[end+1].Value.StartsWith('`')||System.Text.RegularExpressions.Regex.IsMatch(matches[end+1].Value,@"^(?:[2-9]|[1-9]\d+)[.)]\s")))end++;
            var start=i;
            if(i>0&&System.Text.RegularExpressions.Regex.IsMatch(matches[i-1].Value,@"^(?:If|When|Before|After|Only|Run)\b[\s\S]*:$"))start--;
            while(start>0&&System.Text.RegularExpressions.Regex.IsMatch(matches[start-1].Value,@"^(?:\*\*(?:Warning|Important|Caution|Never)|Do not\b|Never\b|Warning:|Important:|Caution:)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))start--;
            while(end+1<matches.Length&&System.Text.RegularExpressions.Regex.IsMatch(matches[end+1].Value,@"^(?:\*\*(?:Warning|Important|Caution|Never)|Do not\b|Never\b|Warning:|Important:|Caution:)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))end++;
            if(end+1<matches.Length&&System.Text.RegularExpressions.Regex.IsMatch(matches[end+1].Value,@"^#{1,6}\s+(?:Warnings?|Important|Cautions?)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){
                end++;
                while(end+1<matches.Length&&!matches[end+1].Value.StartsWith('#'))end++;
            }
            if(end>i||start<i){var finish=matches[end].Index+matches[end].Length;spans.Add(new("@span:"+i,p.Text[matches[start].Index..finish]));}
        }
        var paragraphs=options.Where(o=>blocks.Contains(o.Text)&&CitationSupport.IsSubstantive(o.Text)&&!o.Text.StartsWith('#')
            &&!o.Text.StartsWith('-')&&!o.Text.StartsWith('|')&&!o.Text.StartsWith('`')
            &&!System.Text.RegularExpressions.Regex.IsMatch(o.Text,@"^(?:[2-9]|[1-9]\d+)[.)]\s")
            &&!(System.Text.RegularExpressions.Regex.IsMatch(o.Text,@"^1[.)]")&&o.Text.EndsWith(':')))
            .Concat(spans).Where(o=>!spans.Any(s=>s.Text.Length>o.Text.Length&&s.Text.Contains(o.Text,StringComparison.Ordinal)))
            .Where(o=>!IsContextListing(p,o))
            .Select(o=>BindCondition(p,o))
            .Where(o=>!System.Text.RegularExpressions.Regex.IsMatch(p.Location,@"characters \d+[–-]")
                ||!p.Text.TrimEnd().EndsWith(o.Text.TrimEnd(),StringComparison.Ordinal)
                ||!System.Text.RegularExpressions.Regex.IsMatch(o.Text.TrimEnd(),@"(?:\A|\n)\d+[.)]\s[^\r\n]*\z"))
            .DistinctBy(o=>o.Text).ToArray();
        // Preserve complete numbered procedures instead of offering their individual
        // sentences, which can omit prerequisite or safety steps.
        var structured=parts.Any(s=>System.Text.RegularExpressions.Regex.IsMatch(s.Trim(),@"^#|^\d+[.)]\s"));
        var choices=paragraphs.Length>0?paragraphs:overlap||structured?[]:options.Where(o=>CitationSupport.IsSubstantive(o.Text)).ToArray();
        return choices.Select((option,index)=>new ProcedureOption($"@action:{source}:{index}",option.Text,p.Id));
    }).ToArray();
    private static bool IsContextListing(Passage passage,Option option){
        var index=passage.Text.IndexOf(option.Text,StringComparison.Ordinal);if(index<=0)return false;
        var heading=System.Text.RegularExpressions.Regex.Matches(passage.Text[..index],@"(?m)^#{1,6}\s+([^\r\n]+)").LastOrDefault();
        return heading is not null&&System.Text.RegularExpressions.Regex.IsMatch(heading.Groups[1].Value,@"^(?:Symptoms?|Possible Causes?|Purpose|Scope|Applicability|Background|Basis|References?)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    private static Option BindCondition(Passage passage,Option option){
        var index=passage.Text.IndexOf(option.Text,StringComparison.Ordinal);if(index<=0)return option;
        var headings=System.Text.RegularExpressions.Regex.Matches(passage.Text[..index],@"(?m)^#{1,6}\s+([^\r\n]+)");
        var heading=headings.LastOrDefault();
        if(heading is not null&&System.Text.RegularExpressions.Regex.IsMatch(heading.Groups[1].Value,@"^(?:If|When|Before|After|Only)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return option with{Text=passage.Text[heading.Index..(index+option.Text.Length)]};
        var previous=EvidenceBlocks.Read(passage.Text[..index]).LastOrDefault();
        return previous is not null&&System.Text.RegularExpressions.Regex.IsMatch(previous.Value,@"^(?:If|When|Before|After|Only)\b[\s\S]*:$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            ?option with{Text=passage.Text[previous.Index..(index+option.Text.Length)]}:option;
    }
    public static JsonElement ProcedureSchema(IReadOnlyList<Passage> passages) {
        var schema=JsonNode.Parse(CompactSchema(passages).GetRawText())!;
        var references=ProcedureOptions(passages).Select(o=>o.Reference).ToArray();
        if(references.Length>0)schema["properties"]!["steps"]!["items"]!["properties"]!["text"]=JsonSerializer.SerializeToNode(new{type="string",@enum=references});
        // The application binds each selection to its source; the model cannot
        // accidentally attach the reference index from a different passage.
        schema["properties"]!["steps"]!["items"]!["properties"]!["citations"]!["minItems"]=0;
        schema["properties"]!["steps"]!["items"]!["properties"]!["citations"]!["maxItems"]=0;
        schema["properties"]!["citations"]!["maxItems"]=0;
        // Empty citation arrays cannot use these alternatives. Removing their unused
        // grammar keeps model selection compact; expansion still validates every ID.
        schema["properties"]!["steps"]!["items"]!["properties"]!["citations"]!["items"]=new JsonObject{["type"]="object"};
        schema["properties"]!["citations"]!["items"]=new JsonObject{["type"]="object"};
        schema.AsObject().Remove("$defs");
        return JsonSerializer.SerializeToElement(schema);
    }
    public static Answer ExpandProcedures(Answer answer,IReadOnlyList<Passage> passages) {
        var options=ProcedureOptions(passages).ToDictionary(o=>o.Reference);
        var steps=answer.Steps.Select(step=>{
            if(step.Citations.Count!=0||!options.TryGetValue(step.Text,out var option))throw new InvalidOperationException(OllamaAnswerProvider.Unreadable);
            return step with{Text=option.Text,Citations=[new Citation(option.PassageId,option.Text)]};
        }).ToArray();
        var unique=steps.Where((step,i)=>!steps.Take(i).Any(other=>other.Text==step.Text)
            &&!steps.Any(other=>other.Text.Length>step.Text.Length&&other.Text.Contains(step.Text,StringComparison.Ordinal))).ToArray();
        return answer with{Steps=unique};
    }
    public static Answer Expand(Answer answer,IReadOnlyList<Passage> passages) {
        var options=passages.ToDictionary(p=>p.Id,p=>Options(p));
        Citation Resolve(Citation citation) {
            if(citation is null)return citation!;
            var match=options.TryGetValue(citation.PassageId,out var choices)?choices.FirstOrDefault(o=>o.Reference==citation.Quote):null;
            return match is null?citation:citation with{Quote=match.Text};
        }
        return answer with{Citations=answer.Citations?.Select(Resolve).ToArray()!,Steps=answer.Steps?.Select(s=>s with{Citations=s.Citations?.Select(Resolve).ToArray()!}).ToArray()!};
    }
}
