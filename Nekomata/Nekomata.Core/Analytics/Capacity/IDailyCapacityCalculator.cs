using Nekomata.Core.Analytics.Models;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Analytics.Capacity;

public interface IDailyCapacityCalculator
{
    DailyCapacity Calculate(
        NekomataWorkspace workspace);
}