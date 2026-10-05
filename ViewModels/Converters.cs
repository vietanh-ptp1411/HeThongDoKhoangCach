using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BeltTensionMeasurement.ViewModels;

/// <summary>Chuỗi rỗng → Visible (dùng cho chữ gợi ý trong ô nhập), ngược lại Collapsed.</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public static EmptyToVisibleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Chuỗi có nội dung → Visible, rỗng → Collapsed (nhãn bit PLC cạnh chủng loại).</summary>
public sealed class NotEmptyToVisibleConverter : IValueConverter
{
    public static NotEmptyToVisibleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
