using System.Text.RegularExpressions;
namespace Clary.Api;

public static class EpsonEvidence
{
    // This curated collection has model-specific titles. Future uploads need explicit model metadata.
    public static (bool NeedsModel,IReadOnlyList<Passage> Passages) Select(string question,IReadOnlyList<Passage> passages)
    {
        var matches=Regex.Matches(question,@"\b(?:tm[-\s]*)?(?:t)?(88iv|88v|20iii|20ii|m30)\b",RegexOptions.IgnoreCase)
            .Select(m=>m.Groups[1].Value.ToLowerInvariant()).Distinct().ToArray();
        if(matches.Length==0 && !Regex.IsMatch(question,@"\bepson\b|\btm[-\s]*(?:t\d|m\d)",RegexOptions.IgnoreCase))return(false,passages);
        if(matches.Length!=1)return(true,[]);
        var model=matches[0]=="m30"?"TM-m30":"TM-T"+matches[0].ToUpperInvariant();
        var usbContext=!Regex.IsMatch(question,@"\b(?:ethernet|serial|bluetooth)\b",RegexOptions.IgnoreCase)
            &&Regex.IsMatch(question,@"\busb\b|\bapd\b|\bspooler\b|\bwindows\b|\bport\b|\boffline\b|\bqueue\b|\b3006\b",RegexOptions.IgnoreCase);
        return(false,passages.Where(p=>(p.SupportedModels?.Contains(model,StringComparer.OrdinalIgnoreCase)??false)||p.Title.StartsWith("Epson "+model+" ",StringComparison.OrdinalIgnoreCase)||usbContext&&p.Title.StartsWith("Epson USB/APD — User-provided — ",StringComparison.Ordinal)).ToArray());
    }
    public static bool IsCurated(Passage passage)=>passage.Title.StartsWith("Epson ",StringComparison.Ordinal)&&passage.Title.EndsWith("— Curated Notes",StringComparison.Ordinal);
    public static bool IsProcedure(string text)=>text.StartsWith("For ",StringComparison.Ordinal)&&text.EndsWith(".",StringComparison.Ordinal);
}
