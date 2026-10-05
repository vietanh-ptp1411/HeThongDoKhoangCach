namespace BeltTensionMeasurement.Models;

public enum ScanField { Serial, Inspector, Purpose }

public sealed record MeasurementPurpose(int Code, string Name)
{
    public string DisplayName => $"{Code}. {Name}";
    public static IReadOnlyList<MeasurementPurpose> All { get; } =
    [
        new(1, "Đo kiểm tra đặc thù hằng ngày"),
        new(2, "Đo khởi đầu công việc"),
        new(3, "Điều tra lỗi"),
        new(4, "Yêu cầu các phòng ban"),
        new(5, "Khác"),
    ];

    // Barcode mục đích: 1..5, PURPOSE:1..5 hoặc đúng tên hiển thị trong danh sách.
    public static MeasurementPurpose? Parse(string text)
    {
        text = text.Trim();
        if (text.StartsWith("PURPOSE:", StringComparison.OrdinalIgnoreCase)) text = text[8..].Trim();
        return All.FirstOrDefault(p => text == p.Code.ToString() ||
            text.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
            text.Equals(p.DisplayName, StringComparison.OrdinalIgnoreCase));
    }
}
