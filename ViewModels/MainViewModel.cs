using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.Services;

namespace HeThongDoKhoangCach.ViewModels;

/// <summary>Một khối HISTOGRAM trên màn hình chính (một nhóm chủng loại có cùng quy cách).</summary>
public sealed class HistogramGroupViewModel : ViewModelBase
{
    public HistogramGroupViewModel(string group, Action<string> reset)
    {
        Group = group;
        ResetCommand = new RelayCommand(() => reset(group));
    }

    public string Group { get; }
    public string Title => "HISTOGRAM : " + Group;
    public ICommand ResetCommand { get; }

    private IReadOnlyList<double> _values = [];
    public IReadOnlyList<double> Values { get => _values; set => SetProperty(ref _values, value); }

    private double _lsl = double.NaN;
    public double Lsl { get => _lsl; set => SetProperty(ref _lsl, value); }

    private double _usl = double.NaN;
    public double Usl { get => _usl; set => SetProperty(ref _usl, value); }

    private string _specText = "";
    public string SpecText { get => _specText; set => SetProperty(ref _specText, value); }
}

/// <summary>
/// Màn hình hiển thị (HMI) cho PLC: PLC làm toàn bộ việc đo, giữ serial/đơn hàng, quy cách, phân định OK/NG, đếm.
/// Phần mềm đọc các tag lên hiển thị, ghi 3 bit lệnh START/STOP/RESET, và lưu lịch sử mỗi khi bit Đo xong
/// có sườn lên để vẽ histogram, trend chart, báo cáo, Excel.
/// Tag nào PLC không cung cấp (địa chỉ trống) thì phần mềm tự bù: serial nhập tại máy tính, tra file master,
/// quy cách theo chủng loại trong cài đặt, tự so quy cách để ra OK/NG, tự đếm theo lịch sử.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    public const string AppVersion = "1.0.0";
    private const string AllGroupName = "TẤT CẢ";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly PlcMonitor _monitor = new();
    private readonly DispatcherTimer _clock;

    private AppSettings _settings;
    private MasterDataService _master;
    private ResultStore _store;

    private bool _lastMeasureDone;
    /// <summary>Đã có sườn lên Đo xong nhưng chưa ghi được lịch sử (chờ OK/NG từ PLC hoặc chờ serial).</summary>
    private bool _awaitingRecord;
    private bool _currentSaved;
    /// <summary>Chỉ dùng khi serial nhập tại phần mềm: serial đã gắn với một kết quả đã lưu.</summary>
    private bool _serialConsumed;
    private string _plcSerialLast = "";
    private double _capturedForce;
    private ModelSpec? _currentSpec;
    private int _total, _okCount, _ngCount;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _master = new MasterDataService(SettingsService.ResolvePath(settings.MasterFilePath));
        _store = new ResultStore(SettingsService.ResolvePath(settings.ResultFilePath));
        TryLoadStore();

        _monitor.SnapshotReceived += s => _dispatcher.InvokeAsync(() => OnSnapshot(s));
        _monitor.ConnectionChanged += c => _dispatcher.InvokeAsync(() => OnConnectionChanged(c));
        _monitor.ErrorOccurred += m => _dispatcher.InvokeAsync(() => StatusMessage = "Lỗi PLC: " + m);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Now = DateTime.Now, _dispatcher);
        _clock.Stop();

        LookupSerialCommand = new RelayCommand(LookupSerialFromInput);
        StartCommand = new AsyncRelayCommand(StartRunAsync, () => !IsRunning && PlcOnline, ReportError);
        StopCommand = new AsyncRelayCommand(StopRunAsync, () => IsRunning, ReportError);
        ResetCommand = new AsyncRelayCommand(ResetAsync, null, ReportError);
        SaveCommand = new RelayCommand(SaveCurrent, () => MeasuredValue.HasValue && IsPass.HasValue && !_currentSaved);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _store.All.Count > 0, ReportError);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync, null, ReportError);
        OpenReportCommand = new RelayCommand(() => ShowReportDialog?.Invoke(_store.All));
        OpenStatisticsCommand = new RelayCommand(() => ShowStatisticsDialog?.Invoke(_store.All, _settings.ModelSpecs));
        LoginCommand = new RelayCommand(Login);

        RebuildHistogramGroups();
        RefreshTrend();
        RefreshStatistics();
    }

    // ----- Nguồn dữ liệu: PLC cung cấp hay phần mềm tự bù -----

    private PlcSettings P => _settings.Plc;
    public bool SerialFromPlc => P.SerialText.IsConfigured;
    private bool OrderFromPlc => P.OrderNoText.IsConfigured || P.LineText.IsConfigured || P.ModelText.IsConfigured;
    private bool SpecFromPlc => P.SpecLsl.IsConfigured && P.SpecUsl.IsConfigured;
    private bool JudgeFromPlc => P.JudgeOk.IsConfigured || P.JudgeNg.IsConfigured;
    private bool CountersFromPlc => P.TotalCount.IsConfigured;
    private bool RunningFromPlc => P.RunningState.IsConfigured;
    public string SerialPlaceholder => SerialFromPlc ? "Serial do PLC cung cấp" : "Scan / nhập số serial rồi nhấn Enter";

    // ----- Móc nối với View (hộp thoại) -----

    public Func<AppSettings, AppSettings?>? ShowSettingsDialog { get; set; }
    public Func<string, string?>? ChooseExportPath { get; set; }
    public Action<string, bool>? ShowMessage { get; set; }
    public Action<IReadOnlyList<MeasurementResult>>? ShowReportDialog { get; set; }
    public Action<IReadOnlyList<MeasurementResult>, IReadOnlyList<ModelSpec>>? ShowStatisticsDialog { get; set; }
    public Func<string, string?>? ShowLoginDialog { get; set; }

    // ----- Lệnh -----

    public ICommand LookupSerialCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand OpenStatisticsCommand { get; }
    public ICommand LoginCommand { get; }

    // ----- Tiêu đề / đồng hồ / người kiểm tra -----

    public string Title => _settings.Title;
    public string CompanyLabel => _settings.CompanyLabel;
    public string ForceUnit => _settings.ForceUnit;
    public string InspectorText => "Người KT: " + _settings.Inspector;
    public string VersionText => "VERSION: " + AppVersion + (_settings.Plc.Protocol == PlcProtocol.Simulation ? "  (DEMO)" : "");
    public int HistogramBins => _settings.HistogramBins;

    public string ConnectionText => _settings.Plc.Protocol switch
    {
        PlcProtocol.Simulation => "PLC MÔ PHỎNG",
        PlcProtocol.McProtocol3E => $"MC 3E  {_settings.Plc.IpAddress}:{_settings.Plc.Port}",
        PlcProtocol.ModbusTcp => $"MODBUS TCP  {_settings.Plc.IpAddress}:{_settings.Plc.Port}",
        _ => "",
    };

    private DateTime _now = DateTime.Now;
    public DateTime Now { get => _now; private set => SetProperty(ref _now, value); }

    // ----- Giá trị thời gian thực -----

    private double _force;
    public double Force
    {
        get => _force;
        private set { if (SetProperty(ref _force, value)) OnPropertyChanged(nameof(ForceText)); }
    }
    public string ForceText => Force.ToString("0.00", Inv);

    private double _distance;
    public double Distance
    {
        get => _distance;
        private set { if (SetProperty(ref _distance, value)) OnPropertyChanged(nameof(DistanceText)); }
    }
    public string DistanceText => Distance.ToString("0.00", Inv);

    private bool _plcOnline;
    public bool PlcOnline
    {
        get => _plcOnline;
        private set
        {
            if (!SetProperty(ref _plcOnline, value)) return;
            OnPropertyChanged(nameof(PlcStatusText));
            OnPropertyChanged(nameof(StartHint));
        }
    }
    public string PlcStatusText => PlcOnline ? "ONLINE" : "OFFLINE";

    private bool _loadcellStable;
    public bool LoadcellStable
    {
        get => _loadcellStable;
        private set { if (SetProperty(ref _loadcellStable, value)) OnPropertyChanged(nameof(LoadcellStatusText)); }
    }
    public string LoadcellStatusText => LoadcellStable ? "STABLE" : "UNSTABLE";

    // ----- Trạng thái chạy -----

    private bool _isRunning;
    /// <summary>Đang chạy: theo bit trạng thái của PLC nếu có, không thì theo nút START/STOP.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (SetProperty(ref _isRunning, value)) RaiseJudgeChanged(); }
    }

    // ----- Thông tin đơn hàng -----

    private string _serialInput = "";
    public string SerialInput
    {
        get => _serialInput;
        set { if (SetProperty(ref _serialInput, value)) OnPropertyChanged(nameof(StartHint)); }
    }

    private string _serial = "";
    public string Serial
    {
        get => _serial;
        private set
        {
            if (!SetProperty(ref _serial, value)) return;
            if (SerialFromPlc) SerialInput = value;   // ô serial hiển thị đúng chuỗi PLC gửi
            OnPropertyChanged(nameof(StartHint));
        }
    }

    private string _orderNo = "";
    public string OrderNo { get => _orderNo; private set => SetProperty(ref _orderNo, value); }

    private string _line = "";
    public string Line { get => _line; private set => SetProperty(ref _line, value); }

    private string _model = "";
    public string Model { get => _model; private set => SetProperty(ref _model, value); }

    private string _specText = "";
    public string SpecText { get => _specText; private set => SetProperty(ref _specText, value); }

    private string _note = "";
    public string Note { get => _note; set => SetProperty(ref _note, value); }

    private double? _measuredValue;
    public double? MeasuredValue
    {
        get => _measuredValue;
        private set
        {
            if (!SetProperty(ref _measuredValue, value)) return;
            OnPropertyChanged(nameof(MeasuredValueText));
            RaiseJudgeChanged();
        }
    }
    public string MeasuredValueText => MeasuredValue is { } v ? v.ToString("0.00", Inv) : "--";

    private bool? _isPass;
    public bool? IsPass
    {
        get => _isPass;
        private set { if (SetProperty(ref _isPass, value)) RaiseJudgeChanged(); }
    }

    /// <summary>Trạng thái khối PHÂN ĐỊNH: None / Waiting / Pass / Ng (dùng cho màu sắc trên giao diện).</summary>
    public string JudgeState => IsPass switch
    {
        true => "Pass",
        false => "Ng",
        null => IsRunning ? "Waiting" : "None",
    };

    public string JudgeText
    {
        get
        {
            if (IsPass is { } ok) return ok ? "PASS" : "NG";
            if (!IsRunning) return "---";
            if (!_awaitingRecord) return "ĐANG CHỜ";
            if (!SerialFromPlc && !HasFreshSerial) return "CHỜ SERIAL";
            return "CHỜ KẾT QUẢ";
        }
    }

    private void RaiseJudgeChanged()
    {
        OnPropertyChanged(nameof(JudgeState));
        OnPropertyChanged(nameof(JudgeText));
        OnPropertyChanged(nameof(StartHint));
    }

    /// <summary>Dòng hướng dẫn hiển thị dưới khối thông tin đơn hàng.</summary>
    public string StartHint
    {
        get
        {
            if (!PlcOnline)
                return "PLC chưa kết nối – kiểm tra CÀI ĐẶT kết nối.";
            if (!IsRunning)
                return "Nhấn START để bắt đầu.";
            if (_awaitingRecord && IsPass is null)
            {
                return JudgeFromPlc
                    ? "PLC báo đo xong – đang chờ PLC trả kết quả OK/NG."
                    : "Đo xong nhưng chưa có quy cách để phân định – kiểm tra chủng loại trong CÀI ĐẶT.";
            }
            if (_awaitingRecord && !SerialFromPlc && !HasFreshSerial)
                return "Đã nhận kết quả đo – scan serial để phân định và lưu.";
            if (SerialFromPlc)
                return string.IsNullOrWhiteSpace(Serial) ? "Đang chạy – chờ PLC gửi serial và kết quả đo." : "";
            if (string.IsNullOrWhiteSpace(Serial) || _serialConsumed)
                return "Đang chạy – scan số serial của sản phẩm.";
            if (!OrderFromPlc && string.IsNullOrWhiteSpace(Model))
                return "Serial không có trong file master – không phân định được PASS/NG.";
            if (!JudgeFromPlc && _currentSpec is null)
                return $"Chủng loại '{Model}' chưa có quy cách trong CÀI ĐẶT – không phân định được PASS/NG.";
            return "";
        }
    }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    // ----- Bộ đếm -----

    public string TotalText => _total.ToString(Inv);
    public string OkText => _okCount.ToString(Inv);
    public string NgText => _ngCount.ToString(Inv);

    // ----- Histogram theo nhóm -----

    public ObservableCollection<HistogramGroupViewModel> HistogramGroups { get; } = [];

    // ----- Trend chart -----

    private IReadOnlyList<DateTime> _trendCategories = [];
    public IReadOnlyList<DateTime> TrendCategories { get => _trendCategories; private set => SetProperty(ref _trendCategories, value); }

    private IReadOnlyList<TrendSeries> _trendLines = [];
    public IReadOnlyList<TrendSeries> TrendLines { get => _trendLines; private set => SetProperty(ref _trendLines, value); }

    private double _trendUsl = double.NaN;
    public double TrendUsl { get => _trendUsl; private set => SetProperty(ref _trendUsl, value); }

    private double _trendLsl = double.NaN;
    public double TrendLsl { get => _trendLsl; private set => SetProperty(ref _trendLsl, value); }

    // ================= Vòng đời =================

    public async Task StartAsync()
    {
        _clock.Start();
        Now = DateTime.Now;
        await _monitor.StartAsync(_settings.Plc);
        StatusMessage = _settings.Plc.Protocol == PlcProtocol.Simulation
            ? "Chế độ mô phỏng – nhấn START, PLC giả sẽ tự scan và đo từng sản phẩm"
            : $"Đang kết nối {ConnectionText}...";
    }

    public async ValueTask DisposeAsync()
    {
        _clock.Stop();
        await _monitor.StopAsync();
    }

    // ================= Xử lý dữ liệu PLC =================

    private void OnConnectionChanged(bool connected)
    {
        PlcOnline = connected;
        if (connected)
        {
            StatusMessage = "Đã kết nối PLC";
        }
        else
        {
            LoadcellStable = false;
            if (IsRunning && !RunningFromPlc)
            {
                IsRunning = false;
                StatusMessage = "Mất kết nối PLC – nhấn START lại khi kết nối trở lại";
            }
        }
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Mỗi chu kỳ poll: đổ toàn bộ dữ liệu PLC lên màn hình, bắt sườn lên Đo xong để ghi lịch sử.</summary>
    private void OnSnapshot(PlcSnapshot s)
    {
        Force = s.Force;
        Distance = s.Distance;
        LoadcellStable = s.LoadcellStable;

        if (RunningFromPlc) IsRunning = s.Running == true;

        if (SerialFromPlc)
        {
            var plcSerial = s.Serial ?? "";
            if (!string.Equals(plcSerial, _plcSerialLast, StringComparison.Ordinal))
            {
                _plcSerialLast = plcSerial;
                ApplySerial(plcSerial, fromPlc: true);
            }
        }

        if (P.OrderNoText.IsConfigured) OrderNo = s.OrderNo ?? "";
        if (P.LineText.IsConfigured) Line = s.Line ?? "";
        if (P.ModelText.IsConfigured)
        {
            var model = s.Model ?? "";
            if (!string.Equals(Model, model, StringComparison.Ordinal))
            {
                Model = model;
                if (!SpecFromPlc) ApplySpec(FindSpec(model));
            }
        }

        if (SpecFromPlc && s.Lsl is { } lsl && s.Usl is { } usl)
            ApplyPlcSpec(lsl, usl);

        if (P.ResultValue.IsConfigured && s.ResultValue is { } rv)
        {
            var rounded = Math.Round(rv, 2);
            if (MeasuredValue != rounded) MeasuredValue = rounded;
        }

        if (JudgeFromPlc)
        {
            bool? judge = s.JudgeOk == true ? true : s.JudgeNg == true ? false : null;
            if (IsPass != judge) IsPass = judge;
        }

        if (CountersFromPlc)
            SetCounters(s.Total ?? 0, s.Ok ?? 0, s.Ng ?? 0);

        bool rising = s.MeasureDone && !_lastMeasureDone;
        _lastMeasureDone = s.MeasureDone;

        if (rising) OnMeasureDone(s);
        else if (_awaitingRecord) TryRecord();
    }

    /// <summary>PLC báo đo xong: chốt giá trị, phân định (nếu PLC không làm) và ghi lịch sử.</summary>
    private void OnMeasureDone(PlcSnapshot s)
    {
        if (!P.ResultValue.IsConfigured) MeasuredValue = Math.Round(s.Distance, 2);
        _capturedForce = s.Force;
        _currentSaved = false;

        if (!JudgeFromPlc)
        {
            IsPass = _currentSpec is { } spec && MeasuredValue is { } v
                ? v >= spec.Lsl && v <= spec.Usl
                : null;
        }

        _awaitingRecord = true;
        TryRecord();
        RaiseJudgeChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Ghi lịch sử khi đã đủ điều kiện: có OK/NG và (nếu serial nhập tại phần mềm) có serial mới.</summary>
    private void TryRecord()
    {
        if (!_awaitingRecord) return;

        if (IsPass is not { } ok)
        {
            StatusMessage = JudgeFromPlc
                ? "PLC báo đo xong – chờ kết quả OK/NG"
                : "Đo xong nhưng chưa có quy cách để phân định";
            return;
        }
        if (!SerialFromPlc && !HasFreshSerial)
        {
            StatusMessage = "Đã nhận kết quả đo – scan serial để lưu";
            return;
        }

        _awaitingRecord = false;
        StatusMessage = ok ? "Kết quả: PASS" : "Kết quả: NG";
        if (_settings.AutoSaveOnMeasureDone) SaveCurrent();
        else StatusMessage += " – nhấn LƯU DỮ LIỆU để ghi lịch sử";
        RaiseJudgeChanged();
    }

    private bool HasFreshSerial => !string.IsNullOrWhiteSpace(Serial) && !_serialConsumed;

    /// <summary>Nhận serial mới (từ PLC hoặc gõ/scan tại phần mềm) và bù các trường PLC không cung cấp.</summary>
    private void ApplySerial(string serial, bool fromPlc)
    {
        bool pending = _awaitingRecord;
        if (!fromPlc && !pending) ClearCurrentMeasurement();

        Serial = serial;
        _serialConsumed = false;
        Note = "";

        if (!OrderFromPlc)
        {
            var info = serial.Length == 0 ? null : _master.Lookup(serial);
            OrderNo = info?.OrderNo ?? "";
            Line = info?.Line ?? "";
            Model = info?.Model ?? "";
            if (!SpecFromPlc) ApplySpec(FindSpec(Model));
            if (serial.Length > 0 && info is null)
                StatusMessage = $"Không tìm thấy serial '{serial}' trong file master ({_master.FilePath})";
            else if (info is not null)
                StatusMessage = $"Đơn hàng {info.OrderNo} – {info.Model}";
        }
        else if (serial.Length > 0)
        {
            StatusMessage = "Nhận serial " + serial;
        }

        if (pending) TryRecord();
        RefreshTrend();
        RaiseJudgeChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyPlcSpec(double lsl, double usl)
    {
        if (usl <= lsl)
        {
            if (_currentSpec is not null) ApplySpec(null);
            return;
        }
        if (_currentSpec is { } cur && cur.Lsl == lsl && cur.Usl == usl) return;
        ApplySpec(new ModelSpec { Model = Model, Lsl = lsl, Usl = usl, Unit = "mm" });
    }

    private void ApplySpec(ModelSpec? spec)
    {
        _currentSpec = spec;
        SpecText = spec is null ? "--" : FormatSpec(spec);
        RefreshTrend();
        OnPropertyChanged(nameof(StartHint));
    }

    private void SetCounters(int total, int ok, int ng)
    {
        if (_total == total && _okCount == ok && _ngCount == ng) return;
        _total = total; _okCount = ok; _ngCount = ng;
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(OkText));
        OnPropertyChanged(nameof(NgText));
    }

    // ================= Thao tác người dùng =================

    private void LookupSerialFromInput()
    {
        if (SerialFromPlc) return;          // serial do PLC cấp, ô nhập chỉ hiển thị
        var serial = SerialInput.Trim();
        if (serial.Length == 0) return;
        ApplySerial(serial, fromPlc: false);
    }

    private async Task StartRunAsync()
    {
        if (!PlcOnline)
        {
            StatusMessage = "PLC chưa kết nối, không thể START";
            return;
        }
        IsRunning = true;
        StatusMessage = "Đã gửi START – hiển thị theo dữ liệu PLC";
        try
        {
            await _monitor.WriteCommandAsync(P.StartCommand, true);
        }
        catch
        {
            IsRunning = false;
            throw;
        }
    }

    private async Task StopRunAsync()
    {
        if (!RunningFromPlc) IsRunning = false;
        StatusMessage = "Đã gửi STOP";
        if (PlcOnline)
            await _monitor.WriteCommandAsync(P.StopCommand, true);
        if (RunningFromPlc) IsRunning = false;
    }

    private async Task ResetAsync()
    {
        IsRunning = false;
        _awaitingRecord = false;
        ClearCurrentMeasurement();
        Serial = OrderNo = Line = Model = "";
        SerialInput = "";
        _plcSerialLast = "";
        ApplySpec(null);
        Note = "";
        _serialConsumed = false;
        RaiseJudgeChanged();
        StatusMessage = "Đã RESET";
        if (PlcOnline)
            await _monitor.WriteCommandAsync(P.ResetCommand, true);
    }

    private void SaveCurrent()
    {
        if (MeasuredValue is not { } value)
        {
            StatusMessage = "Chưa có kết quả đo để lưu";
            return;
        }
        if (_currentSaved)
        {
            StatusMessage = "Kết quả này đã được lưu rồi";
            return;
        }
        if (IsPass is not { } ok)
        {
            StatusMessage = "Không thể lưu: chưa có kết quả OK/NG";
            return;
        }

        var result = new MeasurementResult
        {
            No = _store.NextNo,
            OrderNo = OrderNo,
            Line = Line,
            Model = Model,
            Serial = Serial,
            Force = Math.Round(_capturedForce, 2),
            Value = value,
            Lsl = _currentSpec?.Lsl ?? 0,
            Usl = _currentSpec?.Usl ?? 0,
            IsOk = ok,
            InspectedAt = DateTime.Now,
            Inspector = _settings.Inspector,
            Note = Note.Trim(),
            PcName = Environment.MachineName,
        };

        try
        {
            _store.Append(result);
        }
        catch (Exception ex)
        {
            ReportError(new IOException("Không ghi được file kết quả: " + ex.Message, ex));
            return;
        }

        _currentSaved = true;
        _serialConsumed = true;
        _awaitingRecord = false;
        if (!CountersFromPlc) RefreshStatistics();
        RefreshHistograms();
        RefreshTrend();
        StatusMessage = $"Đã lưu kết quả #{result.No} ({result.ResultText})";
        RaiseJudgeChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ExportAsync()
    {
        var defaultName = $"ExportResultData_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var path = ChooseExportPath?.Invoke(defaultName);
        if (string.IsNullOrEmpty(path)) return;

        var rows = _store.All.ToList();
        StatusMessage = "Đang xuất Excel...";
        await Task.Run(() => ExcelExporter.Export(rows, path));
        StatusMessage = $"Đã xuất {rows.Count} dòng ra Excel";
        ShowMessage?.Invoke($"Đã xuất {rows.Count} kết quả ra file:\n{path}", false);
    }

    private async Task OpenSettingsAsync()
    {
        var updated = ShowSettingsDialog?.Invoke(_settings);
        if (updated is null) return;
        await ApplySettingsAsync(updated);
    }

    private void Login()
    {
        var code = ShowLoginDialog?.Invoke(_settings.Inspector);
        if (string.IsNullOrWhiteSpace(code)) return;
        _settings.Inspector = code.Trim();
        TrySaveSettings();
        OnPropertyChanged(nameof(InspectorText));
        StatusMessage = $"Đã đăng nhập người kiểm tra: {_settings.Inspector}";
    }

    private void ResetHistogram(string group)
    {
        _settings.HistogramResetTimes[group] = DateTime.Now;
        TrySaveSettings();
        RefreshHistograms();
        StatusMessage = $"Đã reset histogram {group} – chỉ hiển thị kết quả đo từ bây giờ";
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        _settings = settings;
        SettingsService.Save(settings);

        _master = new MasterDataService(SettingsService.ResolvePath(settings.MasterFilePath));

        var resultPath = SettingsService.ResolvePath(settings.ResultFilePath);
        if (!string.Equals(resultPath, _store.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            _store = new ResultStore(resultPath);
            TryLoadStore();
        }

        IsRunning = false;
        _awaitingRecord = false;
        _lastMeasureDone = false;
        _plcSerialLast = "";
        ClearCurrentMeasurement();
        if (!SpecFromPlc) ApplySpec(FindSpec(Model));
        RebuildHistogramGroups();
        RefreshStatistics();
        RefreshTrend();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CompanyLabel));
        OnPropertyChanged(nameof(ForceUnit));
        OnPropertyChanged(nameof(InspectorText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(HistogramBins));
        OnPropertyChanged(nameof(SerialFromPlc));
        OnPropertyChanged(nameof(SerialPlaceholder));
        RaiseJudgeChanged();

        await _monitor.StartAsync(settings.Plc);
        StatusMessage = "Đã áp dụng cài đặt mới, đang kết nối lại PLC...";
    }

    // ================= Hỗ trợ =================

    private void ClearCurrentMeasurement()
    {
        MeasuredValue = null;
        IsPass = null;
        _currentSaved = false;
        _capturedForce = 0;
    }

    private ModelSpec? FindSpec(string model)
        => string.IsNullOrWhiteSpace(model)
            ? null
            : _settings.ModelSpecs.FirstOrDefault(m => string.Equals(m.Model.Trim(), model.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string FormatSpec(ModelSpec spec)
        => string.Create(Inv, $"{spec.Lsl:0.#} ~ {spec.Usl:0.#} {spec.Unit}");

    private void TryLoadStore()
    {
        try { _store.Load(); }
        catch (Exception ex) { StatusMessage = "Không đọc được file kết quả: " + ex.Message; }
    }

    private void TrySaveSettings()
    {
        try { SettingsService.Save(_settings); }
        catch (Exception ex) { StatusMessage = "Không lưu được cài đặt: " + ex.Message; }
    }

    /// <summary>Bộ đếm theo lịch sử đã lưu (chỉ khi PLC không cung cấp bộ đếm).</summary>
    private void RefreshStatistics()
    {
        if (CountersFromPlc) return;
        int total = _store.All.Count;
        int ok = _store.All.Count(r => r.IsOk);
        SetCounters(total, ok, total - ok);
    }

    /// <summary>Tạo lại danh sách khối histogram theo các nhóm trong cài đặt quy cách.</summary>
    private void RebuildHistogramGroups()
    {
        var groups = _settings.ModelSpecs
            .Select(m => m.Group.Trim())
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count == 0) groups.Add(AllGroupName);

        HistogramGroups.Clear();
        foreach (var g in groups)
            HistogramGroups.Add(new HistogramGroupViewModel(g, ResetHistogram));
        RefreshHistograms();
    }

    private void RefreshHistograms()
    {
        foreach (var hg in HistogramGroups)
        {
            var specs = _settings.ModelSpecs
                .Where(m => string.Equals(m.Group.Trim(), hg.Group, StringComparison.OrdinalIgnoreCase))
                .ToList();

            IEnumerable<MeasurementResult> source = _store.All;
            if (specs.Count > 0)
            {
                var models = specs.Select(m => m.Model.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                source = source.Where(r => models.Contains(r.Model.Trim()));
            }
            if (_settings.HistogramResetTimes.TryGetValue(hg.Group, out var resetAt))
                source = source.Where(r => r.InspectedAt > resetAt);

            hg.Values = source.Select(r => r.Value).ToList();
            var spec = specs.FirstOrDefault();
            hg.Lsl = spec?.Lsl ?? double.NaN;
            hg.Usl = spec?.Usl ?? double.NaN;
            hg.SpecText = spec is null ? "" : string.Create(Inv, $"Quy cách: {spec.Lsl:0.#}~{spec.Usl:0.#}{spec.Unit}");
        }
    }

    /// <summary>Trend theo Line: trung bình mỗi ngày, lấy N ngày gần nhất có dữ liệu.</summary>
    private void RefreshTrend()
    {
        var dates = _store.All
            .Select(r => r.InspectedAt.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(Math.Max(2, _settings.TrendDays))
            .OrderBy(d => d)
            .ToList();

        var inRange = dates.Count == 0
            ? []
            : _store.All.Where(r => r.InspectedAt.Date >= dates[0]).ToList();

        var lines = inRange
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Line) ? "(không có line)" : r.Line.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TrendSeries(
                g.Key,
                dates.Select(d =>
                {
                    var vals = g.Where(r => r.InspectedAt.Date == d).Select(r => r.Value).ToList();
                    return vals.Count == 0 ? double.NaN : Math.Round(vals.Average(), 3);
                }).ToList()))
            .ToList();

        TrendCategories = dates;
        TrendLines = lines;

        var spec = _currentSpec ?? _settings.ModelSpecs.FirstOrDefault();
        TrendUsl = spec?.Usl ?? double.NaN;
        TrendLsl = spec?.Lsl ?? double.NaN;
    }

    private void ReportError(Exception ex)
    {
        StatusMessage = "Lỗi: " + ex.Message;
        if (ex is IOException or UnauthorizedAccessException)
            ShowMessage?.Invoke(ex.Message, true);
    }
}
