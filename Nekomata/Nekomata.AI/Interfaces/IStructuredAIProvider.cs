namespace Nekomata.AI.Interfaces;

public interface IStructuredAIProvider
{
    Task<T?> AskStructuredAsync<T>(
        string systemPrompt,
        string userPrompt)
        where T : class;
}