using System.Text.RegularExpressions;
namespace Clary.Api;
public static class ConversationProgress {
    private static readonly HashSet<string> Stop=["the","a","an","and","or","to","of","is","it","that","this","if","then","both","be","its","your","please","try"];
    private static HashSet<string> Tokens(string text)=>Regex.Matches(text.ToLowerInvariant(),@"[a-z0-9]+").Select(m=>m.Value switch {
        "check" or "verify" or "confirm" or "ensure"=>"check",
        "properly" or "securely" or "secure"=>"secure",
        "connected" or "connection" or "connect"=>"connect",
        "printers"=>"printer",_=>m.Value
    }).Where(w=>!Stop.Contains(w)).ToHashSet();
    // A conservative wording heuristic, not a general semantic-equivalence proof.
    public static bool RepeatsChecklist(IReadOnlyList<string> proposed,IReadOnlyList<string> previous) {
        if(proposed.Count==0||previous.Count==0)return false;
        var old=previous.Select(Tokens).ToArray();
        var repeated=proposed.Count(text=>{
            var words=Tokens(text);if(words.Count==0)return false;
            return old.Any(p=>words.Intersect(p).Count()/(double)words.Union(p).Count()>=.55);
        });
        // One new item must not make a mostly recycled checklist count as progress.
        return repeated>=Math.Ceiling(proposed.Count*.5);
    }
}
