using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.Windows;

public partial class ProposalFeedbackWindow : Window
{
    private readonly bool _replacement;
    public string FeedbackReason { get; private set; } = "";
    public string Replacement { get; private set; } = "";

    public ProposalFeedbackWindow(string itemTitle, bool replacement, string replacementLabel = "REPLACEMENT TITLE OR VALUE")
    {
        InitializeComponent();
        _replacement = replacement;
        ItemText.Text = itemTitle;
        HeadingText.Text = replacement ? "REPLACE SUGGESTION" : "REMOVE SUGGESTION";
        ConfirmButton.Content = replacement ? "REPLACE & LEARN" : "REMOVE & LEARN";
        ReplacementPanel.Visibility = replacement ? Visibility.Visible : Visibility.Collapsed;
        ReplacementLabel.Text = replacementLabel;
        Height = replacement ? 470 : 375;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var selectedReason = (ReasonBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Not relevant to this plan";
        FeedbackReason = string.IsNullOrWhiteSpace(NotesBox.Text) ? selectedReason : $"{selectedReason}: {NotesBox.Text.Trim()}";
        Replacement = ReplacementBox.Text.Trim();
        if (_replacement && string.IsNullOrWhiteSpace(Replacement) && !ReplacementLabel.Text.Contains("CALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Enter the replacement that Guardian should use instead.", "Guardian proposal feedback");
            return;
        }
        DialogResult = true;
    }
}
