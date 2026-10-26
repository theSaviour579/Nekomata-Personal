namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    /// <summary>False while the start-up connection check is still retrying in the background.</summary>
    public bool DatabaseReady { get; set; } = true;

    /// <summary>Called by the background retry once the database answers: clear any warning and load the workspace.</summary>
    public void OnDatabaseReconnected(TimeSpan after)
    {
        var wasDown = !DatabaseReady;
        DatabaseReady = true;
        ResolveAttentionByPrefix("database:");
        Think($"Database connected after {after.TotalSeconds:0}s");
        if (wasDown) _ = LoadAsync();
    }

    /// <summary>Raised once when the database has been unreachable for a sustained period.</summary>
    public void ReportDatabaseUnavailable(string why) =>
        RaiseAttention($"database:unavailable:{DateTimeOffset.Now:yyyyMMddHH}", "Guardian", "Warning", "Can't reach the database",
            $"{why} I'm still retrying in the background. If you're in the office and the public address doesn't loop back, set Database:FallbackHosts to the server's LAN address.",
            "self_check");
}
