using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Tasks;

public sealed class MicrosoftTaskService(HttpClient http, IMicrosoftAuthenticationService authentication)
    : IMicrosoftTaskService
{
    private static readonly string[] Scopes = ["Tasks.Read"];

    public async Task<MicrosoftTaskSnapshot> GetOpenTasksAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(await authentication.GetConnectedAccountAsync(cancellationToken)))
            return new MicrosoftTaskSnapshot(false, [], []);

        var token = await authentication.GetTokenForScopesAsync(Scopes, cancellationToken);
        var todo = await GetToDoTasksAsync(token.AccessToken, cancellationToken);
        var planner = await GetPlannerTasksAsync(token.AccessToken, cancellationToken);
        return new MicrosoftTaskSnapshot(true, todo, planner);
    }

    private async Task<IReadOnlyList<MicrosoftTaskItem>> GetToDoTasksAsync(string token, CancellationToken ct)
    {
        var lists = await GetAsync<GraphCollection<GraphToDoList>>(
            "me/todo/lists", token, ct);
        var result = new List<MicrosoftTaskItem>();
        foreach (var list in lists.Value.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
        {
            var path = $"me/todo/lists/{Uri.EscapeDataString(list.Id!)}/tasks";
            var tasks = await GetAsync<GraphCollection<GraphToDoTask>>(path, token, ct);
            result.AddRange(tasks.Value
                .Where(item => !string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase))
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Title))
                .Select(item => new MicrosoftTaskItem(
                    "Microsoft To Do", item.Id!, item.Title!, BuildToDoDescription(list.DisplayName, item.Body?.Content),
                    item.Status ?? "Not started", MapToDoPriority(item.Importance), ParseGraphDate(item.DueDateTime),
                    item.CreatedDateTime?.LocalDateTime, item.LastModifiedDateTime?.LocalDateTime,
                    item.LinkedResources.FirstOrDefault(link => Uri.TryCreate(link.WebUrl, UriKind.Absolute, out _))?.WebUrl
                        ?? "https://to-do.office.com/tasks/")));
        }
        return result;
    }

    private async Task<IReadOnlyList<MicrosoftTaskItem>> GetPlannerTasksAsync(string token, CancellationToken ct)
    {
        GraphCollection<GraphPlannerTask> tasks;
        try
        {
            tasks = await GetAsync<GraphCollection<GraphPlannerTask>>(
                "me/planner/tasks", token, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            // Planner is not supported for personal Microsoft accounts and may
            // also be unavailable when the work account has no Planner service.
            return [];
        }
        return tasks.Value
            .Where(item => item.PercentComplete < 100)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new MicrosoftTaskItem(
                "Microsoft Planner", item.Id!, item.Title!, "Assigned to you in Microsoft Planner.",
                item.PercentComplete > 0 ? $"In progress · {item.PercentComplete}%" : "Not started",
                MapPlannerPriority(item.Priority), item.DueDateTime?.LocalDateTime,
                item.CreatedDateTime?.LocalDateTime, item.CreatedDateTime?.LocalDateTime,
                "https://planner.cloud.microsoft/", item.PlanId))
            .ToList();
    }

    private async Task<T> GetAsync<T>(string path, string token, CancellationToken ct) where T : class, new()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? new T();
    }

    private static string BuildToDoDescription(string? list, string? body)
    {
        var prefix = string.IsNullOrWhiteSpace(list) ? "Microsoft To Do" : $"Microsoft To Do · {list}";
        return string.IsNullOrWhiteSpace(body) ? prefix : $"{prefix}\n{body.Trim()}";
    }

    private static string MapToDoPriority(string? importance) =>
        string.Equals(importance, "high", StringComparison.OrdinalIgnoreCase) ? "High" :
        string.Equals(importance, "low", StringComparison.OrdinalIgnoreCase) ? "Low" : "Normal";

    private static string MapPlannerPriority(int priority) => priority <= 3 ? "High" : priority >= 8 ? "Low" : "Normal";

    private static DateTime? ParseGraphDate(GraphDateTimeZone? value) =>
        value is not null && DateTime.TryParse(value.DateTime, out var parsed) ? parsed : null;

    private sealed class GraphCollection<T> { [JsonPropertyName("value")] public List<T> Value { get; init; } = []; }
    private sealed class GraphToDoList { public string? Id { get; init; } public string? DisplayName { get; init; } public string? WellknownListName { get; init; } }
    private sealed class GraphToDoTask
    {
        public string? Id { get; init; }
        public string? Title { get; init; }
        public GraphBody? Body { get; init; }
        public string? Status { get; init; }
        public string? Importance { get; init; }
        public GraphDateTimeZone? DueDateTime { get; init; }
        public DateTimeOffset? CreatedDateTime { get; init; }
        public DateTimeOffset? LastModifiedDateTime { get; init; }
        public List<GraphLinkedResource> LinkedResources { get; init; } = [];
    }
    private sealed class GraphPlannerTask
    {
        public string? Id { get; init; }
        public string? Title { get; init; }
        public string? PlanId { get; init; }
        public int PercentComplete { get; init; }
        public int Priority { get; init; } = 5;
        public DateTimeOffset? DueDateTime { get; init; }
        public DateTimeOffset? CreatedDateTime { get; init; }
    }
    private sealed class GraphBody { public string? Content { get; init; } }
    private sealed class GraphDateTimeZone { public string? DateTime { get; init; } }
    private sealed class GraphLinkedResource { public string? WebUrl { get; init; } }
}
