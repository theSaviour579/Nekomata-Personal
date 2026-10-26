using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.OpenLoops;

public static class GuardianChasePolicy
{
    public static int CooldownWorkingDaysAfter(int sentAttemptNumber) => sentAttemptNumber switch
    {
        <= 2 => 3,
        _ => 5
    };

    public static DateTimeOffset NextChaseDue(DateTimeOffset sentAt, int sentAttemptNumber) =>
        AddWorkingDays(sentAt, CooldownWorkingDaysAfter(sentAttemptNumber));

    public static GuardianChaseState DeriveState(
        GuardianOpenLoopChase loop,
        IReadOnlyList<GuardianChaseAttempt> attempts,
        DateTimeOffset now)
    {
        var latest = attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
        if (latest is null) return loop.ChaseMature ? GuardianChaseState.Due : GuardianChaseState.NotDue;
        if (latest.Status == GuardianChaseStatus.Prepared) return GuardianChaseState.Prepared;
        if (latest.Status == GuardianChaseStatus.Approved) return GuardianChaseState.Approved;
        if (latest.Status == GuardianChaseStatus.Sent)
        {
            var due = latest.NextChaseDueAt ?? NextChaseDue(latest.SentAt ?? latest.PreparedAt, latest.AttemptNumber);
            if (now < due) return GuardianChaseState.CoolingDown;
            return latest.AttemptNumber >= 3 ? GuardianChaseState.EscalationDue : GuardianChaseState.Due;
        }
        return loop.ChaseMature ? GuardianChaseState.Due : GuardianChaseState.NotDue;
    }

    public static DateTimeOffset AddWorkingDays(DateTimeOffset value, int days)
    {
        var result = value;
        while (days > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) days--;
        }
        return result;
    }
}
