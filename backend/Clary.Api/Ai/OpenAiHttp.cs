using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Clary.Api;
public static class OpenAiHttp
{
    public static async Task<JsonDocument> PostAsync(HttpClient client, string endpoint, object body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await client.PostAsJsonAsync(endpoint, body, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                try {
                    using var errorJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (errorJson.RootElement.TryGetProperty("error",out var error) && error.TryGetProperty("type",out var type) && type.GetString()=="insufficient_quota")
                        throw new InvalidOperationException("OpenAI API credits or quota are exhausted. Review API billing before retrying.");
                } catch (JsonException) { /* Proxies may return non-JSON errors; never expose their body. */ }
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"OpenAI returned HTTP {(int)response.StatusCode}. Check API access, quota, and model configuration.");
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
    }
}
