using System.Globalization;
using System.Text.RegularExpressions;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services;

/// <summary>
/// Tạo nội dung mã QR của kết quả đo theo mẫu trong CÀI ĐẶT, vd "{Value}" → "3.70",
/// "{Serial};{Model};{Value};{Result}" → "985X57200-00123;CPX;3.70;OK". Tên trường không phân biệt hoa thường.
/// </summary>
public static partial class ResultQrFormatter
{
    public const string DefaultTemplate = "{Value}";

    /// <summary>Các trường dùng được trong mẫu (hiển thị ở màn cài đặt).</summary>
    public static readonly string[] Fields =
        ["Value", "Serial", "Model", "Spec", "Lsl", "Usl", "Result", "Force", "Date", "Time", "DateTime", "Inspector"];

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex FieldRegex();

    /// <summary>Mẫu cũ dùng thông tin đã bỏ được đưa về giá trị đo; QR trong lịch sử không thay đổi.</summary>
    public static string NormalizeTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return DefaultTemplate;
        return FieldRegex().Matches(template).Any(m =>
            m.Groups[1].Value.Equals("OrderNo", StringComparison.OrdinalIgnoreCase) ||
            m.Groups[1].Value.Equals("Line", StringComparison.OrdinalIgnoreCase)) ? DefaultTemplate : template;
    }

    public static string Format(string? template, MeasurementResult r)
    {
        var t = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;
        var inv = CultureInfo.InvariantCulture;
        return FieldRegex().Replace(t, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "value" => r.Value.ToString("0.00", inv),
            "serial" => r.Serial,
            "model" => r.Model,
            "spec" => r.SpecText,
            "lsl" => r.Lsl.ToString("0.##", inv),
            "usl" => r.Usl.ToString("0.##", inv),
            "result" => r.ResultText,
            "force" => r.Force.ToString("0.00", inv),
            "date" => r.InspectedAt.ToString("yyyy-MM-dd", inv),
            "time" => r.InspectedAt.ToString("HH:mm:ss", inv),
            "datetime" => r.InspectedAt.ToString("yyyy-MM-dd HH:mm:ss", inv),
            "inspector" => r.Inspector,
            _ => m.Value,   // trường lạ giữ nguyên để người cài đặt thấy ngay
        });
    }

    /// <summary>Kết quả mẫu để xem trước nội dung QR trong màn cài đặt.</summary>
    public static MeasurementResult Sample() => new()
    {
        Serial = "985X57200-00123",
        Model = "CPX",
        Force = 2.0,
        Value = 3.70,
        Lsl = 3.5,
        Usl = 6,
        IsOk = true,
        InspectedAt = new DateTime(2026, 8, 25, 10, 30, 45),
        Inspector = "23474",
    };
}
