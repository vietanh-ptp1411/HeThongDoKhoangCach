using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using BeltTensionMeasurement;
using BeltTensionMeasurement.Controls;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.Services;
using BeltTensionMeasurement.Services.Plc;
using BeltTensionMeasurement.ViewModels;
using BeltTensionMeasurement.Views;

internal static partial class Program
{
    private static int _checks;
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "BeltWorkflowChecks", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Output);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        int exit = 0;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (args.Contains("--manual-assets"))
                {
                    await CaptureManualScreenshots();
                    Console.WriteLine($"Manual screenshots: {Output}");
                }
                else
                {
                    await Run();
                    Console.WriteLine($"PASS: {_checks} checks. Artifacts: {Output}");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run();
        return exit;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        _checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static async Task CaptureManualScreenshots()
    {
        var settings = Settings("manual-demo");
        settings.AutoConnectPlc = true;
        settings.Inspector = "DEMO";
        settings.ResultQrTemplate = ResultQrFormatter.DefaultTemplate;
        await using var vm = new MainViewModel(settings);
        vm.SelectedSpec = vm.Specs[2];
        vm.SelectedModel = vm.AvailableModels[1];
        await vm.StartAsync();
        foreach (var serial in new[] { "985X57200-00123", "985X57200-00124", "985X57200-00125" })
        {
            vm.QrInput = serial;
            await CompleteScan(vm);
            await Until(() => vm.StartCommand.CanExecute(null));
            int previous = vm.RecentRows.Count;
            vm.StartCommand.Execute(null);
            await Until(() => vm.RecentRows.Count > previous && !vm.MachineRunning);
        }
        var main = new MainWindow(vm);
        ((System.Windows.Controls.Grid)main.Content).Background = (Brush)Application.Current.Resources["BgBrush"];
        await Render((FrameworkElement)main.Content, 1540, 850, "main-window.png");
        await Render((FrameworkElement)main.Content, 1180, 700, "main-window-small.png");
        var editorSettings = SettingsService.Clone(settings);
        editorSettings.DatabaseFilePath = @"Data\HeThongDo.db";
        var editor = new SettingsWindow(editorSettings);
        ((System.Windows.Controls.Grid)editor.Content).Background = (Brush)Application.Current.Resources["BgBrush"];
        var tabs = (System.Windows.Controls.TabControl)editor.FindName("SectionTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            await Render((FrameworkElement)editor.Content, 1120, 720, $"settings-section-{i}.png");
        }
        var report = new ReportWindow(vm.RecentRows, _ => null, (_, _) => { });
        ((System.Windows.Controls.Grid)report.Content).Background = (Brush)Application.Current.Resources["BgBrush"];
        await Render((FrameworkElement)report.Content, 1680, 800, "report-qr.png");
        await Render((FrameworkElement)report.Content, 980, 600, "report-small.png");
    }

    private static object? Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static Task Call(MainViewModel vm, string method) => (Task)Invoke(vm, method)!;
    private static void Poll(MainViewModel vm, bool done, double value = 3.7, bool? ok = true, bool? ng = false)
        => Invoke(vm, "OnSnapshot", new PlcSnapshot { MeasureDone = done, ResultValue = value, Distance = value, Force = 2,
            JudgeOk = ok, JudgeNg = ng, Running = false, LoadcellStable = true, Timestamp = new DateTime(2026, 9, 30, 10, 0, 0) });

    private static AppSettings Settings(string name) => new()
    {
        AutoConnectPlc = false,
        DatabaseFilePath = Path.Combine(Output, name + ".db"),
        ResultFilePath = Path.Combine(Output, name + ".jsonl"),
    };

    private static async Task Run()
    {
        var settings = Settings("workflow");
        var db = new SpecDatabase(settings.DatabaseFilePath);
        var specs = db.Load();
        Check(specs.Count == 3 && specs[2].Models.Select(m => m.Name).SequenceEqual(new[] { "GSM", "CPX" }), "Database seeds PDF specifications and dependent models");
        Check(SpecDatabase.Validate(specs) is null, "M1 and M2 may share M42 within the same specification");
        specs[0].Condition = "Custom condition";
        db.Save(specs);
        Check(db.Load()[0].Condition == "Custom condition", "Catalog edits persist through database reload");
        specs[0].Lsl = 99;
        try { db.Save(specs); throw new Exception("Invalid catalog accepted"); } catch (ArgumentException) { }
        Check(db.Load()[0].Lsl == 3, "Invalid catalog does not overwrite existing data");

        await using var vm = new MainViewModel(settings);
        Check(!vm.StartCommand.CanExecute(null) && !vm.ExportPdfCommand.CanExecute(null), "No START or PDF export before a measurement");
        vm.SelectedSpec = vm.Specs[2];
        Check(vm.AvailableModels.Select(m => m.Name).SequenceEqual(new[] { "GSM", "CPX" }), "Model list follows selected specification");
        vm.SelectedModel = vm.AvailableModels[1];
        vm.QrInput = "SERIAL-001";
        await CompleteScan(vm);
        Check(vm.Model == "CPX" && vm.Serial == "SERIAL-001", "Scanning serial works directly without a master file");

        var monitor = (PlcMonitor)typeof(MainViewModel).GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var client = new RecordingClient();
        SetField(monitor, "_client", client);
        Invoke(vm, "OnConnectionChanged", true);
        Poll(vm, true, 5.9); // Old PLC result at connection must not become a new record.
        Check(vm.RecentRows.Count == 0 && vm.ResultQrText.Length == 0, "Reconnect snapshot does not save or display old result QR");
        Poll(vm, false);
        Check(vm.StartCommand.CanExecute(null), "START enabled after baseline, selection and serial");
        await Call(vm, "StartMachineAsync");
        Check(client.Bits.GetValueOrDefault("M44") && !client.Bits.GetValueOrDefault("M40") && client.Bits.GetValueOrDefault("M0"), "START writes selected model bit and start command");
        Check(!vm.CanEditSelection && !vm.StartCommand.CanExecute(null), "Selection and repeat START locked during measurement");
        vm.SelectedSpec = vm.Specs[0];
        Check(vm.SelectedSpec == vm.Specs[2], "In-flight specification cannot change");
        Poll(vm, true, 3.7, null, null);
        Check(vm.RecentRows.Count == 0 && vm.ResultQrText == "" && !vm.CanEditSelection, "Delayed PLC judgment waits before saving and generating QR");
        Poll(vm, true, 9.9, true, false);
        Check(vm.RecentRows.Count == 1 && vm.RecentRows[0].Value == 3.7 && vm.RecentRows[0].Serial == "SERIAL-001", "Delayed judgment preserves the captured measurement and serial");
        Check(vm.ResultQrText == "3.70", "Default QR encodes captured value with two decimal places");
        Check(vm.RecentRows.Single().QrText == "3.70", "Automatically saved result includes the exact displayed QR payload");
        Poll(vm, true, 3.7);
        Check(vm.RecentRows.Count == 1, "Holding measure-done high does not duplicate a result");
        Poll(vm, false, 3.7);
        Poll(vm, true, 3.7, null, null);
        Check(vm.ResultQrText == "3.70" && vm.RecentRows.Count == 1, "Extra completion pulses cannot clear the result QR without a new START");
        Poll(vm, true, 3.7);
        Check(vm.RecentRows.Count == 1 && vm.IsPass == true, "One scan and START accept exactly one completed result");
        Check(!vm.StartCommand.CanExecute(null) && vm.CanEditSelection, "Completed measurement unlocks scan but requires a new serial scan before START");
        Check(vm.HistogramGroups[2].Values.Count == 1 && vm.HistogramGroups[0].Values.Count == 0, "Histograms group by recorded specification limits");
        Invoke(vm, "SaveCurrent");
        Check(vm.RecentRows.Count == 1, "Retry cannot duplicate an automatically saved result");
        await Call(vm, "StopMachineAsync");
        Poll(vm, false, 9.9, false, true);
        Poll(vm, true, 9.9, false, true);
        Check(vm.RecentRows.Count == 1 && vm.ResultQrText == "3.70" && vm.Force == 2 && vm.Distance == 9.9,
            "STOP prevents new saved results while live PLC readings continue");
        Check(!vm.StartCommand.CanExecute(null) && vm.Serial == "SERIAL-001", "STOP after completion does not bypass the required scan");
        await Call(vm, "StartMachineAsync");
        Poll(vm, false);
        Poll(vm, true, 4.25);
        Check(vm.RecentRows.Count == 1 && vm.ResultQrText == "3.70", "Calling START without rescanning cannot arm another measurement");
        vm.QrInput = "SERIAL-001";
        await CompleteScan(vm);
        await Call(vm, "StartMachineAsync");
        Poll(vm, false);
        Poll(vm, true, 4.25);
        Check(vm.RecentRows.Count == 2 && vm.ResultQrText == "4.25", "Rescanning the same serial allows one new measurement");
        await Call(vm, "StopMachineAsync");

        vm.QrInput = "SERIAL-002";
        await CompleteScan(vm);
        Check(vm.ResultQrText == "" && vm.MeasuredValue is null, "New serial clears previous result and QR");
        vm.ScanTarget = ScanField.Serial;
        vm.SerialInput = new string('x', 257);
        await Call(vm, "StartMachineAsync");
        Check(vm.CanEditSelection && vm.MeasuredValue is null, "Invalid replacement barcode cannot start using previous serial");
        vm.QrInput = "SERIAL-002";
        await CompleteScan(vm);
        settings.ResultQrTemplate = "{Serial};{Model};{Value};{Result}";
        Poll(vm, false);
        await Call(vm, "StartMachineAsync");
        Poll(vm, true, 0, false, true);
        Check(vm.MeasuredValueText == "0.00" && vm.ResultQrText == "SERIAL-002;CPX;0.00;NG", "Zero measurement is a valid NG result and QR uses configured fields");
        Poll(vm, false);
        Poll(vm, true, 4.5);
        Check(vm.RecentRows.Count == 3 && vm.CanEditSelection && vm.MeasuredValue == 0,
            "Every completed result saves automatically and unlocks the next input");
        settings.ResultQrTemplate = "{Value}";
        Invoke(vm, "SaveCurrent");
        Check(vm.RecentRows.Count == 3 && vm.RecentRows.Count(r => r.Serial == "SERIAL-002") == 1,
            "Retrying automatic save cannot duplicate a saved result");
        Check(vm.RecentRows.Single(r => r.Serial == "SERIAL-002").QrText == "SERIAL-002;CPX;0.00;NG",
            "Saved QR remains unchanged after template edits");
        settings.ResultQrTemplate = "{Serial};{Model};{Value};{Result}";
        var reloaded = new ResultStore(settings.ResultFilePath);
        reloaded.Load();
        Check(reloaded.All.First().QrText == "3.70" && reloaded.All.Last().QrText == "SERIAL-002;CPX;0.00;NG",
            "Reloading measurement history preserves QR payloads across template changes");
        using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadLines(settings.ResultFilePath).First()))
            Check(!saved.RootElement.TryGetProperty("OrderNo", out _) && !saved.RootElement.TryGetProperty("Line", out _),
                "New history records contain neither order nor line fields");
        string legacyPath = Path.Combine(Output, "legacy-results.jsonl");
        File.WriteAllText(legacyPath, "{\"No\":1,\"Value\":3.7,\"Serial\":\"LEGACY\",\"InspectedAt\":\"2026-09-30T09:00:00\"}\n");
        var legacyStore = new ResultStore(legacyPath);
        legacyStore.Load();
        Check(legacyStore.All.Single().QrText == "", "Legacy results without QR remain readable and do not invent a historical QR");
        var historical = System.Text.Json.JsonSerializer.Deserialize<MeasurementResult>(
            "{\"No\":9,\"Serial\":\"OLD-001\",\"OrderNo\":\"ORDER-OLD\",\"Line\":\"LINE-OLD\",\"Value\":3.7,\"QrText\":\"OLD-001;ORDER-OLD;LINE-OLD;3.70\"}")!;
        Check(historical.Serial == "OLD-001" && historical.Value == 3.7 && historical.QrText == "OLD-001;ORDER-OLD;LINE-OLD;3.70",
            "Old history ignores removed metadata while preserving the original stored QR");
        var savedSettings = File.Exists(SettingsService.FilePath) ? File.ReadAllText(SettingsService.FilePath) : null;
        try
        {
            File.WriteAllText(SettingsService.FilePath,
                "{\"MasterFilePath\":\"Z:/missing/master.csv\",\"ResultQrTemplate\":\"{Serial};{OrderNo};{Line};{Value}\"}");
            var migrated = SettingsService.Load();
            Check(migrated.ResultQrTemplate == "{Value}" && !System.Text.Json.JsonSerializer.Serialize(migrated).Contains("MasterFilePath"),
                "Old settings load without a master file and retire QR templates using removed fields");
        }
        finally
        {
            if (savedSettings is null) File.Delete(SettingsService.FilePath);
            else File.WriteAllText(SettingsService.FilePath, savedSettings);
        }
        Check(ResultQrFormatter.NormalizeTemplate("{Serial};{Model};{Value}") == "{Serial};{Model};{Value}",
            "QR templates using supported fields stay unchanged");
        var report = new ReportViewModel(reloaded.All) { SearchText = "SERIAL-002;CPX;0.00;NG" };
        report.ApplyCommand.Execute(null);
        Check(report.Rows.Count == 1 && report.Rows[0].Serial == "SERIAL-002", "Report search can find the saved QR payload");
        await Call(vm, "StopMachineAsync");
        Check(vm.CanEditSelection, "STOP unlocks selection once pending results have been saved");
        vm.SelectedSpec = vm.Specs[1];
        Check(vm.SelectedModel is null && vm.Serial == "" && vm.ResultQrText == "", "Changing specification clears model, serial and old QR");
        await Call(vm, "ResetAsync");
        Check(vm.SelectedSpec == vm.Specs[1] && vm.MeasuredValue is null, "RESET clears measurement and retains specification selection");

        var editor = new SettingsViewModel(settings);
        Check(editor.Validate() is null, "Settings accept database defaults with shared model bit");
        editor.Specs[0].Condition = "Unsaved edit";
        Check(db.Load()[0].Condition == "Custom condition", "Canceling catalog editor does not persist draft changes");
        editor.Build();
        Check(db.Load()[0].Condition == "Unsaved edit", "Saving settings persists catalog changes");
        editor.ResultQrTemplate = "{BadField}";
        Check(editor.Validate() is not null, "Unknown QR template fields are rejected");

        var statistics = new StatisticsViewModel(vm.RecentRows, db.Load());
        Check(statistics.ByModel.Single().Spec == "3.5 ~ 6 mm", "Statistics retain historical limits independently of catalog edits");
        string excel = Path.Combine(Output, "results.xlsx");
        ExcelExporter.Export(vm.RecentRows, excel);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(excel))
        {
            var sheet = workbook.Worksheet("ExportResultData");
            Check(sheet.LastRowUsed()!.RowNumber() >= 3, "Excel export contains saved measurements");
            Check(sheet.LastColumnUsed()!.ColumnNumber() == 13 && sheet.Cell(1, 2).GetString() == "Chủng loại"
                  && sheet.Cell(1, 3).GetString() == "Mã quét (Serial)" && sheet.Cell(2, 6).GetDouble() == vm.RecentRows[0].Value,
                "Excel omits order/line and keeps measurement columns aligned");
            Check(sheet.Cell(1, 12).GetString() == "QR" && sheet.Cell(2, 12).GetString() == vm.RecentRows[0].QrText,
                "Excel QR column preserves the payload as text");
            Check(sheet.Pictures.Count() == vm.RecentRows.Count(r => !string.IsNullOrEmpty(r.QrText)),
                "Excel embeds one QR image per saved payload");
        }

        // Render real WPF controls to detect missing resources, invalid bindings and QR drawing errors.
        var bindings = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
        vm.SelectedSpec = vm.Specs[2];
        vm.SelectedModel = vm.AvailableModels[1];
        vm.QrInput = "SERIAL-003";
        await CompleteScan(vm);
        Poll(vm, false);
        await Call(vm, "StartMachineAsync");
        Poll(vm, true, 4.25, true, false);
        Invoke(vm, "SaveCurrent");
        var window = new MainWindow(vm);
        await Render((FrameworkElement)window.Content, 1540, 850, "main-window.png");
        await Render((FrameworkElement)window.Content, 1180, 700, "main-window-small.png");
        var settingsWindow = new SettingsWindow(settings);
        await Render((FrameworkElement)settingsWindow.Content, 1000, 800, "settings-window.png");
        var settingsTabs = (System.Windows.Controls.TabControl)settingsWindow.FindName("SectionTabs");
        for (int section = 1; section < settingsTabs.Items.Count; section++)
        {
            settingsTabs.SelectedIndex = section;
            await Render((FrameworkElement)settingsWindow.Content, 1120, 720, $"settings-section-{section}.png");
        }
        settingsTabs.SelectedIndex = 2;
        await Render((FrameworkElement)settingsWindow.Content, 960, 600, "catalog-editor-small.png");
        await Render(new QrCodeControl { Text = "3.70", Width = 232, Height = 232 }, 232, 232, "result-qr.png");
        var statsWindow = new StatisticsWindow(vm.RecentRows, vm.Specs.ToList());
        await Render((FrameworkElement)statsWindow.Content, 1120, 650, "statistics.png");
        var reportWindow = new ReportWindow([.. vm.RecentRows, .. legacyStore.All], _ => null, (_, _) => { });
        await Render((FrameworkElement)reportWindow.Content, 1680, 800, "report-qr.png");
        Check(bindings.Errors.Count == 0, "Main/settings WPF views render without binding errors: " + string.Join("; ", bindings.Errors));

        var serialBox = (System.Windows.Controls.TextBox)window.FindName("QrBox");
        var inspectorBox = (System.Windows.Controls.TextBox)window.FindName("InspectorBox");
        var purposeBox = (System.Windows.Controls.TextBox)window.FindName("PurposeScanBox");
        Check(SendScannerInput(inspectorBox, "EMP-FIRST") && vm.InspectorCode == "EMP-FIRST", "Clicking employee field routes the barcode to employee");
        var oldPurpose = vm.SelectedPurpose;
        Check(SendScannerInput(serialBox, "CLICK-001") && vm.Serial == "CLICK-001" && vm.InspectorCode == "EMP-FIRST" && vm.SelectedPurpose == oldPurpose,
            "Clicking serial field changes only serial, preserving employee and purpose");
        Check(SendScannerInput(serialBox, "CLICK-002") && vm.Serial == "CLICK-002" && vm.InspectorCode == "EMP-FIRST", "Consecutive scans stay in serial rather than advancing to employee");
        Check(SendScannerInput(inspectorBox, "EMP-SECOND") && vm.InspectorCode == "EMP-SECOND" && vm.Serial == "CLICK-002", "Employee rescan does not change serial");
        Check(SendScannerInput(purposeBox, "PURPOSE:3") && vm.SelectedPurpose?.Code == 3, "Purpose barcode has its own input field");
        Check(SendScannerInput(purposeBox, "3") && vm.PurposeInput == "" && vm.StartCommand.CanExecute(null), "Rescanning the same purpose confirms and clears its input");
        int beforeScan = vm.RecentRows.Count;
        Check(SendScannerInput((UIElement)window.Content, "OUTSIDE-FIELDS") && vm.Serial == "CLICK-002" && vm.InspectorCode == "EMP-SECOND" && vm.RecentRows.Count == beforeScan,
            "Scanning outside input fields cannot change metadata or trigger START");
        Check(vm.StartCommand.CanExecute(null), "Scanner Enter confirms input and leaves measurement waiting for mouse START");
        await Call(vm, "StartMachineAsync");
        Check(SendScannerInput(serialBox, "IGNORED-WHILE-BUSY") && vm.Serial == "CLICK-002", "Scan while measuring cannot replace the active serial");
        Poll(vm, false); Poll(vm, true); Invoke(vm, "SaveCurrent");
        Check(SendScannerInput(serialBox, "CLICK-003") && vm.Serial == "CLICK-003", "Selecting serial accepts the next measurement scan");
        Check(!SendScannerInput((UIElement)settingsWindow.Content, "SETTINGS-INPUT") && vm.Serial == "CLICK-003", "Settings typing remains separate from measurement inputs");

        await using var simulated = new SimulationPlcClient(new PlcSettings(), db.Load());
        await simulated.ConnectAsync();
        await simulated.WriteBitAsync("M44", true);
        await simulated.WriteBitAsync("M0", true);
        await simulated.ReadWordsAsync("D100", 2);
        await Task.Delay(1650);
        await simulated.ReadWordsAsync("D104", 2);
        Check((await simulated.ReadWordsAsync("D110", 2))[0] == 1 && !(await simulated.ReadBitsAsync("M104", 1))[0], "Simulation stops automatically after one measurement");
        await Task.Delay(3300);
        await simulated.ReadWordsAsync("D104", 2);
        await simulated.ReadWordsAsync("D104", 2);
        await Task.Delay(1650);
        Check((await simulated.ReadWordsAsync("D110", 2))[0] == 1, "Simulation cannot repeat measurements without another START");
        await simulated.WriteBitAsync("M0", true);
        await simulated.ReadWordsAsync("D100", 2);
        await simulated.WriteBitAsync("M15", true);
        await Task.Delay(1650);
        Check((await simulated.ReadWordsAsync("D110", 2))[0] == 1 && !(await simulated.ReadBitsAsync("M104", 1))[0], "STOP cancels an in-flight measurement without producing a result");

        // Complete the real background-poll integration in simulation, with no injected snapshots.
        var liveSettings = Settings("live");
        liveSettings.AutoConnectPlc = true;
        liveSettings.Plc.PollIntervalMs = 50;
        await using var live = new MainViewModel(liveSettings);
        live.SelectedSpec = live.Specs[1];
        live.SelectedModel = live.AvailableModels[0];
        live.QrInput = "LIVE-001";
        await CompleteScan(live);
        await live.StartAsync();
        await Until(() => live.StartCommand.CanExecute(null));
        live.StartCommand.Execute(null);
        await Until(() => live.RecentRows.Count == 1);
        Check(live.RecentRows[0].Serial == "LIVE-001" && live.ResultQrText == live.RecentRows[0].Value.ToString("0.00"), "Background PLC polling completes scan/START/save/QR workflow");
        await Until(() => !live.MachineRunning);
        Check(live.RecentRows.Count == 1 && !live.StartCommand.CanExecute(null), "Background polling completes one measurement and requires another scan");
        live.QrInput = "LIVE-002";
        await CompleteScan(live);
        live.StartCommand.Execute(null);
        await Until(() => live.RecentRows.Count == 2);
        Check(live.RecentRows[0].Serial == "LIVE-002" && live.RecentRows[1].Serial == "LIVE-001",
            "Each scanned serial gets exactly one result through the real background poll loop");

        // Optional PLC tags: software judgment and total-counter fallback must retain the same workflow.
        var fallbackSettings = Settings("fallback");
        fallbackSettings.Plc.MeasureDone.Address = "";
        fallbackSettings.Plc.JudgeOk.Address = fallbackSettings.Plc.JudgeNg.Address = "";
        await using var fallback = new MainViewModel(fallbackSettings);
        fallback.SelectedSpec = fallback.Specs[0];
        fallback.SelectedModel = fallback.AvailableModels[0];
        fallback.QrInput = "FALLBACK-1";
        await CompleteScan(fallback);
        var fallbackMonitor = (PlcMonitor)typeof(MainViewModel).GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fallback)!;
        SetField(fallbackMonitor, "_client", new RecordingClient());
        Invoke(fallback, "OnConnectionChanged", true);
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 10 });
        await Call(fallback, "StartMachineAsync");
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 11, ResultValue = 4, Force = 2 });
        Check(fallback.RecentRows.Single().IsOk && fallback.ResultQrText == "4.00", "Counter fallback detects result and software judgment includes USL boundary");
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 12, ResultValue = 4, Force = 2 });
        Check(fallback.RecentRows.Count == 1 && fallback.Serial == "FALLBACK-1", "Counter fallback ignores further results after the single measurement");
        await Call(fallback, "StopMachineAsync");
        fallback.QrInput = "FALLBACK-2";
        await CompleteScan(fallback);
        await Call(fallback, "StartMachineAsync");
        Invoke(fallback, "OnConnectionChanged", false);
        Invoke(fallback, "OnConnectionChanged", true);
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 13, ResultValue = 3.8, Force = 2 });
        Check(fallback.RecentRows.Count == 1 && fallback.ResultQrText == "", "Connection loss discards incomplete cycle and ignores reconnect result");
        await CheckMouseStart();
        await CheckAutoSaveAndPdf();
    }

    private static async Task CompleteScan(MainViewModel vm)
    {
        var serial = vm.QrInput;
        vm.ScanTarget = ScanField.Serial;
        vm.QrInput = serial;
        await Call(vm, "ScanAsync");
        vm.ScanTarget = ScanField.Inspector;
        vm.QrInput = "EMP-23474";
        await Call(vm, "ScanAsync");
        vm.ScanTarget = ScanField.Purpose;
        vm.QrInput = "1";
        await Call(vm, "ScanAsync");
    }

    private static async Task Until(Func<bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("Workflow did not complete");
            await Task.Delay(30);
        }
    }

    private static bool SendScannerInput(UIElement source, string text)
    {
        bool handled = true;
        if (source is System.Windows.Controls.TextBox selectedBox) selectedBox.SelectAll();
        foreach (char c in text)
        {
            var input = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, source, c.ToString()))
            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            source.RaiseEvent(input);
            if (!input.Handled && source is System.Windows.Controls.TextBox box)
            {
                // Emulate TextBox's normal text insertion after the tunneling preview event.
                box.SelectedText = c.ToString();
                box.CaretIndex = box.SelectionStart + box.SelectionLength;
                box.SelectionLength = 0;
                box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }
            else handled &= input.Handled;
        }
        // Cửa sổ native ẩn chỉ cung cấp PresentationSource cho sự kiện phím.
        using var presentation = new HwndSource(new HwndSourceParameters("ScannerInputChecks") { Width = 1, Height = 1, WindowStyle = 0 });
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, presentation, Environment.TickCount, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        source.RaiseEvent(enter);
        return handled && enter.Handled;
    }

    private static async Task Render(FrameworkElement element, int width, int height, string file)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output, file));
        encoder.Save(stream);
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (message?.Contains("Error:") == true) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private sealed class RecordingClient : IPlcClient
    {
        public Dictionary<string, bool> Bits { get; } = [];
        public List<string> Writes { get; } = [];
        public string? FailAddress { get; set; }
        public string? PauseAddress { get; set; }
        public TaskCompletionSource? Paused { get; set; }
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default) => Task.FromResult(new ushort[count]);
        public Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default) => Task.FromResult(new bool[count]);
        public async Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default)
        {
            if (address == FailAddress) throw new IOException("Injected write failure");
            Writes.Add(address + "=" + string.Join(",", values));
            if (address == PauseAddress && Paused is not null) await Paused.Task;
        }
        public Task WriteBitAsync(string address, bool value, CancellationToken ct = default) { if (address == FailAddress) throw new IOException("Injected write failure"); Writes.Add(address + "=" + value); Bits[address] = value; return Task.CompletedTask; }
    }
}
