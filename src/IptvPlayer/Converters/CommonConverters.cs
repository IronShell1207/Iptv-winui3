using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace IptvPlayer.Converters;

/// <summary>true → Visible. Параметр «invert» переворачивает результат.</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value switch
        {
            bool b => b,
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            _ => true,
        };

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>Инвертирует bool — для IsEnabled и обратных состояний.</summary>
public sealed partial class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not true;
}

/// <summary>Путь к файлу логотипа → BitmapImage; пустой путь даёт null.</summary>
public sealed partial class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Шестнадцатеричный цвет «#RRGGBB» → кисть. Для обложек плейлистов.</summary>
public sealed partial class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || hex.Length < 7) return new SolidColorBrush(Colors.Gray);

        try
        {
            var r = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            return new SolidColorBrush(Color.FromArgb(255, r, g, b));
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Статус источника: true → зелёный, false → красный, null → серый.</summary>
public sealed partial class OnlineToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            true => "AppOnlineBrush",
            false => "AppOfflineBrush",
            _ => "AppTextTertiaryBrush",
        };

        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Доля 0..1 → ширина в пикселях относительно переданной ширины дорожки.</summary>
public sealed partial class ProgressToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var progress = value is double d ? d : 0;
        var total = parameter is string s && double.TryParse(s, out var t) ? t : 100;
        return Math.Clamp(progress, 0, 1) * total;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
