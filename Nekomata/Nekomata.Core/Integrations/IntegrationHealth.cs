public class IntegrationHealth
{
    public bool Connected { get; set; }

    public DateTime LastSuccessfulSync { get; set; }

    public string Status { get; set; } = "";

    public string? Error { get; set; }

    public int RecordsLoaded { get; set; }
}