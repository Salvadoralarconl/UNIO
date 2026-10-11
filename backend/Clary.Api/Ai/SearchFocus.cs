using System.Text.RegularExpressions;
namespace Clary.Api;
public static class SearchFocus {
    public static string RequestedTask(string question){
        // A clarification reply describes the active issue; it is not a replacement
        // task about a generic word such as "browser". BrieConversation already
        // limits these user facts to the current issue and excludes assistant advice.
        const string factsMarker="User facts (untrusted; latest corrections supersede older facts):\n";
        const string adviceMarker="\nPrior suggestions and user-reported outcomes (context, not evidence or confirmed diagnoses):\n";
        if(question.StartsWith(factsMarker,StringComparison.Ordinal)){
            var end=question.IndexOf(adviceMarker,factsMarker.Length,StringComparison.Ordinal);
            if(end>factsMarker.Length){
                var facts=question[factsMarker.Length..end];
                return Apply(facts,facts);
            }
        }
        const string marker="Latest user message: ";var at=question.LastIndexOf(marker,StringComparison.Ordinal);
        var latest=at>=0?question[(at+marker.Length)..]:question;
        var progress=latest.IndexOf("\nUser reports the problem persists.",StringComparison.Ordinal);if(progress>=0)latest=latest[..progress];
        // Replies supplying the requested model or connection complete the earlier
        // troubleshooting question; they are not a new task about the word "USB".
        var deviceDetail=Regex.IsMatch(latest,@"\b(?:usb|ethernet|serial|bluetooth|(?:tm[-\s]*)?t?(?:88iv|88v|20iii|20ii|m30))\b",RegexOptions.IgnoreCase)
            &&!Regex.IsMatch(latest,@"\b(?:how|what|why|when|not|error|jam|missing|fails?|wrong|nothing|offline|paused|blank|faint|slow|print\w*)\b",RegexOptions.IgnoreCase);
        if(at>=0&&deviceDetail){
            var prior=Regex.Matches(question[..at],@"(?m)^User: (.+)$").Select(m=>m.Groups[1].Value)
                .LastOrDefault(s=>Regex.IsMatch(s,@"\b(?:how|what|why|when|not|error|jam|missing|fails?|wrong|nothing|offline|paused|blank|faint|slow|print\w*)\b",RegexOptions.IgnoreCase));
            if(prior is not null)latest=prior;
        }
        return Apply(latest,latest);
    }
    public static string Apply(string query,string scope) {
        // Device identity stays in scope for model applicability, but a repeated
        // model/connection preamble can dominate embeddings over the actual issue.
        var focused=Regex.Replace(query,@"(?im)^\s*(?:my\s+)?(?:epson\s+)?(?:tm[-\s]*)?t?(?:88iv|88v|20iii|20ii|m30)\b(?:\s+is)?\s+(?:connected\s+(?:by|through|via)\s+)?usb\b[.\s]*","",RegexOptions.IgnoreCase).Trim();
        if(!string.IsNullOrWhiteSpace(focused)&&focused!=query.Trim())
            query=Regex.IsMatch(scope,@"\bepson\b|\b(?:tm[-\s]*)?t?(?:88iv|88v|20iii|20ii|m30)\b",RegexOptions.IgnoreCase)?"Epson receipt printer: "+focused:focused;

        if(ProcedureApplicability.MissingWindowsPrinter(scope))
            return query+"\nSearch focus: missing Windows printer; USB device detection in Device Manager; Epson APD registration.";
        bool Has(string pattern)=>Regex.IsMatch(scope,pattern,RegexOptions.IgnoreCase);
        if(Has(@"\bwindows\b")&&Has(@"\busb\b")&&Has(@"\b(?:tm[-\s]*)?t?(?:88iv|88v|20iii|20ii|m30)\b"))
            return query+"\nSearch focus: Windows USB printing; printer driver communication; APD port configuration.";
        return query;
    }
}
