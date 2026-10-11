namespace Clary.Api;
public sealed class OllamaEmbeddingProvider(HttpClient client,EmbeddingProfile profile):IEmbeddingProvider
{
    public EmbeddingProfile Profile=>profile;
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct)
    {
        if(texts.Count==0)return [];
        using var response=await OllamaHttp.PostAsync(client,"api/embed",new{model=profile.Model,input=texts,truncate=false},ct);
        if(response.RootElement.GetProperty("model").GetString()!=profile.Model)throw new InvalidOperationException("Ollama returned a different embedding model. Reindex configuration must match.");
        var vectors=response.RootElement.GetProperty("embeddings").EnumerateArray().Select(e=>e.EnumerateArray().Select(v=>v.GetSingle()).ToArray()).ToArray();
        if(vectors.Length!=texts.Count || vectors.Any(v=>v.Length!=profile.Dimensions || v.Any(x=>!float.IsFinite(x)) || v.All(x=>x==0)))
            throw new InvalidOperationException("Ollama embedding dimensions or count differ from the configured index.");
        return vectors;
    }
}
