using System.Security.Cryptography;
using System.Text;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Learning;

public sealed class LearnedWorkflowEngine(
    ILearnedWorkflowRepository learned,
    IGuardianPolicyRepository policies,
    IWorkdayEventRepository chronicle,
    WorkdayJournalService journal)
{
    public const int MinimumComparableThreads = 5;
    public const int MinimumConfidencePercent = 80;
    private static readonly string[] ResultTypes = ["Validation", "PostChangeVerification", "RiskAssessment", "ChangeControl", "ECSV"];

    public async Task<IReadOnlyList<LearnedWorkflowPattern>> ObserveAsync(NekomataWorkspace workspace, DateTimeOffset now)
    {
        var events=await chronicle.GetBetweenAsync(now.AddYears(-3),now.AddDays(1),10000);
        foreach(var thread in workspace.WorkThreads)
        {
            var project=thread.PrimaryProjectId is long projectId?workspace.Projects.FirstOrDefault(x=>x.Id==projectId):null;
            if(project is null) continue;
            var threadEvents=events.Where(x=>x.CorrelationId==thread.Id||x.ProjectId==project.Id).ToList();
            var completed=project.Status.Equals("Completed",StringComparison.OrdinalIgnoreCase)||threadEvents.Any(x=>x.EventType=="ObjectiveCompleted");
            if(!completed) continue;
            var trigger=ClassifyTrigger($"{project.Name} {project.Description}",threadEvents.Select(x=>$"{x.Title} {x.Narrative}"));
            if(trigger is null) continue;
            var linkedTaskIds=thread.Links.Where(x=>x.EntityType=="Task"&&long.TryParse(x.EntityKey,out _)).Select(x=>long.Parse(x.EntityKey)).ToHashSet();
            var evidenceText=string.Join(" ",workspace.Tasks.Where(x=>linkedTaskIds.Contains(x.Id)).Select(x=>$"{x.Title} {x.Description} {x.Category}")
                .Concat(threadEvents.Select(x=>$"{x.Title} {x.Narrative} {x.Category}")));
            foreach(var result in ResultTypes)
            {
                var observed=MatchesResult(evidenceText,result);
                await learned.UpsertObservationAsync(new WorkflowObservation
                {
                    WorkThreadId=thread.Id,TriggerType=trigger,ResultWorkType=result,Observed=observed,
                    Evidence=observed?$"{result} evidence found in confirmed task/Chronicle history.":$"No {result} evidence found in the completed Work Thread.",
                    SourceEventId=threadEvents.OrderByDescending(x=>x.OccurredAt).FirstOrDefault()?.EventId,ObservedAt=now
                });
            }
        }

        var observations=await learned.GetObservationsAsync();
        var patterns=new List<LearnedWorkflowPattern>();
        foreach(var group in observations.GroupBy(x=>new{x.TriggerType,x.ResultWorkType}))
        {
            var independent=group.GroupBy(x=>x.WorkThreadId).Select(x=>x.OrderByDescending(y=>y.ObservedAt).First()).ToList();
            var support=independent.Count(x=>x.Observed); var comparable=independent.Count;
            var confidence=comparable==0?0:(int)Math.Round(support*100d/comparable);
            var lifecycle=comparable>=MinimumComparableThreads&&confidence>=MinimumConfidencePercent?"Candidate":"Observing";
            var pattern=await learned.UpsertPatternAsync(new LearnedWorkflowPattern
            {
                TriggerType=group.Key.TriggerType,ResultWorkType=group.Key.ResultWorkType,ComparableThreadCount=comparable,
                SupportThreadCount=support,ExceptionThreadCount=comparable-support,ConfidencePercent=confidence,Lifecycle=lifecycle
            });
            patterns.Add(pattern);
            if(lifecycle!="Candidate") continue;
            var policy=await policies.UpsertEvidenceAsync(new GuardianPolicy
            {
                Id=Guid.NewGuid(),PolicyKey=$"learned:{pattern.TriggerType}:{pattern.ResultWorkType}".ToLowerInvariant(),
                Name=$"{Friendly(pattern.ResultWorkType)} for {Friendly(pattern.TriggerType)} work",
                Statement=$"{support} of {comparable} independent completed {Friendly(pattern.TriggerType)} Work Threads included {Friendly(pattern.ResultWorkType)} work; {comparable-support} exception(s) retained.",
                Action=$"ensure_{pattern.ResultWorkType.ToLowerInvariant()}",WorkType=pattern.ResultWorkType,Lifecycle="Candidate",
                ConfidencePercent=confidence,EvidenceCount=comparable
            });
            pattern.GuardianPolicyId=policy.Id; pattern=await learned.UpsertPatternAsync(pattern);
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId=StableId("learned-rule-candidate",pattern.Id.ToString()),CorrelationId=pattern.Id,OccurredAt=now,
                EventType="LearnedWorkflowRuleCandidate",Category="Learning",Title=policy.Name,Narrative=policy.Statement,
                ExternalId=pattern.Id.ToString(),Outcome="Candidate",Significance=70
            });
        }
        workspace.Policies=await policies.GetAllAsync();
        return patterns;
    }

    internal static string? ClassifyTrigger(string projectText,IEnumerable<string> evidence)
    {
        var text=$"{projectText} {string.Join(" ",evidence)}";
        if(ContainsAny(text,"infrastructure","network","server","leased line")) return "InfrastructureChange";
        if(ContainsAny(text,"data","database","reporting","migration")) return "DataProject";
        if(ContainsAny(text,"iso","compliance","audit")) return "ComplianceProject";
        if(ContainsAny(text,"change control","ecsv","deployment","system change")) return "SystemChange";
        return null;
    }
    private static bool MatchesResult(string text,string result)=>result switch
    {
        "Validation"=>ContainsAny(text,"validation","validate","data quality"),
        "PostChangeVerification"=>ContainsAny(text,"post-change verification","post change verification","verify change"),
        "RiskAssessment"=>ContainsAny(text,"risk assessment"),
        "ChangeControl"=>ContainsAny(text,"change control"),
        "ECSV"=>ContainsAny(text,"ecsv"),_=>false
    };
    private static bool ContainsAny(string value,params string[] terms)=>terms.Any(x=>value.Contains(x,StringComparison.OrdinalIgnoreCase));
    private static string Friendly(string value)=>System.Text.RegularExpressions.Regex.Replace(value,"(?<!^)([A-Z])"," $1");
    private static Guid StableId(string type,string key)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}:{key}"))[..16]);
}
