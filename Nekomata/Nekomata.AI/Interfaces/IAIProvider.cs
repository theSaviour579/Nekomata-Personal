namespace Nekomata.AI.Interfaces;

public interface IAIProvider
{
    Task<string> AskAsync(string prompt);

    Task<T?> AskJsonAsync<T>(string prompt);
}