using Nekomata.AI.Models;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public class GuardianConversationRequest
{
    public string UserMessage { get; set; } = "";

    public NekomataWorkspace Workspace { get; set; } = new();

    public DateTime CurrentTime { get; set; }

    public IReadOnlyList<GuardianChatTurn> Conversation
    {
        get;
        set;
    }
    = [];
}