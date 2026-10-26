using System.IO;
using System.Text.Json;

namespace Nekomata.UI.ViewModels;

// A tiny per-feature "already handled" list on disk, so background features act on each email, ticket or meeting
// once, across restarts. Entries older than the retention window are dropped on save.
public partial class MainViewModel
{
    private sealed class HandledLedger
    {
        public Dictionary<string, DateTimeOffset> Items { get; set; } = new(StringComparer.Ordinal);
    }

    private static string LedgerPath(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", $"{name}.json");

    private static HandledLedger LoadLedger(string name)
    {
        try
        {
            var path = LedgerPath(name);
            if (File.Exists(path)) return JsonSerializer.Deserialize<HandledLedger>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ledger {name} unreadable: {ex.Message}"); }
        return new();
    }

    private static void SaveLedger(string name, HandledLedger ledger, TimeSpan retention)
    {
        try
        {
            var cutoff = DateTimeOffset.Now - retention;
            foreach (var key in ledger.Items.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList()) ledger.Items.Remove(key);
            var path = LedgerPath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(ledger));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ledger {name} not saved: {ex.Message}"); }
    }
}
