using System.Net;
using System.Text;
using Clary.Api;
using Xunit;

public class ProviderTests
{
    [Fact] public async Task WrongEmbeddingDimensionsAreRejected() {
        using var client = Client("{\"data\":[{\"index\":0,\"embedding\":[1,2]}]}");
        var provider = new OpenAiEmbeddingProvider(client, new("OpenAI", "text-embedding-3-small", 1536, "chars-1200-160-v1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedAsync(["vpn"], default));
    }
    [Fact] public async Task EmptyAnswerIsRejected() {
        var provider = new OpenAiAnswerProvider(Client("{\"status\":\"completed\",\"output\":[]}"), "gpt-4.1-mini");
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GenerateAsync("vpn", [], default));
    }
    [Fact] public async Task QuotaErrorDoesNotLeakProviderBody() {
        var provider = new OpenAiAnswerProvider(Client("private-provider-body", HttpStatusCode.TooManyRequests), "gpt-4.1-mini");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GenerateAsync("vpn", [], default));
        Assert.DoesNotContain("private-provider-body", error.Message);
    }
    private static HttpClient Client(string json, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(json, status)) { BaseAddress = new("https://api.openai.com/v1/") };
    private sealed class Handler(string json, HttpStatusCode status) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
