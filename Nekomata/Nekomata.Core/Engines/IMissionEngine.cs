using Nekomata.Models.Workspace;

public interface IMissionEngine
{
    NekomataWorkspace BuildMission(NekomataWorkspace workspace);
}