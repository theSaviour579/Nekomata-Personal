using Nekomata.AI.Models.Meetings;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Meetings;

public interface IMeetingAnalysisService
{
    Task<MeetingAnalysisResponse> AnalyseAsync(
        string meetingNotes,
        NekomataWorkspace workspace);
}