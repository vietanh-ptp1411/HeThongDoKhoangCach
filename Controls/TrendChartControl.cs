using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Controls;

/// <summary>
/// TREND CHART theo mock BISG: mỗi Line một đường màu, trục X là ngày, trục Y là giá trị (mm),
/// đường "Giới hạn trên" (USL, liền) và "Giới hạn dưới" (LSL, đứt nét) màu đỏ, chú giải bên phải.
/// </summary>
public sealed class TrendChartControl : FrameworkElement
{
    private static readonly Brush[] Palette =
    [
        ChartMath.Frozen("#1F5FBF"), ChartMath.Frozen("#F28C28"), ChartMath.Frozen("#2E9E44"),
        ChartMath.Frozen("#7E3FBF"), ChartMath.Frozen("#2FA7D9"), ChartMath.Frozen("#C2185B"),
        ChartMath.Frozen("#6D4C41"), ChartMath.Frozen("#00897B"), ChartMath.Frozen("#F9A825"),
    ];

    public static readonly DependencyProperty CategoriesProperty = DependencyProperty.Register(
        nameof(Categories), typeof(IReadOnlyList<DateTime>), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<TrendSeries>), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UslProperty = DependencyProperty.Register(
        nameof(Usl), typeof(double), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LslProperty = DependencyProperty.Register(
        nameof(Lsl), typeof(double), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
        nameof(AxisBrush), typeof(Brush), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#5A6B7F"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#E3E8EF"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LimitBrushProperty = DependencyProperty.Register(
        nameof(LimitBrush), typeof(Brush), typeof(TrendChartControl),
        new FrameworkPropertyMetadata(ChartMath.Frozen("#D32F2F"), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DateTime>? Categories { get => (IReadOnlyList<DateTime>?)GetValue(CategoriesProperty); set => SetValue(CategoriesProperty, value); }
    public IReadOnlyList<TrendSeries>? Series { get => (IReadOnlyList<TrendSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public double Usl { get => (double)GetValue(UslProperty); set => SetValue(UslProperty, value); }
    public double Lsl { get => (double)GetValue(LslProperty); set => SetValue(LslProperty, value); }
    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LimitBrush { get => (Brush)GetValue(LimitBrushProperty); set => SetValue(LimitBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 60) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

        const double padLeft = 40, padTop = 12, padBottom = 26, legendW = 118, gap = 10;
        double plotW = w - padLeft - gap - legendW;
        double plotH = h - padTop - padBottom;
        double baseY = padTop + plotH;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var categories = Categories ?? [];
        var series = Series ?? [];
        bool hasUsl = !double.IsNaN(Usl), hasLsl = !double.IsNaN(Lsl);

        // ----- Phạm vi trục Y -----
        var all = series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).ToList();
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        if (all.Count > 0) { lo = all.Min(); hi = all.Max(); }
        if (hasUsl) { lo = Math.Min(lo, Usl); hi = Math.Max(hi, Usl); }
        if (hasLsl) { lo = Math.Min(lo, Lsl); hi = Math.Max(hi, Lsl); }
        if (double.IsInfinity(lo)) { lo = 0; hi = 10; }
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        double padV = (hi - lo) * 0.15;
        lo -= padV;
        hi += padV;
        double yStep = ChartMath.NiceStep((hi - lo) / 5);
        double yMin = Math.Floor(lo / yStep) * yStep;
        double yMax = Math.Ceiling(hi / yStep) * yStep;
        double ySpan = yMax - yMin;
        double Y(double v) => baseY - (v - yMin) / ySpan * plotH;

        // ----- Lưới + nhãn Y -----
        var gridPen = ChartMath.FrozenPen(GridBrush, 1);
        var axisPen = ChartMath.FrozenPen(AxisBrush, 1);
        for (double yv = yMin; yv <= yMax + 1e-9; yv += yStep)
        {
            double y = Math.Round(Y(yv)) + 0.5;
            dc.DrawLine(gridPen, new Point(padLeft, y), new Point(padLeft + plotW, y));
            var ft = ChartMath.Text(ChartMath.FormatTick(yv, yStep), 9.5, AxisBrush, dip);
            dc.DrawText(ft, new Point(padLeft - 6 - ft.Width, y - ft.Height / 2));
        }
        dc.DrawLine(axisPen, new Point(padLeft + 0.5, padTop), new Point(padLeft + 0.5, baseY));
        dc.DrawLine(axisPen, new Point(padLeft, baseY + 0.5), new Point(padLeft + plotW, baseY + 0.5));

        // ----- Trục X -----
        int n = categories.Count;
        double X(int i) => n <= 1 ? padLeft + plotW / 2 : padLeft + i * (plotW / (n - 1));
        int labelEvery = n <= 12 ? 1 : (int)Math.Ceiling(n / 12.0);
        for (int i = 0; i < n; i++)
        {
            double x = X(i);
            dc.DrawLine(axisPen, new Point(x, baseY), new Point(x, baseY + 3));
            if (i % labelEvery != 0 && i != n - 1) continue;
            var ft = ChartMath.Text(categories[i].ToString("dd/MM", CultureInfo.InvariantCulture), 9.5, AxisBrush, dip);
            dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + 6));
        }

        // ----- Giới hạn -----
        var uslPen = ChartMath.FrozenPen(LimitBrush, 1.5);
        var lslPen = ChartMath.FrozenPen(LimitBrush, 1.5, new DashStyle([5, 3], 0));
        if (hasUsl) dc.DrawLine(uslPen, new Point(padLeft, Y(Usl)), new Point(padLeft + plotW, Y(Usl)));
        if (hasLsl) dc.DrawLine(lslPen, new Point(padLeft, Y(Lsl)), new Point(padLeft + plotW, Y(Lsl)));

        // ----- Các đường -----
        for (int s = 0; s < series.Count; s++)
        {
            var brush = Palette[s % Palette.Length];
            var pen = ChartMath.FrozenPen(brush, 1.8);
            var vals = series[s].Values;
            Point? prev = null;
            int run = 0;
            for (int i = 0; i < Math.Min(n, vals.Count); i++)
            {
                if (double.IsNaN(vals[i]))
                {
                    if (run == 1 && prev is { } lone) dc.DrawEllipse(brush, null, lone, 3, 3);
                    prev = null; run = 0;
                    continue;
                }
                var pt = new Point(X(i), Y(vals[i]));
                if (prev is { } p0) dc.DrawLine(pen, p0, pt);
                prev = pt; run++;
            }
            if (run == 1 && prev is { } last) dc.DrawEllipse(brush, null, last, 3, 3);
        }

        // ----- Chú giải -----
        double lx = padLeft + plotW + gap + 4;
        double ly = padTop + 2;
        const double rowH = 17;
        for (int s = 0; s < series.Count && ly + rowH < h; s++)
        {
            var brush = Palette[s % Palette.Length];
            dc.DrawLine(ChartMath.FrozenPen(brush, 2), new Point(lx, ly + 7), new Point(lx + 18, ly + 7));
            var ft = ChartMath.Text(series[s].Name, 9.5, AxisBrush, dip);
            dc.DrawText(ft, new Point(lx + 24, ly));
            ly += rowH;
        }
        if (hasUsl && ly + rowH < h)
        {
            dc.DrawLine(uslPen, new Point(lx, ly + 7), new Point(lx + 18, ly + 7));
            dc.DrawText(ChartMath.Text("Giới hạn trên", 9.5, AxisBrush, dip), new Point(lx + 24, ly));
            ly += rowH;
        }
        if (hasLsl && ly + rowH < h)
        {
            dc.DrawLine(lslPen, new Point(lx, ly + 7), new Point(lx + 18, ly + 7));
            dc.DrawText(ChartMath.Text("Giới hạn dưới", 9.5, AxisBrush, dip), new Point(lx + 24, ly));
        }

        if (all.Count == 0)
        {
            var ft = ChartMath.Text("Chưa có dữ liệu đo", 12, AxisBrush, dip);
            dc.DrawText(ft, new Point(padLeft + (plotW - ft.Width) / 2, padTop + (plotH - ft.Height) / 2));
        }

        dc.Pop();
    }
}
