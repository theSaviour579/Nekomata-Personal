using System.Windows;

namespace Nekomata.UI.Views;

public partial class MissionAnalysisWindow : Window
{
    public MissionAnalysisWindow(object dataContext)
    {
        InitializeComponent();

        DataContext = dataContext;
    }
}