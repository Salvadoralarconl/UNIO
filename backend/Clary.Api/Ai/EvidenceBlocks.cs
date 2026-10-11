using System.Text.RegularExpressions;
namespace Clary.Api;

// Markdown headings do not require a blank line before their body. Keep source
// offsets and exact text while separating headings from selectable instructions.
public static class EvidenceBlocks
{
    public static Match[] Read(string text)=>Regex.Matches(text,
        @"(?m)^#{1,6}[ \t]+[^\r\n]+|\S[\s\S]*?(?=\r?\n[ \t]*\r?\n|\r?\n#{1,6}[ \t]+|\z)").ToArray();
}
