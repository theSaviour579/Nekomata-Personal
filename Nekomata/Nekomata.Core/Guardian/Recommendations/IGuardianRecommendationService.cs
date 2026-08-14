using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Recommendations;

public interface IGuardianRecommendationService
{
    GuardianDashboardRecommendation? GetTopRecommendation(
        NekomataWorkspace workspace);
}