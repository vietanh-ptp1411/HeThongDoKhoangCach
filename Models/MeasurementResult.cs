using System.Globalization;
using System.Text.Json.Serialization;

namespace HeThongDoKhoangCach.Models;

/// <summary>Một kết quả đo đã lưu (một dòng trong bảng ExportResultData).</summary>
public sealed class MeasurementResult
{
    public int No { get; set; }
    public string OrderNo { get; set; } = "";
    public string Line { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    /// <summary>Lực căng (gf) tại thời điểm đo.</summary>
    public double Force { get; set; }
    /// <summary>Khoảng cách đo được (mm).</summary>
    public double Value { get; set; }
    public double Lsl { get; set; }
    public double Usl { get; set; }
    public bool IsOk { get; set; }
    public DateTime InspectedAt { get; set; }
    public string Inspector { get; set; } = "";
    /// <summary>Ghi chú của người kiểm tra (ô GHI CHÚ).</summary>
    public string Note { get; set; } = "";
    /// <summary>Tên máy tính thực hiện đo.</summary>
    public string PcName { get; set; } = "";

    [JsonIgnore] public string ResultText => IsOk ? "OK" : "NG";
    [JsonIgnore] public string ItemName => "Khoảng cách";
    [JsonIgnore] public string SpecText => string.Create(CultureInfo.InvariantCulture, $"{Lsl:0.#} ~ {Usl:0.#}");
}
