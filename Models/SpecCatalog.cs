using System.Globalization;
using System.Collections.ObjectModel;

namespace BeltTensionMeasurement.Models;

/// <summary>
/// Một quy cách trong database – một dòng của "Bảng Điều kiện và Quy cách đo lực căng dây curoa motor":
/// giới hạn LSL ~ USL, điều kiện xác nhận cân bằng dây và các model (chủng loại) đo theo quy cách này.
/// </summary>
public sealed class SpecDefinition
{
    public int Id { get; set; }
    /// <summary>Số thứ tự (cột No) – thứ tự hiển thị trong danh sách chọn và histogram.</summary>
    public int SortOrder { get; set; }
    public double Lsl { get; set; }
    public double Usl { get; set; }
    public string Unit { get; set; } = "mm";
    /// <summary>Điều kiện xác nhận cân bằng dây (vd "Tác động lực 2gf").</summary>
    public string Condition { get; set; } = "";
    public ObservableCollection<ModelDefinition> Models { get; set; } = [];

    /// <summary>Tên hiển thị trong ô chọn Quy cách: "3 ~ 4 mm", "3.5 ~ 6 mm".</summary>
    public string Name => string.Create(CultureInfo.InvariantCulture, $"{Lsl:0.##} ~ {Usl:0.##} {Unit}");

    /// <summary>Kết quả đo có giới hạn LSL/USL trùng với quy cách này (dùng gộp histogram, thống kê).</summary>
    public bool Matches(double lsl, double usl) => Math.Abs(Lsl - lsl) < 1e-6 && Math.Abs(Usl - usl) < 1e-6;

    public override string ToString() => Name;
}

/// <summary>Một model (chủng loại) thuộc một quy cách, kèm bit PLC được bật khi chọn model này.</summary>
public sealed class ModelDefinition
{
    public int Id { get; set; }
    /// <summary>Vị trí trong cột Model 1..12 của bảng quy cách.</summary>
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Bit PLC bật khi chọn model (GSM M40, PP1 M41, M1/M2 M42, NF M43, CPX M44...). Trống = không gửi.</summary>
    public string PlcBit { get; set; } = "";

    public bool HasPlcBit => !string.IsNullOrWhiteSpace(PlcBit);

    public override string ToString() => Name;
}

/// <summary>Dữ liệu mặc định và hàm so khớp tên cho bảng quy cách – model.</summary>
public static class SpecCatalog
{
    public const string DefaultCondition = "Tác động lực 2gf";

    /// <summary>
    /// Bảng quy cách theo tài liệu khách hàng (GIAO DIỆN.pdf): 3~4 mm: LCP; 4~5 mm: M1, M2, NF, PP1; 3.5~6 mm: GSM, CPX.
    /// Bit PLC giữ theo isue_MC (GSM M40, PP1 M41, M1-M2 M42, NF83 M43, CPX M44); LCP là model phát sinh → M45.
    /// </summary>
    public static List<SpecDefinition> Defaults() =>
    [
        new()
        {
            SortOrder = 1, Lsl = 3, Usl = 4, Condition = DefaultCondition,
            Models = [new() { SortOrder = 1, Name = "LCP", PlcBit = "M45" }],
        },
        new()
        {
            SortOrder = 2, Lsl = 4, Usl = 5, Condition = DefaultCondition,
            Models =
            [
                new() { SortOrder = 1, Name = "M1", PlcBit = "M42" },
                new() { SortOrder = 2, Name = "M2", PlcBit = "M42" },
                new() { SortOrder = 3, Name = "NF", PlcBit = "M43" },
                new() { SortOrder = 4, Name = "PP1", PlcBit = "M41" },
            ],
        },
        new()
        {
            SortOrder = 3, Lsl = 3.5, Usl = 6, Condition = DefaultCondition,
            Models =
            [
                new() { SortOrder = 1, Name = "GSM", PlcBit = "M40" },
                new() { SortOrder = 2, Name = "CPX", PlcBit = "M44" },
            ],
        },
    ];

    /// <summary>Chuẩn hóa tên model để so khớp (bỏ khoảng trắng, dấu chấm, gạch; không phân biệt hoa thường).</summary>
    public static string NormalizeModel(string model)
        => new string((model ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// Tìm model trong danh sách theo tên ghi trong kết quả: khớp sau chuẩn hóa ("NF 8.3" = "NF83"), nếu không có
    /// thì tên cũ bắt đầu bằng tên trong database ("NF83" → "NF", "M1-M2" → "M1", "GSM RUP" → "GSM"), ưu tiên tên dài nhất.
    /// </summary>
    public static ModelDefinition? FindModel(IEnumerable<ModelDefinition> models, string name)
    {
        var n = NormalizeModel(name);
        if (n.Length == 0) return null;
        var list = models.Select(m => (Model: m, Key: NormalizeModel(m.Name))).Where(x => x.Key.Length > 0).ToList();
        return list.FirstOrDefault(x => x.Key == n).Model
               ?? list.Where(x => n.StartsWith(x.Key, StringComparison.Ordinal))
                      .OrderByDescending(x => x.Key.Length)
                      .Select(x => x.Model)
                      .FirstOrDefault();
    }

    /// <summary>Tất cả bit model khác nhau trong database (để tắt các bit không được chọn).</summary>
    public static IReadOnlyList<string> AllPlcBits(IEnumerable<SpecDefinition> specs)
        => specs.SelectMany(s => s.Models)
                .Where(m => m.HasPlcBit)
                .Select(m => m.PlcBit.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}
