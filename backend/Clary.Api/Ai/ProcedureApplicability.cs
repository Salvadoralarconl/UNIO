using System.Text.RegularExpressions;
namespace Clary.Api;
public static class ProcedureApplicability
{
    public static bool MissingWindowsPrinter(string question)=>Regex.IsMatch(question,@"not appear|doesn.t appear|missing|not showing|doesn.t show|not show|cannot find|can.t find|\bno\s+(?:epson\s+)?printer\s+(?:appears?|exists?|shows?)",RegexOptions.IgnoreCase)
        &&Regex.IsMatch(question,@"\bwindows\b|\bsettings\b",RegexOptions.IgnoreCase)
        &&Regex.IsMatch(question,@"\busb\b|\bapd\b",RegexOptions.IgnoreCase);
    public static IReadOnlyList<Passage> Select(string question,IReadOnlyList<Passage> passages) {
        if(!MissingWindowsPrinter(question))return passages;
        // Queue procedures require a visible printer/queue. Do not offer them as a missing-device fix.
        return passages.Where(p=>!Regex.IsMatch(p.Text,@"Print Spooler|Print a Test Page",RegexOptions.IgnoreCase)).ToArray();
    }
    public static bool IsValid(string question,Answer answer)=>!MissingWindowsPrinter(question)
        ||answer.Steps.All(s=>!Regex.IsMatch(s.Text,@"\bspooler\b|print(?: a)? test page|\bpaused?\b|\boffline\b|stuck print jobs|cancel.{0,30}print jobs|clear(?: the)? print queue",RegexOptions.IgnoreCase));
}
