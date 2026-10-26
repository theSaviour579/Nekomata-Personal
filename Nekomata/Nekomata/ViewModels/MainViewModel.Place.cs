using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Core.Planning;

namespace Nekomata.UI.ViewModels;

// Office or home. Guardian reads the network every two minutes (default gateways, DNS suffixes, Wi-Fi name) and decides
// office / home / elsewhere once the user has told it which is which ("I'm in the office", "I'm at home": it learns the
// networks it can see right then). Things for the office ("when I'm next in: swap the finance monitor", or "G:" captures
// that say so) are read out the first time he arrives in the office that day, along with open tasks that mention the
// office. The Home top line shows where he is.
public partial class MainViewModel
{
    private static string PlacePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "places.json");
    private PlaceSettings? _places;
    private PlaceSettings Places => _places ??= LoadJson<PlaceSettings>(PlacePath) ?? new();
    private DateTimeOffset _placeCheckedAt;
    private string _place = "";
    private DateTime _officeArrivalOn;

    [ObservableProperty] private string placeLabel = "";

    private static List<string> NetworkSignals()
    {
        var signals = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                                                                         n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
            {
                var props = nic.GetIPProperties();
                if (props.DnsSuffix.Length > 0) signals.Add($"dns:{props.DnsSuffix.ToLowerInvariant()}");
                foreach (var gw in props.GatewayAddresses.Where(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.ToString().StartsWith("0.")))
                    signals.Add($"gw:{gw.Address}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Network read failed: {ex.Message}"); }
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", "wlan show interfaces") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(3000);
            var ssid = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (ssid.Success) signals.Add($"wifi:{ssid.Groups[1].Value.Trim().ToLowerInvariant()}");
        }
        catch { /* no Wi-Fi adapter */ }
        return signals.Distinct().ToList();
    }

    private async Task MaybeCheckPlaceAsync(DateTimeOffset now)
    {
        if (!FeatureOn("place") || now - _placeCheckedAt < TimeSpan.FromMinutes(2)) return;
        _placeCheckedAt = now;
        var signals = await Task.Run(NetworkSignals);
        var place = NetworkPlace.Decide(signals, Places.Office, Places.Home);
        var was = _place;
        _place = place;
        PlaceLabel = place switch { "office" => "IN THE OFFICE", "home" => "AT HOME", _ => "" };
        if (place == "office" && was != "office" && _officeArrivalOn != now.LocalDateTime.Date && !OffHoursActive)
        {
            _officeArrivalOn = now.LocalDateTime.Date;
            AnnounceOfficeThings();
        }
    }

    private void AnnounceOfficeThings()
    {
        var tasks = Workspace.Tasks.Where(t => t.CompletedAt is null && !t.Status.Equals("Done", StringComparison.OrdinalIgnoreCase) &&
                                               Regex.IsMatch($"{t.Title} {t.Description}", @"@office|\bin the office\b|\bwhen i'?m (?:next )?in\b|\bon site\b", RegexOptions.IgnoreCase))
            .Select(t => t.Title).ToList();
        var items = Places.OfficeReminders.Concat(tasks).Distinct().ToList();
        if (items.Count == 0) return;
        GuardianResponse = "WHILE YOU'RE IN THE OFFICE\n" + string.Join("\n", items.Select(i => $"• {i}")) +
                           (Places.OfficeReminders.Count > 0 ? "\n\nSay \"office reminders done\" to clear the list." : "");
        GuardianPanelExpanded = true;
        _ = SpeakGuardianAsync(items.Count == 1 ? $"While you're in the office: {items[0]}." : $"Welcome in. {items.Count} things for while you're here; the first is {items[0]}. The rest are on screen.");
    }

    private void AddOfficeReminder(string text)
    {
        Places.OfficeReminders.Add(text.Trim().TrimEnd('.'));
        SaveJson(PlacePath, Places);
    }

    private async Task PlaceCommandAsync(string argument)
    {
        var signals = await Task.Run(NetworkSignals);
        switch (argument)
        {
            case "office":
            case "home":
            {
                var mine = argument == "office" ? Places.Office : Places.Home;
                var other = argument == "office" ? Places.Home : Places.Office;
                // Gateways like 192.168.1.1 are common at home, so learn everything visible but let the office win ties.
                foreach (var s in signals.Where(s => !mine.Contains(s))) mine.Add(s);
                other.RemoveAll(signals.Contains);
                SaveJson(PlacePath, Places);
                _placeCheckedAt = DateTimeOffset.MinValue;
                await MaybeCheckPlaceAsync(DateTimeOffset.Now);
                await RespondAndSpeakAsync(signals.Count == 0 ? "I can't see any network details to learn from right now." :
                    $"Got it, this is {(argument == "office" ? "the office" : "home")}. I'll recognise it from now on.");
                if (argument == "office") AnnounceOfficeThings();
                return;
            }
            case "where":
                await RespondAndSpeakAsync(_place switch
                {
                    "office" => "You're in the office.",
                    "home" => "You're at home.",
                    _ => "I don't recognise this network. Say \"I'm in the office\" or \"I'm at home\" and I'll learn it."
                });
                return;
            case "done":
                var n = Places.OfficeReminders.Count;
                Places.OfficeReminders.Clear();
                SaveJson(PlacePath, Places);
                await RespondAndSpeakAsync(n == 0 ? "There were no office reminders." : $"Cleared {n} office reminder{(n == 1 ? "" : "s")}.");
                return;
            case "list":
                await RespondAndSpeakAsync(Places.OfficeReminders.Count == 0 ? "Nothing waiting for the office." :
                    "For the office: " + string.Join("; ", Places.OfficeReminders) + ".");
                return;
            default:
                if (argument.StartsWith("add:", StringComparison.Ordinal))
                {
                    AddOfficeReminder(argument[4..]);
                    await RespondAndSpeakAsync($"I'll remind you next time you're in the office: {argument[4..].Trim()}.");
                }
                return;
        }
    }
}
