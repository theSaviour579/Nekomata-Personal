using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Models.AI;

namespace Nekomata.UI.ViewModels;

public partial class GuardianReviewViewModel
    : ObservableObject
{
    [ObservableProperty]
    private GuardianTaskActionPlan actionPlan;

    public GuardianReviewViewModel(
        GuardianTaskActionPlan actionPlan)
    {
        ActionPlan = actionPlan;
    }

    public int SelectedCount =>
        ActionPlan.Actions.Count(action => action.Selected);

    public int TotalCount =>
        ActionPlan.Actions.Count;

}