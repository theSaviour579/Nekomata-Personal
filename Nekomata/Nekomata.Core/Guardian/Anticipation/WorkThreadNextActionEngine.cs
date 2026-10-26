using System.Security.Cryptography;
using System.Text;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Anticipation;

public sealed class WorkThreadNextActionEngine
{
    public IReadOnlyList<WorkThreadNextAction> Derive(NekomataWorkspace workspace,
        IReadOnlyCollection<GuardianOpenLoopChase> loopStates, DateTimeOffset now)
    {
        var result = new List<WorkThreadNextAction>();
        foreach (var thread in workspace.WorkThreads.Where(x => x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)))
        {
            var summary = workspace.WorkThreadSummaries.FirstOrDefault(x => x.ThreadId == thread.Id);
            var threadLoops = loopStates.Where(x => (thread.PrimaryProjectId is long projectId && x.OpenLoop.ProjectId == projectId) ||
                thread.Links.Any(link => link.Status == WorkThreadLinkStatus.Confirmed && link.EntityType == "OpenLoop" &&
                    link.EntityKey.Equals(x.OpenLoop.Id.ToString(), StringComparison.OrdinalIgnoreCase))).ToList();
            var dependency = threadLoops.OrderByDescending(Priority).ThenByDescending(x => x.WorkingDaysOpen).FirstOrDefault();
            if (dependency is not null)
            {
                result.Add(FromDependency(thread, dependency, now));
                continue;
            }

            var requirement = workspace.ProcessRequirements.Where(x => x.WorkThreadId == thread.Id &&
                    x.State is not RequirementState.Satisfied and not RequirementState.Waived and not RequirementState.NotApplicable)
                .OrderBy(x => x.Gate).ThenBy(x => x.Name).FirstOrDefault();
            if (requirement is not null)
            {
                result.Add(Create(thread, WorkThreadNextActionDisposition.Blocked, "SatisfyProcessRequirement",
                    $"Satisfy {requirement.Name}",
                    $"{requirement.Name} is {requirement.State} and gates {requirement.Gate.ToString().ToLowerInvariant()}.",
                    [$"Requirement: {requirement.Name}", $"State: {requirement.State}", $"Gate: {requirement.Gate}", requirement.Reason],
                    100, now, taskId: requirement.TaskId));
                continue;
            }

            var taskIds = thread.Links.Where(x => x.Status == WorkThreadLinkStatus.Confirmed && x.EntityType == "Task" &&
                    long.TryParse(x.EntityKey, out _)).Select(x => long.Parse(x.EntityKey)).ToHashSet();
            var task = workspace.Tasks.Where(x => taskIds.Contains(x.Id) && !x.IsCompleted &&
                    !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.BusinessCritical).ThenByDescending(x => x.PriorityScore)
                .ThenByDescending(x => x.EstimatedBusinessValue).FirstOrDefault();
            if (task is not null)
            {
                result.Add(Create(thread, WorkThreadNextActionDisposition.ActNow, "ContinueActiveTask", task.Title,
                    "The thread has no known blocking dependency or unsatisfied process gate, so its strongest active task can move forward.",
                    [$"Confirmed task link: {task.Id}", $"Priority score: {task.PriorityScore}", $"Business value: {task.EstimatedBusinessValue:C0}"],
                    95, now, taskId: task.Id));
                continue;
            }

            result.Add(Create(thread, WorkThreadNextActionDisposition.Wait, "AwaitNewEvidence", "Wait for new evidence",
                "No active task, mature dependency intervention, or outstanding process requirement currently creates a deterministic next action.",
                [summary is null ? "Thread summary unavailable" : $"Active tasks: {summary.ActiveTasks}; open loops: {summary.OpenLoops}"],
                100, now));
        }
        return result.OrderBy(x => Order(x.Disposition)).ThenByDescending(x => x.Confidence).ToList();
    }

    private static WorkThreadNextAction FromDependency(GuardianWorkThread thread, GuardianOpenLoopChase dependency, DateTimeOffset now) =>
        dependency.ChaseState switch
        {
            GuardianChaseState.Due or GuardianChaseState.EscalationDue => Create(thread, WorkThreadNextActionDisposition.Prepare,
                "PrepareDependencyFollowUp", $"Prepare follow-up to {dependency.OpenLoop.WaitingOn}", dependency.Message,
                [$"Loop age: {dependency.WorkingDaysOpen} working days", $"Chase state: {dependency.ChaseState}", $"Chase count: {dependency.ChaseCount}"],
                100, now, openLoopId: dependency.OpenLoop.Id),
            GuardianChaseState.Prepared => Create(thread, WorkThreadNextActionDisposition.Blocked,
                "AwaitFollowUpApproval", $"Approve follow-up to {dependency.OpenLoop.WaitingOn}", dependency.Message,
                ["A prepared intervention already exists", "Sending requires explicit approval"], 100, now, openLoopId: dependency.OpenLoop.Id),
            GuardianChaseState.Approved => Create(thread, WorkThreadNextActionDisposition.Blocked,
                "SendApprovedFollowUp", $"Send approved follow-up to {dependency.OpenLoop.WaitingOn}", dependency.Message,
                ["The intervention is approved", "External sending remains an explicit action"], 100, now, openLoopId: dependency.OpenLoop.Id),
            GuardianChaseState.CoolingDown => Create(thread, WorkThreadNextActionDisposition.Wait,
                "WaitForDependency", $"Wait for {dependency.OpenLoop.WaitingOn}", dependency.Message,
                [$"Chase count: {dependency.ChaseCount}", $"Next review: {dependency.NextChaseDueAt:O}"], 100, now,
                dependency.NextChaseDueAt, openLoopId: dependency.OpenLoop.Id),
            _ => Create(thread, WorkThreadNextActionDisposition.Wait, "WaitForDependency",
                $"Wait for {dependency.OpenLoop.WaitingOn}", dependency.Message,
                [$"Loop age: {dependency.WorkingDaysOpen} working days", $"Chase state: {dependency.ChaseState}"], 100, now,
                dependency.NextChaseDueAt, openLoopId: dependency.OpenLoop.Id)
        };

    private static WorkThreadNextAction Create(GuardianWorkThread thread, WorkThreadNextActionDisposition disposition,
        string actionType, string title, string reason, List<string> evidence, int confidence, DateTimeOffset now,
        DateTimeOffset? reconsiderAt = null, long? taskId = null, Guid? openLoopId = null) => new()
    {
        Id = StableId($"{thread.Id:N}:{actionType}:{taskId}:{openLoopId}"), WorkThreadId = thread.Id,
        ThreadTitle = thread.Title, Disposition = disposition, ActionType = actionType, Title = title,
        Reason = reason, Evidence = evidence.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(), Confidence = confidence,
        DerivedAt = now, ReconsiderAt = reconsiderAt, TaskId = taskId, OpenLoopId = openLoopId
    };

    private static int Priority(GuardianOpenLoopChase value) => value.ChaseState switch
    {
        GuardianChaseState.EscalationDue => 6, GuardianChaseState.Due => 5, GuardianChaseState.Approved => 4,
        GuardianChaseState.Prepared => 3, GuardianChaseState.CoolingDown => 2, _ => 1
    };
    private static int Order(WorkThreadNextActionDisposition value) => value switch
    {
        WorkThreadNextActionDisposition.ActNow => 0, WorkThreadNextActionDisposition.Prepare => 1,
        WorkThreadNextActionDisposition.Blocked => 2, _ => 3
    };
    private static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
}
