using System.Text.Json;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;

namespace Nekomata.Data.Local;

public sealed class LocalWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata Personal", "workspace.json");

    public string FilePath => _path;

    public async Task<T> ReadAsync<T>(Func<LocalWorkspaceData, T> read)
    {
        await _gate.WaitAsync();
        try { return read(await LoadAsync()); }
        finally { _gate.Release(); }
    }

    public async Task<T> UpdateAsync<T>(Func<LocalWorkspaceData, T> update)
    {
        await _gate.WaitAsync();
        try
        {
            var data = await LoadAsync();
            var result = update(data);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(data, JsonOptions));
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task EnsureCreatedAsync()
    {
        if (!File.Exists(_path)) await UpdateAsync(_ => true);
    }

    private async Task<LocalWorkspaceData> LoadAsync()
    {
        if (!File.Exists(_path)) return new LocalWorkspaceData();
        try { return JsonSerializer.Deserialize<LocalWorkspaceData>(await File.ReadAllTextAsync(_path)) ?? new LocalWorkspaceData(); }
        catch (JsonException ex) { throw new InvalidDataException("The local workspace file could not be read.", ex); }
    }
}

public sealed class LocalWorkspaceData
{
    public List<NekomataTask> Tasks { get; set; } = [];
    public List<NekomataProject> Projects { get; set; } = [];
    public List<MissionSession> MissionSessions { get; set; } = [];
    public List<GuardianMemory> GuardianMemories { get; set; } = [];
    public List<GuardianAuditEntry> GuardianAudit { get; set; } = [];
}
