using System.Collections.ObjectModel;
using System.Windows.Input;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.Services;

namespace BeltTensionMeasurement.ViewModels;

/// <summary>Màn báo cáo: lọc theo ngày, chủng loại, mục đích, kết quả, từ khóa; xuất Excel/PDF.</summary>
public sealed class ReportViewModel : ViewModelBase
{
    public const string AllOption = "Tất cả";

    private readonly IReadOnlyList<MeasurementResult> _all;
    private string _appliedFilterText = "";

    public ReportViewModel(IReadOnlyList<MeasurementResult> all)
    {
        _all = all;

        Models = [AllOption, .. Distinct(all.Select(r => r.Model))];
        Results = [AllOption, "OK", "NG"];
        Purposes = [AllOption, .. Distinct(MeasurementPurpose.All.Select(p => p.Name).Concat(all.Select(r => r.Purpose ?? "")))];

        _fromDate = all.Count == 0 ? DateTime.Today.AddDays(-30) : all.Min(r => r.InspectedAt).Date;
        _toDate = DateTime.Today;

        ApplyCommand = new RelayCommand(Apply);
        ClearCommand = new RelayCommand(Clear);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => Rows.Count > 0, ex => ShowMessage?.Invoke(ex.Message, true));
        ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, () => Rows.Count > 0, ex => ShowMessage?.Invoke(ex.Message, true));

        Apply();
    }

    public Func<string, string?>? ChooseExportPath { get; set; }
    public Action<string, bool>? ShowMessage { get; set; }

    public IReadOnlyList<string> Models { get; }
    public IReadOnlyList<string> Results { get; }
    public IReadOnlyList<string> Purposes { get; }
    private string _selectedPurpose = AllOption;
    public string SelectedPurpose { get => _selectedPurpose; set => SetProperty(ref _selectedPurpose, value); }

    public ICommand ApplyCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ExportPdfCommand { get; }

    public ObservableCollection<MeasurementResult> Rows { get; } = [];

    private DateTime? _fromDate;
    public DateTime? FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }

    private DateTime? _toDate;
    public DateTime? ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }

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
        if (SelectedModel != AllOption) q = q.Where(r => string.Equals(r.Model.Trim(), SelectedModel, StringComparison.OrdinalIgnoreCase));
        if (SelectedPurpose != AllOption) q = q.Where(r => r.Purpose == SelectedPurpose);
        if (SelectedResult == "OK") q = q.Where(r => r.IsOk);
        else if (SelectedResult == "NG") q = q.Where(r => !r.IsOk);

        var s = SearchText.Trim();
        if (s.Length > 0)
        {
            q = q.Where(r => r.Serial.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || r.Inspector.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || (r.Purpose ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                          || (r.QrText ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                          || r.Note.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.OrderByDescending(r => r.InspectedAt).ThenByDescending(r => r.No).ToList();
        Rows.Clear();
        foreach (var r in list) Rows.Add(r);

        int ok = list.Count(r => r.IsOk);
        SummaryText = $"{list.Count} kết quả   |   OK: {ok}   |   NG: {list.Count - ok}";
        _appliedFilterText = $"Từ: {FromDate?.ToString("dd/MM/yyyy") ?? AllOption} – Đến: {ToDate?.ToString("dd/MM/yyyy") ?? AllOption}"
            + $" | Dòng hàng: {SelectedModel} | Mục đích: {SelectedPurpose} | Kết quả: {SelectedResult}"
            + (s.Length > 0 ? $" | Từ khóa: {s}" : "");
        CommandManager.InvalidateRequerySuggested();
    }

    private void Clear()
    {
        FromDate = _all.Count == 0 ? DateTime.Today.AddDays(-30) : _all.Min(r => r.InspectedAt).Date;
        ToDate = DateTime.Today;
        SelectedModel = SelectedResult = SelectedPurpose = AllOption;
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

    private async Task ExportPdfAsync()
    {
        // Xuất đúng bảng đã áp dụng bằng nút Tìm, kể cả khi bộ lọc đang được sửa nhưng chưa áp dụng.
        var rows = Rows.ToList();
        var filter = _appliedFilterText;
        if (rows.Count == 0) return;
        var path = ChooseExportPath?.Invoke($"BaoCao_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        if (string.IsNullOrEmpty(path)) return;
        await Task.Run(() => PdfExporter.Export(rows, path, filter));
        ShowMessage?.Invoke($"Đã xuất {rows.Count} kết quả ra PDF:\n{path}", false);
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> values)
        => values.Select(v => v.Trim())
                 .Where(v => v.Length > 0)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
}
