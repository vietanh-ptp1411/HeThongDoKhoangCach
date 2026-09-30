using System.Globalization;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.ViewModels;

/// <summary>Một dòng thống kê (theo chủng loại hoặc theo line).</summary>
public sealed class StatRow
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Key { get; init; } = "";
    public string Group { get; init; } = "";
    public string Spec { get; init; } = "";
    public int Total { get; init; }
    public int Ok { get; init; }
    public int Ng { get; init; }
    public double Mean { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double StdDev { get; init; }
    public double? Cpk { get; init; }

    public double OkRate => Total == 0 ? 0 : Ok * 100.0 / Total;
    public string OkRateText => Total == 0 ? "-" : OkRate.ToString("0.0", Inv) + " %";
    public string MeanText => Total == 0 ? "-" : Mean.ToString("0.000", Inv);
    public string MinText => Total == 0 ? "-" : Min.ToString("0.000", Inv);
    public string MaxText => Total == 0 ? "-" : Max.ToString("0.000", Inv);
    public string StdDevText => Total < 2 ? "-" : StdDev.ToString("0.000", Inv);
    public string CpkText => Cpk is { } c ? c.ToString("0.00", Inv) : "-";
}

/// <summary>Màn THỐNG KÊ: tổng hợp OK/NG, trung bình, độ lệch chuẩn, Cpk theo chủng loại và theo line.</summary>
public sealed class StatisticsViewModel : ViewModelBase
{
    public StatisticsViewModel(IReadOnlyList<MeasurementResult> all, IReadOnlyList<SpecDefinition> specs)
    {
        // Giữ giới hạn đã lưu của từng kết quả; không áp quy cách mới cho lịch sử cũ.
        ByModel = all.GroupBy(r => (r.Model, r.Lsl, r.Usl))
            .OrderBy(g => g.Key.Model).ThenBy(g => g.Key.Lsl)
            .Select(g =>
            {
                var spec = new SpecDefinition { Lsl = g.Key.Lsl, Usl = g.Key.Usl };
                return Build(g.Key.Model, g.ToList(), spec, spec.Name, spec.Name);
            }).ToList();

        ByLine = all.GroupBy(r => r.Line.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => Build(g.Key.Length == 0 ? "(không rõ)" : g.Key, g.ToList(), null, "", ""))
                    .ToList();

        Overall = Build("Tổng cộng", all.ToList(), null, "", "");
        SummaryText = $"Tổng: {Overall.Total}   |   OK: {Overall.Ok}   |   NG: {Overall.Ng}   |   Tỉ lệ OK: {Overall.OkRateText}"
                    + (all.Count > 0 ? $"   |   Từ {all.Min(r => r.InspectedAt):dd/MM/yyyy} đến {all.Max(r => r.InspectedAt):dd/MM/yyyy}" : "");
    }

    public IReadOnlyList<StatRow> ByModel { get; }
    public IReadOnlyList<StatRow> ByLine { get; }
    public StatRow Overall { get; }
    public string SummaryText { get; }

    private static StatRow Build(string key, List<MeasurementResult> rows, SpecDefinition? spec, string group, string specText)
    {
        int total = rows.Count;
        int ok = rows.Count(r => r.IsOk);
        double mean = 0, min = 0, max = 0, sd = 0;
        double? cpk = null;

        if (total > 0)
        {
            var v = rows.Select(r => r.Value).ToList();
            mean = v.Average();
            min = v.Min();
            max = v.Max();
            if (total > 1)
            {
                sd = Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (total - 1));
                if (spec is not null && sd > 1e-9)
                    cpk = Math.Min(spec.Usl - mean, mean - spec.Lsl) / (3 * sd);
            }
        }

        return new StatRow
        {
            Key = key, Group = group, Spec = specText,
            Total = total, Ok = ok, Ng = total - ok,
            Mean = mean, Min = min, Max = max, StdDev = sd, Cpk = cpk,
        };
    }
}
