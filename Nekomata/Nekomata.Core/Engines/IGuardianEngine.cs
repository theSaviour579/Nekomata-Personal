using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

public interface IGuardianEngine
{
    GuardianState Analyse(NekomataWorkspace workspace);
}