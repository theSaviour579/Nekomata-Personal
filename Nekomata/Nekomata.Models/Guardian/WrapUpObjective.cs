namespace Nekomata.Models.Guardian;

public class WrapUpObjective : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; set; } = "";
    public long? TaskId { get; set; }
    public string Title { get; set; } = "";
    private string _decision = "No change";
    public string Decision
    {
        get => _decision;
        set
        {
            if (_decision == value) return;
            _decision = value;
            PropertyChanged?.Invoke(this, new(nameof(Decision)));
        }
    }
    public DateTime? DeferUntil { get; set; }
    public DateTime PlanDate { get; set; }
}
