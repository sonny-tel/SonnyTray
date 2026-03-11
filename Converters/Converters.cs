using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SonnyTray.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var param = parameter as string ?? "";
        var invert = param == "Invert";
        bool visible = value switch
        {
            bool b => b,
            int i => i > 0,
            object o => o is not null,
            _ => false
        };
        if (invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class OnlineStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool online && online
            ? Application.Current.FindResource("GreenBrush")
            : Application.Current.FindResource("TextSecondaryBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ConnectionStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string state && state == "Running"
            ? Application.Current.FindResource("GreenBrush")
            : Application.Current.FindResource("RedBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BackendStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string state ? state switch
        {
            "Running" => "Connected",
            "Stopped" => "Disconnected",
            "Starting" => "Starting...",
            "NeedsLogin" => "Needs Login",
            "NeedsMachineAuth" => "Pending Auth",
            "NoState" => "Logged Out",
            _ => state
        } : "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                return new BitmapImage(new Uri(url, UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SearchFilterConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // values[0] = search query, values[1] = item text to match
        if (values.Length < 2) return Visibility.Visible;
        var query = values[0] as string ?? "";
        var text = values[1] as string ?? "";
        if (string.IsNullOrWhiteSpace(query)) return Visibility.Visible;
        return text.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
