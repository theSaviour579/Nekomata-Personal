using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IContextSignalRepository
{
    Task<ContextSignal> UpsertAsync(ContextSignal signal);
    Task<List<ContextSignal>> GetCurrentAsync(int count = 1000);
    /// <summary>Signals (any state) whose title or body contains any of the words, newest first.</summary>
    Task<List<ContextSignal>> SearchAsync(IReadOnlyList<string> words, int count = 200) => Task.FromResult(new List<ContextSignal>());
    Task ResolveAsync(Guid id, ContextSignalDisposition disposition, Guid? threadId, long? taskId, string resolution);
}
