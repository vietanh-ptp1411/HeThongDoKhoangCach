using System.Collections.ObjectModel;
using System.Windows.Input;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.Services;

namespace HeThongDoKhoangCach.ViewModels;

/// <summary>Màn "Giao diện báo cáo": lưới kết quả có bộ lọc theo ngày, line, chủng loại, kết quả, từ khóa; xuất Excel.</summary>
public sealed class ReportViewModel : ViewModelBase
{
    public const string AllOption = "Tất cả";

    private readonly IReadOnlyList<MeasurementResult> _all;

    public ReportViewModel(IReadOnlyList<MeasurementResult> all)
    {
        _all = all;

        Lines = [AllOption, .. Distinct(all.Select(r => r.Line))];
        Models = [AllOption, .. Distinct(all.Select(r => r.Model))];
        Results = [AllOption, "OK", "NG"];

        _fromDate = all.Count == 0 ? DateTime.Today.AddDays(-30) : all.Min(r => r.InspectedAt).Date;
        _toDate = DateTime.Today;

        ApplyCommand = new RelayCommand(Apply);
        ClearCommand = new RelayCommand(Clear);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => Rows.Count > 0, ex => ShowMessage?.Invoke(ex.Message, true));

        Apply();
    }

    public Func<string, string?>? ChooseExportPath { get; set; }
    public Action<string, bool>? ShowMessage { get; set; }

    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<string> Models { get; }
    public IReadOnlyList<string> Results { get; }

    public ICommand ApplyCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ExportCommand { get; }

    public ObservableCollection<MeasurementResult> Rows { get; } = [];

    private DateTime? _fromDate;
    public DateTime? FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }

    private DateTime? _toDate;
    public DateTime? ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }

    private string _selectedLine = AllOption;
    public string SelectedLine { get => _selectedLine; set => SetProperty(ref _selectedLine, value); }

    private string _selectedModel = AllOption;
    public string SelectedModel { get => _selectedModel; set => SetProperty(ref _selectedModel, value); }

    private string _selectedResult = AllOption;
    public string SelectedResult { get => _selectedResult; set => SetProperty(ref _selectedResult, value); }

    private string _searchText = "";
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

    private string _summaryText = "";
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    private void Apply()
    {
        IEnumerable<MeasurementResult> q = _all;
        if (FromDate is { } from) q = q.Where(r => r.InspectedAt >= from.Date);
        if (ToDate is { } to) q = q.Where(r => r.InspectedAt < to.Date.AddDays(1));
        if (SelectedLine != AllOption) q = q.Where(r => string.Equals(r.Line.Trim(), SelectedLine, StringComparison.OrdinalIgnoreCase));
        if (SelectedModel != AllOption) q = q.Where(r => string.Equals(r.Model.Trim(), SelectedModel, StringComparison.OrdinalIgnoreCase));
        if (SelectedResult == "OK") q = q.Where(r => r.IsOk);
        else if (SelectedResult == "NG") q = q.Where(r => !r.IsOk);

        var s = SearchText.Trim();
        if (s.Length > 0)
        {
            q = q.Where(r => r.Serial.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || r.OrderNo.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || r.Inspector.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || r.Note.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.OrderByDescending(r => r.InspectedAt).ThenByDescending(r => r.No).ToList();
        Rows.Clear();
        foreach (var r in list) Rows.Add(r);

        int ok = list.Count(r => r.IsOk);
        SummaryText = $"{list.Count} kết quả   |   OK: {ok}   |   NG: {list.Count - ok}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Clear()
    {
        FromDate = _all.Count == 0 ? DateTime.Today.AddDays(-30) : _all.Min(r => r.InspectedAt).Date;
        ToDate = DateTime.Today;
        SelectedLine = SelectedModel = SelectedResult = AllOption;
        SearchText = "";
        Apply();
    }

    private async Task ExportAsync()
    {
        var path = ChooseExportPath?.Invoke($"BaoCao_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        if (string.IsNullOrEmpty(path)) return;
        var rows = Rows.OrderBy(r => r.No).ToList();
        await Task.Run(() => ExcelExporter.Export(rows, path));
        ShowMessage?.Invoke($"Đã xuất {rows.Count} dòng ra file:\n{path}", false);
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> values)
        => values.Select(v => v.Trim())
                 .Where(v => v.Length > 0)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
}
