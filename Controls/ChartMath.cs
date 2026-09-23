using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HeThongDoKhoangCach.Controls;

/// <summary>Hàm dùng chung cho các control vẽ biểu đồ.</summary>
internal static class ChartMath
{
    public static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface SemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>Bước chia "đẹp" (1, 2, 5 × 10^n) gần với giá trị thô.</summary>
    public static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / exp;
        double nice = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10;
        return nice * exp;
    }

    /// <summary>Định dạng số theo độ mịn của bước chia (0.5 → "0.0", 0.05 → "0.00", 1 → "0").</summary>
    public static string FormatTick(double value, double step)
    {
        int decimals = step >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
        decimals = Math.Clamp(decimals, 0, 4);
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static FormattedText Text(string text, double size, Brush brush, double dip, bool bold = false)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? SemiBold : Regular, size, brush, dip);

    public static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static Pen FrozenPen(Brush brush, double thickness, DashStyle? dash = null)
    {
        var p = new Pen(brush, thickness);
        if (dash is not null) p.DashStyle = dash;
        p.Freeze();
        return p;
    }
}
