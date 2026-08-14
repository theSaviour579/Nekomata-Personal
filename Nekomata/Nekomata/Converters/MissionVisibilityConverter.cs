using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.Converters;

public class MissionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is WorkspaceMode.Mission
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}