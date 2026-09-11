using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class TeamSkillsWindow : Window
{
    public TeamSkillsWindow(TeamSkillsViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
