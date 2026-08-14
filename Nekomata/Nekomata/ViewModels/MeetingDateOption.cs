using CommunityToolkit.Mvvm.ComponentModel;

namespace Nekomata.UI.ViewModels;

public partial class MeetingDateOption : ObservableObject
{
    public DateTime Date { get; init; }
    [ObservableProperty] private bool isSelected = true;
    public string Label => Date.ToString("ddd d MMM yyyy");
}