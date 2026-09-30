using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HeThongDoKhoangCach;
using HeThongDoKhoangCach.Controls;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.Services;
using HeThongDoKhoangCach.Services.Plc;
using HeThongDoKhoangCach.ViewModels;
using HeThongDoKhoangCach.Views;

internal static class Program
{
    private static int _checks;
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "BeltWorkflowChecks", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main()
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
                await Run();
                Console.WriteLine($"PASS: {_checks} checks. Artifacts: {Output}");
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
        MasterFilePath = Path.Combine(Output, "master.csv"),
    };

    private static async Task Run()
    {
        var settings = Settings("workflow");
        File.WriteAllText(settings.MasterFilePath, "Key,OrderNo,Line,Model\nSERIAL,ORDER-1,LINE-A,LCP\n");
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
        Check(!vm.StartCommand.CanExecute(null) && !vm.SaveCommand.CanExecute(null), "No START or SAVE before selection and measurement");
        vm.SelectedSpec = vm.Specs[2];
        Check(vm.AvailableModels.Select(m => m.Name).SequenceEqual(new[] { "GSM", "CPX" }), "Model list follows selected specification");
        vm.SelectedModel = vm.AvailableModels[1];
        vm.QrInput = "SERIAL-001";
        await Call(vm, "ScanAsync");
        Check(vm.Model == "CPX" && vm.OrderNo == "ORDER-1" && vm.Line == "LINE-A", "Master lookup supplements order/line without overriding selected model");

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
        Poll(vm, true, 3.7);
        Check(vm.RecentRows.Count == 1, "Holding measure-done high does not duplicate a result");
        Poll(vm, false, 3.7);
        Poll(vm, true, 3.7, null, null);
        Check(vm.ResultQrText == "" && vm.RecentRows.Count == 1, "Next measurement waiting for judgment clears the previous QR");
        Poll(vm, true, 3.7);
        Check(vm.RecentRows.Count == 2 && vm.RecentRows.All(r => r.Serial == "SERIAL-001"), "One scan and START save successive measurements, including equal values, under the same serial");
        Check(!vm.StartCommand.CanExecute(null) && !vm.CanEditSelection, "Continuous run stays active between results and locks serial selection");
        Check(vm.HistogramGroups[2].Values.Count == 2 && vm.HistogramGroups[0].Values.Count == 0, "Histograms group by recorded specification limits");
        vm.SaveCommand.Execute(null);
        Check(vm.RecentRows.Count == 2, "Manual SAVE cannot duplicate automatically saved result");
        await Call(vm, "StopMachineAsync");
        Poll(vm, false, 9.9, false, true);
        Poll(vm, true, 9.9, false, true);
        Check(vm.RecentRows.Count == 2 && vm.ResultQrText == "3.70" && vm.Force == 2 && vm.Distance == 9.9,
            "STOP prevents new saved results while live PLC readings continue");
        Check(vm.StartCommand.CanExecute(null) && vm.Serial == "SERIAL-001", "STOP retains serial so START can resume without rescanning");
        await Call(vm, "StartMachineAsync");
        Poll(vm, false);
        Poll(vm, true, 4.25);
        Check(vm.RecentRows.Count == 3 && vm.ResultQrText == "4.25", "Resuming the same roll updates history and latest QR");
        await Call(vm, "StopMachineAsync");

        vm.QrInput = "SERIAL-002";
        await Call(vm, "ScanAsync");
        Check(vm.ResultQrText == "" && vm.MeasuredValue is null, "New serial clears previous result and QR");
        vm.QrInput = new string('x', 257);
        await Call(vm, "StartMachineAsync");
        Check(vm.CanEditSelection && vm.MeasuredValue is null, "Invalid replacement barcode cannot start using previous serial");
        vm.QrInput = "SERIAL-002";
        await Call(vm, "ScanAsync");
        settings.AutoSaveOnMeasureDone = false;
        settings.ResultQrTemplate = "{Serial};{Model};{Value};{Result}";
        Poll(vm, false);
        await Call(vm, "StartMachineAsync");
        Poll(vm, true, 0, false, true);
        Check(vm.MeasuredValueText == "0.00" && vm.ResultQrText == "SERIAL-002;CPX;0.00;NG", "Zero measurement is a valid NG result and QR uses configured fields");
        Poll(vm, false);
        Poll(vm, true, 4.5);
        Check(vm.RecentRows.Count == 3 && vm.SaveCommand.CanExecute(null) && !vm.CanEditSelection, "Manual mode queues multiple results while measurement continues");
        vm.SaveCommand.Execute(null);
        Check(vm.RecentRows.Count == 5 && vm.RecentRows.Count(r => r.Serial == "SERIAL-002") == 2 && !vm.CanEditSelection,
            "Manual SAVE writes all queued results without stopping the continuous run");
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
            Check(workbook.Worksheet("ExportResultData").LastRowUsed()!.RowNumber() >= 3, "Excel export contains both saved measurements");

        // Render real WPF controls to detect missing resources, invalid bindings and QR drawing errors.
        var bindings = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
        vm.SelectedSpec = vm.Specs[2];
        vm.SelectedModel = vm.AvailableModels[1];
        vm.QrInput = "SERIAL-003";
        await Call(vm, "ScanAsync");
        Poll(vm, false);
        await Call(vm, "StartMachineAsync");
        Poll(vm, true, 4.25, true, false);
        vm.SaveCommand.Execute(null);
        var window = new MainWindow(vm);
        await Render((FrameworkElement)window.Content, 1540, 850, "main-window.png");
        await Render((FrameworkElement)window.Content, 1180, 700, "main-window-small.png");
        var settingsWindow = new SettingsWindow(settings);
        await Render((FrameworkElement)settingsWindow.Content, 1000, 800, "settings-window.png");
        var settingsScroll = ((System.Windows.Controls.Grid)settingsWindow.Content).Children.OfType<System.Windows.Controls.ScrollViewer>().Single();
        settingsScroll.ScrollToVerticalOffset(780);
        await Render((FrameworkElement)settingsWindow.Content, 1000, 800, "catalog-editor.png");
        await Render(new QrCodeControl { Text = "3.70", Width = 232, Height = 232 }, 232, 232, "result-qr.png");
        Check(bindings.Errors.Count == 0, "Main/settings WPF views render without binding errors: " + string.Join("; ", bindings.Errors));

        await using var simulated = new SimulationPlcClient(new PlcSettings(), db.Load());
        await simulated.ConnectAsync();
        await simulated.WriteBitAsync("M44", true);
        await simulated.WriteBitAsync("M0", true);
        await simulated.ReadWordsAsync("D100", 2);
        await Task.Delay(1650);
        await simulated.ReadWordsAsync("D104", 2);
        Check((await simulated.ReadWordsAsync("D110", 2))[0] == 1 && (await simulated.ReadBitsAsync("M104", 1))[0], "Simulation stays running after the first measurement");
        await Task.Delay(3300);
        await simulated.ReadWordsAsync("D104", 2);
        await simulated.ReadWordsAsync("D104", 2);
        await Task.Delay(1650);
        Check((await simulated.ReadWordsAsync("D110", 2))[0] == 2, "Simulation repeats measurements without another START");
        await simulated.WriteBitAsync("M15", true);
        await Task.Delay(1650);
        Check((await simulated.ReadWordsAsync("D110", 2))[0] == 2 && !(await simulated.ReadBitsAsync("M104", 1))[0], "Simulation stops measuring when STOP is pressed");

        // Complete the real background-poll integration in simulation, with no injected snapshots.
        var liveSettings = Settings("live");
        liveSettings.AutoConnectPlc = true;
        liveSettings.Plc.PollIntervalMs = 50;
        await using var live = new MainViewModel(liveSettings);
        live.SelectedSpec = live.Specs[1];
        live.SelectedModel = live.AvailableModels[0];
        live.QrInput = "LIVE-001";
        await Call(live, "ScanAsync");
        await live.StartAsync();
        await Until(() => live.StartCommand.CanExecute(null));
        live.StartCommand.Execute(null);
        await Until(() => live.RecentRows.Count == 1);
        Check(live.RecentRows[0].Serial == "LIVE-001" && live.ResultQrText == live.RecentRows[0].Value.ToString("0.00"), "Background PLC polling completes scan/START/save/QR workflow");
        await Until(() => live.RecentRows.Count >= 2);
        Check(live.RecentRows.All(r => r.Serial == "LIVE-001") && live.MachineRunning, "Background polling saves continuous results for one scanned serial");
        await Call(live, "StopMachineAsync");
        await Until(() => !live.MachineRunning);
        Check(live.StartCommand.CanExecute(null), "Live STOP retains serial and enables START again");

        // Optional PLC tags: software judgment and total-counter fallback must retain the same workflow.
        var fallbackSettings = Settings("fallback");
        fallbackSettings.Plc.MeasureDone.Address = "";
        fallbackSettings.Plc.JudgeOk.Address = fallbackSettings.Plc.JudgeNg.Address = "";
        await using var fallback = new MainViewModel(fallbackSettings);
        fallback.SelectedSpec = fallback.Specs[0];
        fallback.SelectedModel = fallback.AvailableModels[0];
        fallback.QrInput = "FALLBACK-1";
        await Call(fallback, "ScanAsync");
        var fallbackMonitor = (PlcMonitor)typeof(MainViewModel).GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fallback)!;
        SetField(fallbackMonitor, "_client", new RecordingClient());
        Invoke(fallback, "OnConnectionChanged", true);
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 10 });
        await Call(fallback, "StartMachineAsync");
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 11, ResultValue = 4, Force = 2 });
        Check(fallback.RecentRows.Single().IsOk && fallback.ResultQrText == "4.00", "Counter fallback detects result and software judgment includes USL boundary");
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 12, ResultValue = 4, Force = 2 });
        Check(fallback.RecentRows.Count == 2 && fallback.Serial == "FALLBACK-1", "Counter fallback also records consecutive equal results without rescanning");
        await Call(fallback, "StopMachineAsync");
        fallback.QrInput = "FALLBACK-2";
        await Call(fallback, "ScanAsync");
        await Call(fallback, "StartMachineAsync");
        Invoke(fallback, "OnConnectionChanged", false);
        Invoke(fallback, "OnConnectionChanged", true);
        Invoke(fallback, "OnSnapshot", new PlcSnapshot { Total = 13, ResultValue = 3.8, Force = 2 });
        Check(fallback.RecentRows.Count == 2 && fallback.ResultQrText == "", "Connection loss discards incomplete cycle and ignores reconnect result");
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
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default) => Task.FromResult(new ushort[count]);
        public Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default) => Task.FromResult(new bool[count]);
        public Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default) => Task.CompletedTask;
        public Task WriteBitAsync(string address, bool value, CancellationToken ct = default) { Bits[address] = value; return Task.CompletedTask; }
    }
}
