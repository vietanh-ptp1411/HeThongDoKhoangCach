using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HeThongDoKhoangCach.Controls;

/// <summary>
/// Histogram theo mock BISG: trục Y "Số lượng", trục X "Khoảng cách (mm)", vạch LSL/USL đứt nét,
/// dòng "Quy cách: ..." góc dưới phải. Vẽ trực tiếp bằng DrawingContext.
/// </summary>
public sealed class HistogramControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(HistogramControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LslProperty = DependencyProperty.Register(
        nameof(Lsl), typeof(double), typeof(HistogramControl),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UslProperty = DependencyProperty.Register(
        nameof(Usl), typeof(double), typeof(HistogramControl),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BinCountProperty = DependencyProperty.Register(
        nameof(BinCount), typeof(int), typeof(HistogramControl),
        new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpecTextProperty = DependencyProperty.Register(
        nameof(SpecText), typeof(string), typeof(HistogramControl),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty XAxisTitleProperty = DependencyProperty.Register(
        nameof(XAxisTitle), typeof(string), typeof(HistogramControl),
        new FrameworkPropertyMetadata("Khoảng cách (mm)", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YAxisTitleProperty = DependencyProperty.Register(
        nameof(YAxisTitle), typeof(string), typeof(HistogramControl),
        new FrameworkPropertyMetadata("Số lượng", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(HistogramControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#2A5DB0"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LslBrushProperty = DependencyProperty.Register(
        nameof(LslBrush), typeof(Brush), typeof(HistogramControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#E67E22"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UslBrushProperty = DependencyProperty.Register(
        nameof(UslBrush), typeof(Brush), typeof(HistogramControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#D32F2F"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
        nameof(AxisBrush), typeof(Brush), typeof(HistogramControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#5A6B7F"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(HistogramControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#E3E8EF"), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Lsl { get => (double)GetValue(LslProperty); set => SetValue(LslProperty, value); }
    public double Usl { get => (double)GetValue(UslProperty); set => SetValue(UslProperty, value); }
    public int BinCount { get => (int)GetValue(BinCountProperty); set => SetValue(BinCountProperty, value); }
    public string SpecText { get => (string)GetValue(SpecTextProperty); set => SetValue(SpecTextProperty, value); }
    public string XAxisTitle { get => (string)GetValue(XAxisTitleProperty); set => SetValue(XAxisTitleProperty, value); }
    public string YAxisTitle { get => (string)GetValue(YAxisTitleProperty); set => SetValue(YAxisTitleProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush LslBrush { get => (Brush)GetValue(LslBrushProperty); set => SetValue(LslBrushProperty, value); }
    public Brush UslBrush { get => (Brush)GetValue(UslBrushProperty); set => SetValue(UslBrushProperty, value); }
    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 60) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

        const double padLeft = 36, padRight = 14, padTop = 30, padBottom = 40;
        double plotW = w - padLeft - padRight;
        double plotH = h - padTop - padBottom;
        double baseY = padTop + plotH;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var values = (Values ?? []).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
        bool hasLsl = !double.IsNaN(Lsl);
        bool hasUsl = !double.IsNaN(Usl);

        // ----- Phạm vi trục X -----
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        if (values.Length > 0) { lo = values.Min(); hi = values.Max(); }
        if (hasLsl) { lo = Math.Min(lo, Lsl); hi = Math.Max(hi, Lsl); }
        if (hasUsl) { lo = Math.Min(lo, Usl); hi = Math.Max(hi, Usl); }
        if (double.IsInfinity(lo)) { lo = 0; hi = 10; }
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        double pad = (hi - lo) * 0.2;
        lo -= pad;
        hi += pad;
        double span = hi - lo;

        // ----- Phân bin -----
        int n = Math.Max(BinCount, 2);
        var counts = new int[n];
        foreach (var v in values)
        {
            int idx = (int)((v - lo) / span * n);
            counts[Math.Clamp(idx, 0, n - 1)]++;
        }
        int maxCount = Math.Max(1, counts.Max());

        // ----- Trục Y: lưới + nhãn -----
        double yStep = Math.Max(1, ChartMath.NiceStep(maxCount / 5.0));
        double yMax = Math.Ceiling(maxCount / yStep) * yStep;
        if (yMax <= 0) yMax = yStep;
        var gridPen = ChartMath.FrozenPen(GridBrush, 1);
        var axisPen = ChartMath.FrozenPen(AxisBrush, 1);
        for (double yv = 0; yv <= yMax + 1e-9; yv += yStep)
        {
            double y = baseY - yv / yMax * plotH;
            if (yv > 0) dc.DrawLine(gridPen, new Point(padLeft, Math.Round(y) + 0.5), new Point(w - padRight, Math.Round(y) + 0.5));
            var ft = ChartMath.Text(yv.ToString("0", CultureInfo.InvariantCulture), 9.5, AxisBrush, dip);
            dc.DrawText(ft, new Point(padLeft - 6 - ft.Width, y - ft.Height / 2));
        }
        dc.DrawLine(axisPen, new Point(padLeft, baseY + 0.5), new Point(w - padRight, baseY + 0.5));
        dc.DrawLine(axisPen, new Point(padLeft + 0.5, padTop - 4), new Point(padLeft + 0.5, baseY));

        var yTitle = ChartMath.Text(YAxisTitle, 9.5, AxisBrush, dip);
        dc.DrawText(yTitle, new Point(2, 4));

        // ----- Cột -----
        double binW = plotW / n;
        double barW = Math.Max(2, binW * 0.78);
        for (int i = 0; i < n; i++)
        {
            if (counts[i] == 0) continue;
            double bh = counts[i] / yMax * plotH;
            double x = padLeft + i * binW + (binW - barW) / 2;
            dc.DrawRectangle(BarBrush, null, new Rect(x, baseY - bh, barW, bh));
        }

        // ----- LSL / USL -----
        if (hasLsl) DrawLimit(dc, Lsl, "LSL", LslBrush, lo, span, padLeft, plotW, padTop, baseY, dip);
        if (hasUsl) DrawLimit(dc, Usl, "USL", UslBrush, lo, span, padLeft, plotW, padTop, baseY, dip);

        // ----- Trục X: nhãn -----
        double xStep = ChartMath.NiceStep(span / 8);
        double firstTick = Math.Ceiling(lo / xStep) * xStep;
        for (double xv = firstTick; xv <= hi + 1e-9; xv += xStep)
        {
            double x = padLeft + (xv - lo) / span * plotW;
            dc.DrawLine(axisPen, new Point(x, baseY), new Point(x, baseY + 3));
            var ft = ChartMath.Text(ChartMath.FormatTick(xv, xStep), 9.5, AxisBrush, dip);
            dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + 5));
        }

        var xTitle = ChartMath.Text(XAxisTitle, 9.5, AxisBrush, dip);
        dc.DrawText(xTitle, new Point(padLeft + (plotW - xTitle.Width) / 2, h - xTitle.Height - 2));

        if (!string.IsNullOrEmpty(SpecText))
        {
            var spec = ChartMath.Text(SpecText, 9.5, UslBrush, dip, bold: true);
            dc.DrawText(spec, new Point(w - padRight - spec.Width, h - spec.Height - 2));
        }

        // ----- Số mẫu / trạng thái trống -----
        if (values.Length == 0)
        {
            var ft = ChartMath.Text("Chưa có dữ liệu đo", 12, AxisBrush, dip);
            dc.DrawText(ft, new Point(padLeft + (plotW - ft.Width) / 2, padTop + (plotH - ft.Height) / 2));
        }
        else
        {
            var ft = ChartMath.Text($"n = {values.Length}", 9.5, AxisBrush, dip);
            dc.DrawText(ft, new Point(w - padRight - ft.Width, padTop - 2));
        }

        dc.Pop();
    }

    private static void DrawLimit(DrawingContext dc, double value, string label, Brush brush,
        double lo, double span, double left, double plotW, double top, double baseY, double dip)
    {
        double x = left + (value - lo) / span * plotW;
        var pen = ChartMath.FrozenPen(brush, 1.4, new DashStyle([4, 3], 0));
        dc.DrawLine(pen, new Point(x, top - 4), new Point(x, baseY));

        var ft = ChartMath.Text($"{label} {value.ToString("0.000", CultureInfo.InvariantCulture)}", 9.5, brush, dip, bold: true);
        double tx = Math.Clamp(x - ft.Width / 2, left, Math.Max(left, left + plotW - ft.Width));
        dc.DrawText(ft, new Point(tx, 4));
    }
}
