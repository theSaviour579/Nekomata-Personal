using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class ProjectWindow : Window
{
    public ProjectWindow(ProjectWindowViewModel vm)
    {
        InitializeComponent();

        DataContext = vm;

        vm.CloseRequested += () =>
        {
            DialogResult = true;
            Close();
        };
    }
}