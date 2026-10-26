using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nekomata.Data.Local;

/// <summary>
/// Small JSON document store for Guardian's own records in Nekomata Personal. Each named document is one file under
/// %LocalAppData%\Nekomata Personal\store, read and rewritten atomically under a per-document lock. Personal has no
/// database server, so everything that the work edition keeps in PostgreSQL lives here instead.
/// </summary>
public sealed class LocalDocumentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public LocalDocumentStore() : this(null) { }

    public LocalDocumentStore(string? rootDirectory)
    {
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nekomata Personal", "store");
    }

    public string RootDirectory => _root;

    public async Task<TResult> ReadAsync<TDocument, TResult>(string name, Func<TDocument, TResult> read)
        where TDocument : new()
    {
        var gate = Gate(name);
        await gate.WaitAsync();
        try { return read(await LoadAsync<TDocument>(name)); }
        finally { gate.Release(); }
    }

    public async Task<TResult> UpdateAsync<TDocument, TResult>(string name, Func<TDocument, TResult> update)
        where TDocument : new()
    {
        var gate = Gate(name);
        await gate.WaitAsync();
        try
        {
            var document = await LoadAsync<TDocument>(name);
            var result = update(document);
            await SaveAsync(name, document);
            return result;
        }
        finally { gate.Release(); }
    }

    public Task UpdateAsync<TDocument>(string name, Action<TDocument> update) where TDocument : new() =>
        UpdateAsync<TDocument, bool>(name, document => { update(document); return true; });

    private SemaphoreSlim Gate(string name) => _gates.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));

    private string PathFor(string name) => Path.Combine(_root, name + ".json");

    private async Task<TDocument> LoadAsync<TDocument>(string name) where TDocument : new()
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return new TDocument();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<TDocument>(stream, JsonOptions) ?? new TDocument();
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file for recovery rather than silently overwriting it.
            var quarantine = path + $".unreadable-{DateTime.Now:yyyyMMddHHmmss}";
            try { File.Move(path, quarantine, true); } catch (IOException) { }
            System.Diagnostics.Debug.WriteLine($"Local store '{name}' could not be read and was set aside: {ex.Message}");
            return new TDocument();
        }
    }

    private async Task SaveAsync<TDocument>(string name, TDocument document)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(name);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
        File.Move(temp, path, true);
    }
}
