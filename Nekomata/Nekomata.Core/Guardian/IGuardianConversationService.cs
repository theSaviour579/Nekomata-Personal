using Nekomata.AI.Models.Actions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public interface IGuardianConversationService
{
    Task<GuardianActionResponse> AskAsync(
        GuardianConversationRequest request);
}