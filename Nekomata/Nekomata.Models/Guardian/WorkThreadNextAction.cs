namespace Nekomata.Models.Guardian;

public enum WorkThreadNextActionDisposition { ActNow, Prepare, Wait, Blocked }

public sealed class WorkThreadNextAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkThreadId { get; set; }
    public string ThreadTitle { get; set; } = "";
    public WorkThreadNextActionDisposition Disposition { get; set; }
    public string ActionType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
    public int Confidence { get; set; }
    public DateTimeOffset DerivedAt { get; set; }
    public DateTimeOffset? ReconsiderAt { get; set; }
    public long? TaskId { get; set; }
    public Guid? OpenLoopId { get; set; }
}
