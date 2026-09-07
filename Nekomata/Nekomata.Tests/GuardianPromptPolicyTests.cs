using Nekomata.Core.Guardian.Anticipation;
using Xunit;
namespace Nekomata.Tests;
public class GuardianPromptPolicyTests
{
    private static bool Allowed(GuardianPromptKind request, GuardianPromptKind[] waiting, bool occupied=false, bool urgent=false, int pauseMinutes=-1, int quietMinutes=-1)
    {
        var now=DateTimeOffset.UtcNow;
        return GuardianPromptPolicy.CanPresent(request,waiting,occupied,urgent,now,now.AddMinutes(pauseMinutes),now.AddMinutes(quietMinutes));
    }
    [Fact] public void Only_one_routine_prompt_at_a_time() => Assert.False(Allowed(GuardianPromptKind.MeetingBrief,[],occupied:true));
    [Fact] public void Urgent_attention_blocks_routine_prompts() => Assert.False(Allowed(GuardianPromptKind.Preflight,[],urgent:true));
    [Fact] public void Meeting_brief_precedes_next_action() => Assert.False(Allowed(GuardianPromptKind.NextAction,[GuardianPromptKind.MeetingBrief]));
    [Fact] public void Highest_priority_can_proceed() => Assert.True(Allowed(GuardianPromptKind.MeetingBrief,[GuardianPromptKind.MeetingBrief,GuardianPromptKind.Chases]));
    [Fact] public void Global_pause_is_respected() => Assert.False(Allowed(GuardianPromptKind.Chases,[],pauseMinutes:15));
    [Fact] public void Quiet_period_prevents_back_to_back_prompts() => Assert.False(Allowed(GuardianPromptKind.NextAction,[],quietMinutes:2));
    [Fact] public void Expired_pause_allows_pending_work() => Assert.True(Allowed(GuardianPromptKind.Chases,[]));
}
