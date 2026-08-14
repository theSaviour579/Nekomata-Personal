using Nekomata.Services.Halo;
using Nekomata.Models.Integrations;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Integrations;

public sealed class HaloWorkspaceDataSource : IWorkspaceDataSource
{
    private readonly IHaloClient _client;
    private readonly HaloOptions _options;

    public string Name => "Halo";

    public HaloWorkspaceDataSource(IHaloClient client, HaloOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<WorkspaceDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new WorkspaceDataSnapshot
        {
            SourceName = Name,
            RetrievedAt = DateTime.Now
        };

        var tickets = await _client.GetMyTicketsAsync(cancellationToken);
        var relevantOpenTickets = tickets
            .Where(ticket => !ticket.IsClosed)
            .Where(ticket => !ticket.IsPhishAlert || ticket.IsP1)
            .Where(ticket => ticket.IsP1 ||
                string.IsNullOrWhiteSpace(_options.AssignedAgentName) ||
                string.Equals(ticket.AgentName.Trim(), _options.AssignedAgentName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(ticket => ticket.IsP1 ||
                _options.IncludedTicketTypeIds.Count == 0 ||
                _options.IncludedTicketTypeIds.Contains(ticket.TicketTypeId))
            .OrderByDescending(ticket => ticket.IsP1)
            .ThenByDescending(ticket => ticket.RequiresImmediateAttention)
            .ThenBy(ticket => ticket.Due ?? DateTime.MaxValue)
            .ToList();

        foreach (var ticket in relevantOpenTickets)        {
            var isPaused = ticket.StatusId is 4 or 28;
            var isOutsourced = ticket.StatusId is 31 or 32 or 33 or 34 or 36;
            var isStaleOutsourced = isOutsourced && BusinessDaysSince(ticket.LastUpdatedAt ?? ticket.Created, DateTime.Now) >= 2;
            var isActionable = !isPaused && (!isOutsourced || isStaleOutsourced);
            var effectiveImmediateAttention = ticket.IsP1 || (ticket.RequiresImmediateAttention && !isPaused && !isOutsourced);
            var effectiveP1 = ticket.IsP1;

            snapshot.IntegrationMissions.Add(new IntegrationMission
            {
                SourceType = "Halo",
                SourceRecordId = ticket.Id.ToString(),
                Title = $"#{ticket.Id} • {ticket.Summary}",
                Description = ResolveStatus(ticket),
                Customer = ticket.Customer,
                AssignedTo = ticket.AgentName,
                Status = ResolveStatus(ticket),
                Priority = ticket.Priority,
                BusinessValue = ticket.BusinessValue,
                EstimatedMinutes = ticket.IsPhishAlert ? 30 : ticket.EstimatedMinutes,
                CustomerImpact = ticket.CustomerImpact,
                SecurityRelated = ticket.SecurityRelated || ticket.IsPhishAlert,
                RevenueImpact = false,
                CreatedAt = ticket.Created,
                LastUpdatedAt = ticket.LastUpdatedAt,
                ExternalStatusId = ticket.StatusId,
                IsAwaitingExternalResponse = isPaused || isOutsourced,
                IsActionable = isActionable,
                DueAt = ticket.Due,
                SlaExpiresAt = ticket.Due,
                RequiresImmediateAttention = effectiveImmediateAttention,
                IsP1 = effectiveP1
            });
        }

        var urgentCount = snapshot.IntegrationMissions.Count(mission => mission.RequiresImmediateAttention);
        if (urgentCount > 0)
            snapshot.Notifications.Add($"{urgentCount} Halo ticket{(urgentCount == 1 ? string.Empty : "s")} require immediate attention.");

        snapshot.Health = new IntegrationHealth
        {
            Connected = true,
            LastSuccessfulSync = DateTime.Now,
            Status = urgentCount > 0 ? $"Connected · {urgentCount} urgent" : "Connected",
            RecordsLoaded = relevantOpenTickets.Count
        };

        return snapshot;
    }

    private static string ResolveStatus(HaloTicket ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket.Status) ||
            int.TryParse(ticket.Status, out _))
        {
            return MapStatus(ticket.StatusId);
        }

        return ticket.Status;
    }

    private static int BusinessDaysSince(DateTime from, DateTime to)
    {
        if (from >= to) return 0;
        var days = 0;
        for (var date = from.Date.AddDays(1); date <= to.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                days++;
        }
        return days;
    }

    private static string MapStatus(int statusId) => statusId switch
    {
        1 => "New",
        2 => "In Progress",
        3 => "Action Required",
        4 => "With End User",
        5 => "With Supplier",
        9 => "Closed",
        21 => "Updated",
        22 => "Re-Opened",
        28 => "On Hold",
        31 => "Web Developers",
        32 => "ERP Developers",
        33 => "CRM Developer",
        34 => "Server Administrator",
        36 => "Printer MSP",
        _ => $"Status {statusId}"
    };
}