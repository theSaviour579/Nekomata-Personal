using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nekomata.Services.Halo;

internal sealed class HaloApiTicket
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = "";

    [JsonPropertyName("status_id")]
    public int StatusId { get; set; }

    [JsonPropertyName("status")]
    public string? StatusName { get; set; }

    [JsonPropertyName("priority_id")]
    public int PriorityId { get; set; }

    [JsonPropertyName("priority")]
    public string? PriorityName { get; set; }

    [JsonPropertyName("priority_name")]
    public string? PriorityDisplayName { get; set; }

    [JsonPropertyName("sla_name")]
    public string? SlaName { get; set; }

    [JsonPropertyName("sla_priority")]
    public string? SlaPriority { get; set; }

    [JsonPropertyName("sla_priority_name")]
    public string? SlaPriorityName { get; set; }

    [JsonPropertyName("slapriority")]
    public string? CompactSlaPriority { get; set; }

    [JsonPropertyName("slapriority_name")]
    public string? CompactSlaPriorityName { get; set; }

    [JsonPropertyName("agent_name")]
    public string AgentName { get; set; } = "";

    [JsonPropertyName("team")]
    public string Team { get; set; } = "";

    [JsonPropertyName("category_1_display")]
    public string Category1 { get; set; } = "";

    [JsonPropertyName("category_2_display")]
    public string Category2 { get; set; } = "";

    [JsonPropertyName("dateoccurred")]
    public DateTime? DateOccurred { get; set; }

    [JsonPropertyName("fixbydate")]
    public DateTime? FixByDate { get; set; }

    [JsonPropertyName("respondbydate")]
    public DateTime? RespondByDate { get; set; }

    [JsonPropertyName("sla_action_date")]
    public DateTime? SlaActionDate { get; set; }

    [JsonPropertyName("last_update")]
    public DateTime? LastUpdate { get; set; }

    [JsonPropertyName("time_taken")]
    public double TimeTaken { get; set; }

    [JsonPropertyName("onhold")]
    public bool OnHold { get; set; }

    [JsonPropertyName("is_vip")]
    public bool IsVip { get; set; }

    [JsonPropertyName("ticketage")]
    public double TicketAge { get; set; }

    [JsonPropertyName("tickettype_id")]
    public int TicketTypeId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }

}