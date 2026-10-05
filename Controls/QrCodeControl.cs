using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace BeltTensionMeasurement.Controls;

/// <summary>
/// Vẽ mã QR của <see cref="Text"/> (thư viện QRCoder sinh ma trận, vẽ bằng DrawingContext).
/// Mỗi ô QR được làm tròn theo pixel màn hình để ảnh sắc nét – máy quét (ZEBRA DS8178) đọc được trực tiếp trên màn hình.
/// </summary>
public sealed class QrCodeControl : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(QrCodeControl),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((QrCodeControl)d)._matrix = null));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(QrCodeControl),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(QrCodeControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <summary>Ma trận ô QR (đã gồm viền trắng 4 ô), tạo lại khi Text đổi.</summary>
    private bool[][]? _matrix;

    protected override void OnRender(DrawingContext dc)
    {
        var text = Text ?? "";
        if (text.Length == 0) return;

        _matrix ??= Encode(text);
        int n = _matrix.Length;
        if (n == 0) return;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double size = Math.Min(ActualWidth, ActualHeight);
        int modulePx = (int)Math.Floor(size * scale / n);
        if (modulePx < 1) return;

        double m = modulePx / scale;
        double side = m * n;
        double x0 = Math.Round((ActualWidth - side) / 2 * scale) / scale;
        double y0 = Math.Round((ActualHeight - side) / 2 * scale) / scale;

        dc.DrawRectangle(Background, null, new Rect(x0, y0, side, side));

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (int row = 0; row < n; row++)
            {
                var line = _matrix[row];
                int col = 0;
                while (col < n)
                {
                    if (!line[col]) { col++; continue; }
                    int start = col;
                    while (col < n && line[col]) col++;
                    double x = x0 + start * m, y = y0 + row * m, w = (col - start) * m;
                    g.BeginFigure(new Point(x, y), true, true);
                    g.LineTo(new Point(x + w, y), false, false);
                    g.LineTo(new Point(x + w, y + m), false, false);
                    g.LineTo(new Point(x, y + m), false, false);
                }
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(Foreground, null, geometry);
    }

    private static bool[][] Encode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return data.ModuleMatrix.Select(row => Enumerable.Range(0, row.Length).Select(i => row[i]).ToArray()).ToArray();
    }
}
