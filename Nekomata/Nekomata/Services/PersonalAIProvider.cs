using Microsoft.Extensions.Configuration;
using Nekomata.AI.Interfaces;
using Nekomata.AI.Providers;

namespace Nekomata.UI.Services;

public sealed class PersonalAIProvider(PersonalSecretService secrets) : IAIProvider, IStructuredAIProvider
{
    private const string NotConfigured = "AI assistance is not configured yet. Add an OpenAI API key during Personal setup to enable it.";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(secrets.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    public Task<string> AskAsync(string prompt) { var provider = CreateProvider(); return provider is null ? Task.FromResult(NotConfigured) : provider.AskAsync(prompt); }
    public Task<T?> AskJsonAsync<T>(string prompt) { var provider = CreateProvider(); return provider is null ? Task.FromResult<T?>(default) : provider.AskJsonAsync<T>(prompt); }
    public Task<T?> AskStructuredAsync<T>(string systemPrompt, string userPrompt) where T : class { var provider = CreateStructuredProvider(); return provider is null ? Task.FromResult<T?>(default) : provider.AskStructuredAsync<T>(systemPrompt, userPrompt); }
    private OpenAIProvider? CreateProvider() { var configuration = CreateConfiguration(); return configuration is null ? null : new OpenAIProvider(configuration); }
    private OpenAIStructuredProvider? CreateStructuredProvider() { var configuration = CreateConfiguration(); return configuration is null ? null : new OpenAIStructuredProvider(configuration); }
    private IConfiguration? CreateConfiguration()
    {
        var key = secrets.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return string.IsNullOrWhiteSpace(key) ? null : new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = key }).Build();
    }
}
