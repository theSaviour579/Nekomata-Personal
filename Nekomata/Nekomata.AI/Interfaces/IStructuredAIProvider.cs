namespace Nekomata.AI.Interfaces;

public interface IStructuredAIProvider
{
    Task<T?> AskStructuredAsync<T>(
        string systemPrompt,
        string userPrompt)
        where T : class;

    /// <summary>The same, with a picture (PNG) for the model to look at alongside the text.</summary>
    Task<T?> AskStructuredWithImageAsync<T>(string systemPrompt, string userPrompt, byte[] png) where T : class =>
        throw new NotSupportedException("This AI provider can't look at images.");
}