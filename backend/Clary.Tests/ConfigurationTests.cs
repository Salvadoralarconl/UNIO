using Clary.Api;
using System.Text.Json;
using Xunit;

public class ConfigurationTests
{
    [Fact]
    public void MissingKeyDisablesLiveReadiness()
    {
        var config = RuntimeConfiguration.Create("OpenAI", null);
        Assert.False(config.Health().LiveReady);
        Assert.Equal("Live OpenAI", config.Health().Origin);
    }

    [Fact]
    public void HealthNeverContainsCredential()
    {
        var config = RuntimeConfiguration.Create("OpenAI", "test-secret-sentinel");
        Assert.True(config.Health().LiveReady);
        Assert.DoesNotContain("test-secret-sentinel", JsonSerializer.Serialize(config.Health()));
    }

    [Fact]
    public void UnknownProviderFailsExplicitly()
    {
        Assert.Throws<ArgumentException>(() => RuntimeConfiguration.Create("Typo", null));
    }
    [Fact] public void OllamaNeedsServerConfigurationButNoOpenAiKey()
    {
        Assert.False(RuntimeConfiguration.Create("Ollama",null).Health().LiveReady);
        var health=RuntimeConfiguration.Create("Ollama",null,"http://192.168.1.50:11434").Health();
        Assert.True(health.LiveReady);Assert.Equal("Live Ollama",health.Origin);
    }
}
