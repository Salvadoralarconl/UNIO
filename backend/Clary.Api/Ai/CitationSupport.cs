using System.Text.RegularExpressions;
namespace Clary.Api;
public static class CitationSupport
{
    // A section label or conditional introduction alone cannot justify a fix.
    public static bool IsSubstantive(string quote) {
        var text=quote.Trim();
        return text.Length>0 && !Regex.IsMatch(text,@"^[^\r\n]+:$") && !Regex.IsMatch(text,@"^\d+\.\s+[^.!?\r\n]+$");
    }
}
