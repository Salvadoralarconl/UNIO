namespace Clary.Api;

public sealed record TextChunk(int Start, string Text);
public static class Chunker
{
    public static IReadOnlyList<TextChunk> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var chunks = new List<TextChunk>();
        for (var start = 0; start < text.Length;)
        {
            var end = Math.Min(start + 1200, text.Length);
            if (end < text.Length)
            {
                var newline = text.LastIndexOf('\n', end - 1, end - start);
                if (newline > start + 600) end = newline + 1;
            }
            chunks.Add(new(start, text[start..end]));
            if (end == text.Length) break;
            start = end - 160;
        }
        return chunks;
    }
}
