using System.Text.Json;

namespace Nekomata.Services.KnowBe4;

public sealed class KnowBe4AcknowledgementStore
{
    private readonly object _gate = new();
    private HashSet<string>? _acknowledged;

    public bool IsAcknowledged(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        lock (_gate) return Load().Contains(eventId);
    }

    public Task AcknowledgeAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return Task.CompletedTask;
        lock (_gate)
        {
            var values = Load();
            if (!values.Add(eventId)) return Task.CompletedTask;
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(values.OrderBy(value => value)));
        }
        return Task.CompletedTask;
    }

    private HashSet<string> Load()
    {
        if (_acknowledged is not null) return _acknowledged;
        try
        {
            _acknowledged = File.Exists(StorePath)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(StorePath)) ?? new(StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _acknowledged = new(StringComparer.OrdinalIgnoreCase);
        }
        return _acknowledged;
    }

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata",
        "knowbe4-acknowledged.json");
}