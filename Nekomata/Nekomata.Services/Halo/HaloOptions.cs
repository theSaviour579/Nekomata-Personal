namespace Nekomata.Services.Halo;

public sealed class HaloOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Tenant { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AssignedAgentName { get; set; } = string.Empty;
    public int RefreshIntervalMinutes { get; set; } = 5;
    public int PageSize { get; set; } = 50;
    public Dictionary<int, string> PriorityMappings { get; set; } = new()
    {
        [1] = "Critical",
        [2] = "High",
        [3] = "Normal",
        [4] = "Low",
        [5] = "Phish Alert"
    };
    public List<int> ActiveStatusIds { get; set; } = [1, 2, 3, 4, 5, 12, 14, 17, 18, 20, 21, 22, 23, 25, 26, 27, 28];
    public List<int> IncludedTicketTypeIds { get; set; } = [];
}
