using System.Net.Http.Json;
using System.Text.Json;

namespace Clary.Api;
public static class OllamaHttp
{
    public static async Task<JsonDocument> PostAsync(HttpClient client,string endpoint,object body,CancellationToken ct)
    {
        using var response=await client.PostAsJsonAsync(endpoint,body,ct);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode}. Check server reachability, installed models, and available memory.");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
    }
}
