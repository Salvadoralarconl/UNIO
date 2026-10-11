namespace Clary.Api;

public sealed class OpenAiEmbeddingProvider(HttpClient client, EmbeddingProfile profile) : IEmbeddingProvider
{
    public EmbeddingProfile Profile => profile;
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return [];
        using var json = await OpenAiHttp.PostAsync(client, "embeddings", new { model = profile.Model, input = texts, dimensions = profile.Dimensions }, ct);
        var data = json.RootElement.GetProperty("data").EnumerateArray().OrderBy(x => x.GetProperty("index").GetInt32()).ToArray();
        if (data.Length != texts.Count) throw new InvalidOperationException("Embedding count did not match inputs.");
        var vectors = new List<float[]>();
        for (var i = 0; i < data.Length; i++)
        {
            var vector = data[i].GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            if (data[i].GetProperty("index").GetInt32() != i || vector.Length != profile.Dimensions || vector.Any(v => !float.IsFinite(v)) || vector.All(v => v == 0))
                throw new InvalidOperationException("Embedding model, dimensions, or ordering did not match the configured index.");
            vectors.Add(vector);
        }
        return vectors;
    }
}
