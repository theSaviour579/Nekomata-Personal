using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.OpenLoops;

public enum GuardianOpenLoopSignalKind { Email, Meeting, Message }

public sealed class GuardianOpenLoopSignal
{
    public GuardianOpenLoopSignalKind Kind { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string PersonName { get; set; } = "";
    public string PersonAddress { get; set; } = "";
    public long? PersonId { get; set; }
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
    public bool IsOutbound { get; set; }
    public bool ContainsQuestionOrRequest { get; set; }
    public int AgeDays { get; set; }
    public bool PreparationRequired { get; set; }
    public bool RequestsMeetingArrangement { get; set; }
    public string EmailId { get; set; } = "";
    public string TeamsMessageId { get; set; } = "";
    public string CalendarEventId { get; set; } = "";
}

public static class GuardianOpenLoopDetector
{
    public static GuardianOpenLoop? Detect(GuardianOpenLoopSignal signal)
    {
        var type = signal.Kind switch
        {
            GuardianOpenLoopSignalKind.Email when signal.IsOutbound && signal.ContainsQuestionOrRequest
                => OpenLoopType.AwaitingThem,
            GuardianOpenLoopSignalKind.Meeting when signal.PreparationRequired
                => OpenLoopType.AwaitingMe,
            GuardianOpenLoopSignalKind.Message when signal.RequestsMeetingArrangement
                => OpenLoopType.MeetingRequired,
            _ => (OpenLoopType?)null
        };
        if (type is null) return null;

        return new GuardianOpenLoop
        {
            CreatedAt = signal.OccurredAt,
            UpdatedAt = signal.OccurredAt,
            Type = type.Value,
            Title = signal.Title,
            Detail = signal.Summary,
            WaitingOn = signal.PersonName,
            WaitingOnAddress = signal.PersonAddress,
            PersonId = signal.PersonId,
            ProjectId = signal.ProjectId,
            TaskId = signal.TaskId,
            EmailId = signal.EmailId,
            TeamsMessageId = signal.TeamsMessageId,
            CalendarEventId = signal.CalendarEventId,
            Source = signal.Kind.ToString(),
            SourceExternalId = First(signal.EmailId, signal.TeamsMessageId, signal.CalendarEventId),
            SourceEventId = signal.EventId,
            ExpectedEvidenceKind = type switch
            {
                OpenLoopType.AwaitingThem when signal.Kind == GuardianOpenLoopSignalKind.Email => "ReplyReceived",
                OpenLoopType.AwaitingMe => "TaskCompleted",
                OpenLoopType.MeetingRequired => "MeetingScheduled",
                _ => ""
            }
        };
    }

    private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
}
