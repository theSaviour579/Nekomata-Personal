using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.AI;

public interface IGuardianToolRouter
{
    GuardianToolLaunch? Route(string request);
}