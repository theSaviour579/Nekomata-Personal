using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.Converters;

public sealed class DiagnosticsVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is WorkspaceMode.Diagnostics ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
