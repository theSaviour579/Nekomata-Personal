using System.Security.Cryptography;
using System.Text;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Learning;

public sealed class LearnedWorkflowPromotionService(
    ILearnedWorkflowRepository learned,
    IGuardianPolicyRepository policies,
    WorkdayJournalService journal)
{
    public async Task<bool> PromoteAsync(GuardianPolicy policy,string rationale,DateTimeOffset now)
    {
        if(!policy.IsCandidate||!policy.PolicyKey.StartsWith("learned:",StringComparison.OrdinalIgnoreCase)) return false;
        var pattern=await learned.FindByPolicyIdAsync(policy.Id); if(pattern is null) return false;
        var eventId=StableId("learned-rule-promoted",$"{pattern.Id}:{policy.Id}");
        await policies.SetLifecycleAsync(policy.Id,"Active");
        await learned.MarkPromotedAsync(pattern.Id,policy.Id);
        await learned.AddPromotionDecisionAsync(new PolicyPromotionDecision
        {
            PatternId=pattern.Id,Decision="Approved",Rationale=rationale,GuardianPolicyId=policy.Id,
            SourceEventId=eventId,DecidedAt=now
        });
        await journal.RecordIfAbsentAsync(new WorkdayEvent
        {
            EventId=eventId,CorrelationId=pattern.Id,OccurredAt=now,EventType="LearnedWorkflowRulePromoted",
            Category="Learning",Title=policy.Name,Narrative=rationale,ExternalId=pattern.Id.ToString(),
            Outcome="Active",UserDecision="Approved",Significance=85
        });
        return true;
    }
    private static Guid StableId(string type,string key)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}:{key}"))[..16]);
}
