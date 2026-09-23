namespace HeThongDoKhoangCach.Models;

/// <summary>
/// Một đường trên TREND CHART (thường là một Line sản xuất).
/// <see cref="Values"/> thẳng hàng với danh sách ngày của biểu đồ; NaN = ngày đó không có dữ liệu.
/// </summary>
public sealed record TrendSeries(string Name, IReadOnlyList<double> Values);
