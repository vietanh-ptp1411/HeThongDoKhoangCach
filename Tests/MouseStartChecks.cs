using System.IO;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.Services;
using BeltTensionMeasurement.ViewModels;

internal static partial class Program
{
    private static async Task CheckMouseStart()
    {
        var settings = Settings("mouse-start");
        settings.Plc.Protocol = PlcProtocol.McProtocol3E;
        settings.Plc.SpecLslWrite.Address = "D200";
        settings.Plc.SpecUslWrite.Address = "D202";
        settings.Plc.PurposeWrite.Address = "D204";
        await using var vm = new MainViewModel(settings);
        vm.SelectedSpec = vm.Specs[2]; vm.SelectedModel = vm.AvailableModels[1];
        var monitor = (PlcMonitor)typeof(MainViewModel).GetField("_monitor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
        var client = new RecordingClient(); SetField(monitor, "_client", client);
        Invoke(vm, "OnConnectionChanged", true); Poll(vm, false);
        Check(client.Writes.Count == 0, "Selecting model and connecting do not start the machine");
        vm.InspectorInput = "BISG-001"; vm.AcceptScan(ScanField.Inspector);
        vm.SelectedPurpose = MeasurementPurpose.All[3];
        Check(!vm.StartCommand.CanExecute(null), "Missing serial disables mouse START");
        vm.SerialInput = "MOUSE-001"; vm.AcceptScan(ScanField.Serial);
        Check(vm.InspectorCode == "BISG-001" && vm.SelectedPurpose?.Code == 4, "Scanning serial preserves separately entered employee and purpose");
        Check(vm.StartCommand.CanExecute(null), "Mouse START is enabled on a physical PLC when all fields are confirmed");
        Poll(vm, true, 9.9); Poll(vm, false);
        Check(client.Writes.Count == 0 && vm.RecentRows.Count == 0, "PLC snapshots cannot start a measurement without a mouse command");
        vm.BeginDialog(); Check(!vm.StartCommand.CanExecute(null), "Open dialog blocks START"); vm.EndDialog();
        vm.StartCommand.Execute(null);
        Check(client.Writes.First() == "M0=False" && client.Writes.Last() == "M0=True" && client.Writes.IndexOf("M44=True") < client.Writes.IndexOf("D204=4"),
            "Click START sends model, limits and purpose before the start bit");
        var writeCount = client.Writes.Count;
        vm.StartCommand.Execute(null);
        Check(client.Writes.Count == writeCount && !vm.CanEditSelection, "Repeated click during measurement cannot start twice");
        Poll(vm, true, 4.2);
        var result = vm.RecentRows.Single();
        Check(result.Inspector == "BISG-001" && result.PurposeCode == 4 && result.Serial == "MOUSE-001" && vm.JudgeText == "OK", "Mouse measurement captures the selected employee, serial and purpose");
        Check(!vm.StartCommand.CanExecute(null), "Completed measurement still requires a fresh serial confirmation");
        var store = new ResultStore(settings.ResultFilePath); store.Load();
        Check(store.All.Single().Purpose == result.Purpose && store.All.Single().QrText == "4.20", "Purpose and QR survive history reload");
        var report = new ReportViewModel(store.All) { SelectedPurpose = result.Purpose }; report.ApplyCommand.Execute(null);
        Check(report.Rows.Count == 1, "Report filters by saved purpose");
        report.SelectedPurpose = MeasurementPurpose.All[0].Name; report.ApplyCommand.Execute(null);
        Check(report.Rows.Count == 0, "Report excludes other purposes");
        string excel = Path.Combine(Output, "mouse-purpose.xlsx"); ExcelExporter.Export(store.All, excel);
        using (var book = new ClosedXML.Excel.XLWorkbook(excel))
            Check(book.Worksheet(1).Cell(2, 9).GetString() == "BISG-001" && book.Worksheet(1).Cell(2, 13).GetString() == result.Purpose, "Excel retains employee and purpose");
        vm.SerialInput = "MOUSE-002"; vm.AcceptScan(ScanField.Serial);
        vm.InspectorInput = "UNCONFIRMED";
        Check(vm.StartCommand.CanExecute(null), "Edited employee can be committed by mouse START without Enter");
        client.FailAddress = "D202";
        vm.StartCommand.Execute(null);
        Check(!client.Bits.GetValueOrDefault("M0") && vm.CanEditSelection, "Write failure prevents the START bit and allows retry");
        client.FailAddress = null;
        client.PauseAddress = "D200"; client.Paused = new TaskCompletionSource();
        var start = Call(vm, "StartMachineAsync");
        var stop = Call(vm, "StopMachineAsync");
        client.Paused.SetResult(); await start; await stop;
        Check(!client.Bits.GetValueOrDefault("M0") && client.Bits.GetValueOrDefault("M15"), "STOP cancels a click while model data is still being sent");
        client.PauseAddress = null; settings.Plc.RunningState.Address = "";
        vm.StartCommand.Execute(null);
        Check(vm.MachineRunning, "START sets running when no PLC running tag is configured");
        Poll(vm, false); Poll(vm, true, 4.4);
        Check(!vm.MachineRunning && vm.RecentRows.Count == 2, "Single result clears the software running state");
        var editor = new SettingsViewModel(settings);
        editor.Tags.First(t => t.Key == nameof(PlcSettings.StartCommand)).Address = settings.Plc.StopCommand.Address;
        Check(editor.Validate() is not null, "Settings reject START sharing another command address");
        Check(MeasurementPurpose.All.All(p => MeasurementPurpose.Parse(p.Code.ToString()) == p), "Five purpose barcode mappings remain available");
        vm.SerialInput = "MOUSE-DISCONNECT"; vm.AcceptScan(ScanField.Serial);
        client.PauseAddress = "D200"; client.Paused = new TaskCompletionSource();
        start = Call(vm, "StartMachineAsync");
        Invoke(vm, "OnConnectionChanged", false);
        client.Paused.SetResult(); await start;
        Check(!client.Bits.GetValueOrDefault("M0"), "Connection loss during data transfer prevents the START bit");
        await CheckTypedStart();
    }

    private static async Task CheckTypedStart()
    {
        var settings = Settings("typed-start");
        await using var vm = new MainViewModel(settings);
        vm.SelectedSpec = vm.Specs[2]; vm.SelectedModel = vm.AvailableModels[1];
        vm.SerialInput = "  TYPED-001  ";
        vm.InspectorInput = "  EMP-TYPED  ";
        vm.SelectedPurpose = MeasurementPurpose.All[1];
        Check(!vm.StartCommand.CanExecute(null) && vm.StartBlockReason.Contains("PLC"), "Filled fields still explain the offline PLC requirement");
        var monitor = (PlcMonitor)typeof(MainViewModel).GetField("_monitor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
        var client = new RecordingClient(); SetField(monitor, "_client", client);
        Invoke(vm, "OnConnectionChanged", true);
        Check(!vm.StartCommand.CanExecute(null) && vm.StartBlockReason.Contains("đầu tiên"), "START explains that it is waiting for the first PLC snapshot");
        Poll(vm, false);
        Check(vm.StartCommand.CanExecute(null) && vm.ScanHint.StartsWith("Sẵn sàng"), "Typing all fields without Enter enables START and shows ready");
        vm.StartCommand.Execute(null);
        Poll(vm, true, 4.1);
        var result = vm.RecentRows.Single();
        Check(result.Serial == "TYPED-001" && result.Inspector == "EMP-TYPED" && result.PurposeCode == 2, "START captures and trims the visible typed values without scan confirmation");
        Check(!vm.StartCommand.CanExecute(null) && vm.StartBlockReason.Contains("Lượt trước"), "Completed serial cannot be reused by accidentally clicking START again");
        vm.SerialInput = "TYPED-002";
        vm.InspectorInput = "NEW-EMP";
        vm.PurposeInput = "PURPOSE:4";
        Check(vm.StartCommand.CanExecute(null), "A typed valid purpose barcode is accepted without Enter");
        vm.PurposeInput = "99";
        Check(!vm.StartCommand.CanExecute(null) && vm.StartBlockReason.Contains("mục đích"), "Invalid purpose blocks START with a specific reason");
        vm.PurposeInput = "4";
        vm.InspectorInput = " ";
        Check(!vm.StartCommand.CanExecute(null) && vm.StartBlockReason.Contains("MSNV"), "Blank employee blocks START with a specific reason");
        vm.InspectorInput = "NEW-EMP";
        vm.SerialInput = new string('S', 257);
        Check(!vm.StartCommand.CanExecute(null) && vm.StartBlockReason.Contains("Serial"), "Overlong serial blocks START with a specific reason");
        vm.SerialInput = "TYPED-002";
        vm.StartCommand.Execute(null);
        Poll(vm, false); Poll(vm, true, 4.3);
        Check(vm.RecentRows[0].Serial == "TYPED-002" && vm.RecentRows[0].Inspector == "NEW-EMP" && vm.RecentRows[0].PurposeCode == 4,
            "Second typed measurement stores current employee and barcode purpose, not old confirmed values");
    }
}
