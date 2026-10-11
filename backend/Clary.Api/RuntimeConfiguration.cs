namespace Clary.Api;

public sealed record HealthStatus(string Status, string Origin, bool LiveReady, string? ConfigurationError);

// Owns safe readiness metadata. It does not store or return the API credential.
public sealed class RuntimeConfiguration
{
    public string Provider { get; }
    private readonly bool keyPresent;
    private readonly bool ollamaConfigured;

    private RuntimeConfiguration(string provider, bool keyPresent, bool ollamaConfigured)
    {
        Provider = provider;
        this.keyPresent = keyPresent;
        this.ollamaConfigured = ollamaConfigured;
    }

    public static RuntimeConfiguration Create(string provider, string? apiKey, string? ollamaBaseUrl = null)
    {
        if (provider is not ("OpenAI" or "Ollama" or "Demo"))
            throw new ArgumentException("AI_PROVIDER must be OpenAI, Ollama, or Demo.", nameof(provider));
        var validUrl=Uri.TryCreate(ollamaBaseUrl,UriKind.Absolute,out var url) && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo);
        return new(provider, !string.IsNullOrWhiteSpace(apiKey),validUrl);
    }

    public HealthStatus Health() => new("ok", Provider switch {"OpenAI"=>"Live OpenAI","Ollama"=>"Live Ollama",_=>"Demo workspace"},
        Provider == "OpenAI" && keyPresent || Provider == "Ollama" && ollamaConfigured,
        Provider == "OpenAI" && !keyPresent ? "OPENAI_API_KEY is missing from the backend process environment." :
        Provider == "Ollama" && !ollamaConfigured ? "Set OLLAMA_BASE_URL to your private Ubuntu server URL." : null);
}
