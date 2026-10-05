using System.IO;
using System.Text.Json;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.Services;
using BeltTensionMeasurement.ViewModels;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static async Task CheckAutoSaveAndPdf()
    {
        // Cấu hình cũ từng tắt tự lưu không được vô hiệu luồng mới.
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"AutoSaveOnMeasureDone\":false,\"AutoConnectPlc\":false}")!;
        settings.DatabaseFilePath = Path.Combine(Output, "autosave.db");
        settings.ResultFilePath = Path.Combine(Output, "autosave.jsonl");
        Check(!JsonSerializer.Serialize(settings).Contains("AutoSaveOnMeasureDone"), "Old manual-save flag is ignored and omitted from new settings");
        await using var vm = new MainViewModel(settings);
        vm.SelectedSpec = vm.Specs[2]; vm.SelectedModel = vm.AvailableModels[1];
        vm.QrInput = "AUTO-FAIL-001"; await CompleteScan(vm);
        var monitor = (PlcMonitor)typeof(MainViewModel).GetField("_monitor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
        SetField(monitor, "_client", new RecordingClient());
        Invoke(vm, "OnConnectionChanged", true); Poll(vm, false);
        await vm.StartAsync(); // Chạy timer kể cả khi không tự kết nối PLC.
        var locked = new FileStream(settings.ResultFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            vm.StartCommand.Execute(null); Poll(vm, true, 4.2);
            Check(vm.RecentRows.Count == 0 && !vm.CanEditSelection && vm.ResultQrText == "4.20", "Write failure retains completed result and blocks the next measurement");
            await Call(vm, "ResetAsync");
            Check(vm.Serial == "AUTO-FAIL-001" && vm.MeasuredValue == 4.2 && !vm.TryFlushPendingResults(), "RESET and close flush cannot discard a result that failed to save");
        }
        finally { locked.Dispose(); }
        await Until(() => vm.RecentRows.Count == 1);
        Check(vm.CanEditSelection && vm.RecentRows[0].Serial == "AUTO-FAIL-001", "Automatic timer retry saves after file access is restored without a save button");
        vm.TryFlushPendingResults();
        var store = new ResultStore(settings.ResultFilePath); store.Load();
        Check(store.All.Count == 1 && store.All[0].QrText == "4.20", "Automatic retry persists exactly one result with its original QR");

        vm.SerialInput = "EXCLUDED-PDF-OTHER-DAY";
        vm.StartCommand.Execute(null); Poll(vm, false);
        Invoke(vm, "OnSnapshot", new PlcSnapshot { MeasureDone = true, ResultValue = 4.5, Force = 2,
            JudgeOk = true, Running = false, Timestamp = store.All[0].InspectedAt.AddDays(1) });
        Check(vm.RecentRows.Count == 2, "PDF filter test has measurements on two different dates");

        // Kiểm tra lệnh PDF dùng dữ liệu đã lọc và ngày lọc được chốt trước hộp thoại.
        string filteredPdf = Path.Combine(Output, "filtered-report.pdf");
        string requestedName = "";
        vm.ChooseExportPath = name => { requestedName = name; return filteredPdf; };
        // Dữ liệu Poll có thời gian 30/09/2026; chọn đúng ngày theo kết quả thực tế.
        var measuredDate = store.All[0].InspectedAt.Date;
        vm.FromDate = measuredDate; vm.ToDate = measuredDate;
        string? message = null;
        vm.ShowMessage = (text, error) => { if (error) throw new Exception(text); message = text; };
        await Call(vm, "ExportPdfAsync");
        using (var pdf = PdfReader.Open(filteredPdf, PdfDocumentOpenMode.Import))
            Check(requestedName.EndsWith(".pdf") && pdf.PageCount == 1 && message?.Contains("1 kết quả") == true, "PDF command creates a readable PDF of the selected date range");
        vm.FromDate = measuredDate.AddDays(2); vm.ToDate = measuredDate.AddDays(2);
        Check(!vm.ExportPdfCommand.CanExecute(null), "PDF button is disabled when the date filter has no results");
        vm.FromDate = measuredDate; vm.ToDate = measuredDate;
        vm.ChooseExportPath = _ => null;
        message = null; await Call(vm, "ExportPdfAsync");
        Check(message is null, "Canceling the PDF save dialog does not report an export");

        var samples = Enumerable.Range(1, 22).Select(i => new MeasurementResult
        {
            No = i, Serial = $"PDF-SERIAL-{i:000}", Model = "CPX", Inspector = "NV-ĐỖ-001",
            Value = 3.7, Force = 2, Lsl = 3.5, Usl = 6, IsOk = i % 4 != 0,
            PurposeCode = 1, Purpose = "Đo kiểm tra đặc thù hằng ngày",
            InspectedAt = new DateTime(2026, 10, 1, 10, 0, 0).AddMinutes(i),
            QrText = i == 22 ? "" : $"PDF-SERIAL-{i:000};3.70",
        }).ToList();
        samples[20].Serial = new string('S', 256);
        var allPdf = Path.Combine(Output, "pdf-multiple-pages.pdf");
        await Task.Run(() => PdfExporter.Export(samples, allPdf, "01/10/2026", "MVA Lab"));
        using (var pdf = PdfReader.Open(allPdf, PdfDocumentOpenMode.Import))
            Check(pdf.PageCount > 1 && pdf.Info.Title.Contains("khoảng cách"), "PDF paginates multiple measurements and supports long serials and legacy rows without QR");
        var pdfBytes = File.ReadAllBytes(allPdf);
        try { PdfExporter.Export([], allPdf, "empty"); throw new Exception("Empty PDF export was accepted"); }
        catch (ArgumentException) { }
        Check(File.ReadAllBytes(allPdf).SequenceEqual(pdfBytes), "Invalid empty export does not overwrite an existing PDF");
        Check(!Directory.GetFiles(Output, "*.tmp").Any(), "PDF export cleans up temporary files");

        var report = new ReportViewModel(samples)
        {
            FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 1),
            SelectedModel = "CPX", SelectedPurpose = samples[0].Purpose, SelectedResult = "OK", SearchText = "PDF-SERIAL-001",
        };
        report.ApplyCommand.Execute(null);
        string reportPdf = Path.Combine(Output, "report-filtered.pdf");
        string? reportMessage = null;
        report.ChooseExportPath = name => name.EndsWith(".pdf") ? reportPdf : throw new Exception("Expected PDF filename");
        report.ShowMessage = (text, error) => { if (error) throw new Exception(text); reportMessage = text; };
        Check(report.ExportPdfCommand.CanExecute(null) && report.Rows.Count == 1, "Report PDF command is enabled for filtered rows");
        report.SearchText = "UNAPPLIED-FILTER";
        await (Task)Invoke(report, "ExportPdfAsync")!;
        using (var pdf = PdfReader.Open(reportPdf, PdfDocumentOpenMode.Import))
            Check(pdf.PageCount == 1 && reportMessage?.Contains("1 kết quả") == true, "Report PDF exports the applied table rather than unapplied filter edits");
        report.ChooseExportPath = _ => null; reportMessage = null;
        await (Task)Invoke(report, "ExportPdfAsync")!;
        Check(reportMessage is null, "Canceling report PDF export shows no success message");
        report.ApplyCommand.Execute(null);
        Check(!report.ExportPdfCommand.CanExecute(null), "Report PDF button disables when no filtered results remain");
    }
}
