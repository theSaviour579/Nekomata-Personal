using System.Text.Json.Serialization;

namespace Nekomata.Services.Halo;

internal sealed class HaloTicketPage
{
    [JsonPropertyName("tickets")]
    public List<HaloApiTicket> Tickets { get; set; } = [];
}