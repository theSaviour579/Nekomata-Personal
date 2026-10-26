using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Core.Guardian.Autonomy;

namespace Nekomata.UI.ViewModels;

public sealed record AutonomyOption(AutonomyMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One row of the Guardian autonomy section on the status page: the setting, and what Guardian has done.</summary>
public sealed partial class AutonomySettingRow : ObservableObject
{
    private readonly Action<AutonomyAction, AutonomyMode> _apply;
    private bool _loading;

    public AutonomySettingRow(AutonomyActionInfo info, AutonomyMode current, Action<AutonomyAction, AutonomyMode> apply)
    {
        _apply = apply;
        Action = info.Action;
        Label = info.Label;
        Description = info.Description;
        Options = info.SupportsAsk
            ? [new(AutonomyMode.Auto, "Do it"), new(AutonomyMode.Ask, "Ask me"), new(AutonomyMode.Off, "Off")]
            : [new(AutonomyMode.Auto, "On"), new(AutonomyMode.Off, "Off")];
        _loading = true;
        SelectedOption = Options.FirstOrDefault(o => o.Mode == current) ?? Options[0];
        _loading = false;
    }

    public AutonomyAction Action { get; }
    public string Label { get; }
    public string Description { get; }
    public IReadOnlyList<AutonomyOption> Options { get; }

    [ObservableProperty] private AutonomyOption? selectedOption;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool needsAttention;

    public bool IsOff => SelectedOption?.Mode == AutonomyMode.Off;

    partial void OnSelectedOptionChanged(AutonomyOption? value)
    {
        OnPropertyChanged(nameof(IsOff));
        if (!_loading && value is not null) _apply(Action, value.Mode);
    }
}
