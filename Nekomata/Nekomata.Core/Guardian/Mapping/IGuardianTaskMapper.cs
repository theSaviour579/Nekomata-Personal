using Nekomata.AI.Models.Actions;
using Nekomata.Models.Tasks;

namespace Nekomata.Core.Guardian.Mapping;

public interface IGuardianTaskMapper
{
    NekomataTask Map(
        ProposedTask proposedTask,
        long? projectId);
}