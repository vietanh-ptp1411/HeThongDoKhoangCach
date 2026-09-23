using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HeThongDoKhoangCach.ViewModels;

/// <summary>Chuỗi rỗng → Visible (dùng cho chữ gợi ý trong ô nhập), ngược lại Collapsed.</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public static EmptyToVisibleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
