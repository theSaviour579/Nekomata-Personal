using Nekomata.AI.Interfaces;

namespace Nekomata.AI.Providers;

public sealed class UnconfiguredAIProvider : IAIProvider, IStructuredAIProvider
{
    public const string Message =
        "Guardian AI is not configured. Add OpenAI:ApiKey to user secrets to enable AI features.";

    public Task<string> AskAsync(string prompt) => Task.FromResult(Message);

    public Task<T?> AskJsonAsync<T>(string prompt) => Task.FromResult<T?>(default);

    public Task<T?> AskStructuredAsync<T>(string systemPrompt, string userPrompt)
        where T : class => Task.FromResult<T?>(default);
}