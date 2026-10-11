using System.Text.Json;

namespace Clary.Api;
public sealed class OpenAiAnswerProvider(HttpClient client, string model) : IAnswerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<Answer> GenerateAsync(string question, IReadOnlyList<Passage> passages, CancellationToken ct)
    {
        var evidence = passages.Select(p => new { passageId = p.Id, p.Title, p.Version, p.Location, content = p.Text });
        using var json = await OpenAiHttp.PostAsync(client, "responses", new {
            model, store = false, instructions = GroundedAnswerContract.Instructions,
            input = JsonSerializer.Serialize(new { question, untrustedEvidence = evidence }),
            max_output_tokens = 2000,
            text = new { format = new { type = "json_schema", name = "grounded_answer", strict = true, schema = GroundedAnswerContract.SchemaFor(passages) } }
        }, ct);
        if (json.RootElement.GetProperty("status").GetString() != "completed")
            throw new InvalidOperationException("OpenAI did not complete the answer.");
        var texts = json.RootElement.GetProperty("output").EnumerateArray()
            .Where(o => o.GetProperty("type").GetString() == "message")
            .SelectMany(o => o.GetProperty("content").EnumerateArray())
            .Where(c => c.GetProperty("type").GetString() == "output_text")
            .Select(c => c.GetProperty("text").GetString());
        var text = string.Concat(texts);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("OpenAI returned no usable answer.");
        var answer = JsonSerializer.Deserialize<Answer>(text, JsonOptions) ?? throw new InvalidOperationException("OpenAI returned an empty answer.");
        return CitationReferences.Expand(answer,passages) with { Origin = "Live OpenAI", Error = null };
    }
}
