using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.Services;

namespace HeThongDoKhoangCach.ViewModels;

/// <summary>Một khối histogram trên màn hình chính (một nhóm chủng loại có cùng quy cách).</summary>
public sealed class HistogramGroupViewModel : ViewModelBase
{
    public HistogramGroupViewModel(string group) => Group = group;

    public string Group { get; }
    public string Title => "QUY CÁCH: " + Group;

    private IReadOnlyList<double> _values = [];
    public IReadOnlyList<double> Values { get => _values; set => SetProperty(ref _values, value); }

    private double _lsl = double.NaN;
    public double Lsl { get => _lsl; set => SetProperty(ref _lsl, value); }

    private double _usl = double.NaN;
    public double Usl { get => _usl; set => SetProperty(ref _usl, value); }

    private string _specText = "";
    public string SpecText { get => _specText; set => SetProperty(ref _specText, value); }
}

/// <summary>Chọn quy cách → model → scan serial → START → chốt kết quả và QR.</summary>
public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    public const string AppVersion = "1.2.1";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly PlcMonitor _monitor = new();
    private readonly DispatcherTimer _clock;

    private AppSettings _settings;
    private ResultStore _store;

    // ----- phát hiện kết quả mới từ PLC -----
    private bool _hasSnapshot;
    private bool _lastMeasureDone;
    private int _lastTotal;
    private double _lastResultValue;
    /// <summary>Đã có kết quả mới nhưng chưa ghi được lịch sử (chờ OK/NG từ PLC hoặc chưa có quy cách).</summary>
    private bool _pendingRecord;
    private SpecDefinition? _currentSpec;
    private ModelDefinition? _selectedModel;
    private bool _measurementActive, _serialReady, _starting;
    private readonly Queue<MeasurementResult> _unsavedResults = new();
    private MeasurementResult? _completedResult;
    private MeasurementResult? _cycleResult;
    private readonly SemaphoreSlim _modelWriteGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    public ObservableCollection<SpecDefinition> Specs { get; } = [];
    public IReadOnlyList<ModelDefinition> AvailableModels => SelectedSpec?.Models.ToList() ?? [];
    public bool CanEditSelection => !_measurementActive && !_starting && !MachineRunning && !_pendingRecord && _unsavedResults.Count == 0;
    public SpecDefinition? SelectedSpec
    {
        get => _currentSpec;
        set
        {
            if (!CanEditSelection || ReferenceEquals(_currentSpec, value)) return;
            _currentSpec = value;
            _selectedModel = null;
            Model = "";
            SpecText = value?.Name ?? "--";
            InvalidateSerial();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableModels));
            OnPropertyChanged(nameof(SelectedModel));
            OnPropertyChanged(nameof(ConditionText));
            RememberSelection();
            _ = SendModelToPlcSafeAsync();
        }
    }
    public ModelDefinition? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!CanEditSelection || ReferenceEquals(_selectedModel, value)) return;
            if (value is not null && SelectedSpec?.Models.Contains(value) != true) return;
            _selectedModel = value;
            Model = value?.Name ?? "";
            InvalidateSerial();
            OnPropertyChanged();
            RememberSelection();
            _ = SendModelToPlcSafeAsync();
        }
    }
    public string ConditionText => SelectedSpec?.Condition ?? "";
    private string _resultQrText = "";
    public string ResultQrText { get => _resultQrText; private set => SetProperty(ref _resultQrText, value); }
    private void RememberSelection()
    {
        _settings.LastSpec = SelectedSpec?.Name ?? "";
        _settings.LastModel = Model;
        TrySaveSettings();
        RaiseJudgeChanged();
    }
    private void InvalidateSerial()
    {
        _serialReady = false;
        Serial = QrInput = "";
        ClearCurrentMeasurement();
    }
    private void LoadCatalog()
    {
        var database = new SpecDatabase(SettingsService.ResolvePath(_settings.DatabaseFilePath));
        bool firstUse = !File.Exists(database.FilePath);
        var specs = database.Load();
        if (firstUse && P.Protocol == PlcProtocol.ModbusTcp)
        {
            foreach (var model in specs.SelectMany(s => s.Models))
                if (model.PlcBit.StartsWith('M')) model.PlcBit = "C" + model.PlcBit[1..];
            database.Save(specs);
        }
        Specs.Clear();
        foreach (var spec in specs) Specs.Add(spec);
        _currentSpec = Specs.FirstOrDefault(s => s.Name == _settings.LastSpec);
        _selectedModel = _currentSpec?.Models.FirstOrDefault(m => m.Name == _settings.LastModel);
        Model = _selectedModel?.Name ?? "";
        SpecText = _currentSpec?.Name ?? "--";
        OnPropertyChanged(nameof(SelectedSpec));
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(AvailableModels));
        OnPropertyChanged(nameof(ConditionText));
    }
    private bool _plcActive;
    private int _total, _okCount, _ngCount;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _store = new ResultStore(SettingsService.ResolvePath(settings.ResultFilePath));
        TryLoadStore();
        LoadCatalog();

        _monitor.SnapshotReceived += s => _dispatcher.InvokeAsync(() => OnSnapshot(s));
        _monitor.ConnectionChanged += c => _dispatcher.InvokeAsync(() => OnConnectionChanged(c));
        _monitor.ErrorOccurred += m => _dispatcher.InvokeAsync(() => StatusMessage = "Lỗi PLC: " + m);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Now = DateTime.Now, _dispatcher);
        _clock.Stop();

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => CanEditSelection && SelectedModel is not null, ReportError);
        ConnectCommand = new AsyncRelayCommand(ToggleConnectAsync, null, ReportError);
        StartCommand = new AsyncRelayCommand(StartMachineAsync, () => PlcOnline && _hasSnapshot && CanEditSelection && SelectedModel is not null && (_serialReady || !string.IsNullOrWhiteSpace(QrInput)), ReportError);
        StopCommand = new AsyncRelayCommand(StopMachineAsync, () => PlcOnline, ReportError);
        ResetCommand = new AsyncRelayCommand(ResetAsync, null, ReportError);
        SaveCommand = new RelayCommand(SaveCurrent, () => _unsavedResults.Count > 0);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => RecentRows.Count > 0, ReportError);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync, () => CanEditSelection, ReportError);
        OpenReportCommand = new RelayCommand(() => ShowReportDialog?.Invoke(_store.All));
        OpenStatisticsCommand = new RelayCommand(() => ShowStatisticsDialog?.Invoke(_store.All, Specs.ToList()));
        LoginCommand = new RelayCommand(Login);
        FilterTodayCommand = new RelayCommand(() => SetDateRange(DateTime.Today, DateTime.Today));
        FilterWeekCommand = new RelayCommand(() => SetDateRange(DateTime.Today.AddDays(-6), DateTime.Today));
        FilterMonthCommand = new RelayCommand(() => SetDateRange(DateTime.Today.AddDays(-29), DateTime.Today));
        FilterAllCommand = new RelayCommand(() => SetDateRange(null, null));

        RebuildHistogramGroups();
        RefreshView();
    }

    // ----- Nguồn dữ liệu: PLC cung cấp hay phần mềm tự bù -----

    private PlcSettings P => _settings.Plc;
    private bool JudgeFromPlc => P.JudgeOk.IsConfigured || P.JudgeNg.IsConfigured;
    private bool CountersFromPlc => P.TotalCount.IsConfigured;
    private bool RunningFromPlc => P.RunningState.IsConfigured;

    // ----- Móc nối với View (hộp thoại) -----

    public Func<AppSettings, AppSettings?>? ShowSettingsDialog { get; set; }
    public Func<string, string?>? ChooseExportPath { get; set; }
    public Action<string, bool>? ShowMessage { get; set; }
    public Action<IReadOnlyList<MeasurementResult>>? ShowReportDialog { get; set; }
    public Action<IReadOnlyList<MeasurementResult>, IReadOnlyList<SpecDefinition>>? ShowStatisticsDialog { get; set; }
    public Func<string, string?>? ShowLoginDialog { get; set; }

    // ----- Lệnh -----

    public ICommand ScanCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand OpenStatisticsCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand FilterTodayCommand { get; }
    public ICommand FilterWeekCommand { get; }
    public ICommand FilterMonthCommand { get; }
    public ICommand FilterAllCommand { get; }

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

    // ----- Giá trị PLC gửi liên tục -----

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

    private double? _measuredValue;
    /// <summary>Kết quả của lượt đo vừa hoàn tất, giữ nguyên đến lần scan hoặc RESET tiếp theo.</summary>
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
    /// <summary>Chưa nhận tín hiệu đo xong → hiển thị "--".</summary>
    public string MeasuredValueText => MeasuredValue is { } v ? v.ToString("0.00", Inv) : "--";

    // ----- Kết nối / trạng thái -----

    private bool _plcOnline;
    public bool PlcOnline
    {
        get => _plcOnline;
        private set
        {
            if (!SetProperty(ref _plcOnline, value)) return;
            OnPropertyChanged(nameof(PlcStatusText));
            OnPropertyChanged(nameof(ScanHint));
        }
    }
    public string PlcStatusText => PlcOnline ? "ONLINE" : _plcActive ? "ĐANG KẾT NỐI" : "OFFLINE";

    /// <summary>Đã bấm KẾT NỐI PLC (vòng đọc đang chạy, có thể đang thử kết nối lại).</summary>
    public bool PlcActive
    {
        get => _plcActive;
        private set
        {
            if (!SetProperty(ref _plcActive, value)) return;
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(PlcStatusText));
            OnPropertyChanged(nameof(ScanHint));
        }
    }
    public string ConnectButtonText => PlcActive ? "NGẮT KẾT NỐI" : "KẾT NỐI PLC";

    private bool _loadcellStable;
    public bool LoadcellStable
    {
        get => _loadcellStable;
        private set { if (SetProperty(ref _loadcellStable, value)) OnPropertyChanged(nameof(LoadcellStatusText)); }
    }
    public string LoadcellStatusText => LoadcellStable ? "STABLE" : "UNSTABLE";

    private bool _machineRunning;
    /// <summary>Máy đang chạy: theo bit trạng thái của PLC nếu có, không thì theo nút START/STOP vừa bấm.</summary>
    public bool MachineRunning
    {
        get => _machineRunning;
        private set
        {
            if (!SetProperty(ref _machineRunning, value)) return;
            OnPropertyChanged(nameof(MachineStateText));
            RaiseJudgeChanged();
        }
    }
    public string MachineStateText => MachineRunning ? "ĐANG CHẠY" : "DỪNG";

    // ----- Serial và thông tin đo -----

    private string _qrInput = "";
    public string QrInput { get => _qrInput; set => SetProperty(ref _qrInput, value); }
    public string QrPlaceholder => "Scan mã vạch serial, kết thúc bằng Enter";

    private string _serial = "";
    public string Serial { get => _serial; private set { if (SetProperty(ref _serial, value)) OnPropertyChanged(nameof(ScanHint)); } }

    private string _model = "";
    public string Model { get => _model; private set { if (SetProperty(ref _model, value)) OnPropertyChanged(nameof(ScanHint)); } }

    private string _specText = "--";
    public string SpecText { get => _specText; private set => SetProperty(ref _specText, value); }

    private string _modelBitText = "";
    /// <summary>Bit PLC đang bật cho chủng loại hiện tại (hiển thị cạnh chủng loại).</summary>
    public string ModelBitText { get => _modelBitText; private set => SetProperty(ref _modelBitText, value); }

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
        null => _pendingRecord || _measurementActive || _starting || MachineRunning ? "Waiting" : "None",
    };

    public string JudgeText
    {
        get
        {
            if (IsPass is { } ok) return ok ? "PASS" : "NG";
            if (_pendingRecord) return "CHỜ KẾT QUẢ";
            return _measurementActive || _starting || MachineRunning ? "ĐANG ĐO" : "---";
        }
    }

    private void RaiseJudgeChanged()
    {
        OnPropertyChanged(nameof(CanEditSelection));
        CommandManager.InvalidateRequerySuggested();
        OnPropertyChanged(nameof(JudgeState));
        OnPropertyChanged(nameof(JudgeText));
        OnPropertyChanged(nameof(ScanHint));
    }

    /// <summary>Dòng hướng dẫn (đỏ) dưới khối thông tin đo.</summary>
    public string ScanHint
    {
        get
        {
            if (!PlcActive) return "PLC chưa kết nối – nhấn KẾT NỐI PLC ở thanh trạng thái.";
            if (!PlcOnline) return "Đang kết nối PLC...";
            if (_unsavedResults.Count > 0) return $"Có {_unsavedResults.Count} kết quả chờ lưu – nhấn LƯU DỮ LIỆU.";
            if (SelectedSpec is null) return "Bước 1: Chọn quy cách đo.";
            if (SelectedModel is null) return "Bước 2: Chọn chủng loại thuộc quy cách.";
            if (_pendingRecord) return "Đang chờ bit OK/NG từ PLC.";
            if (_measurementActive || _starting || MachineRunning) return "Đang đo – chờ kết quả từ PLC.";
            if (!_serialReady) return "Bước 3: Scan serial cho lượt đo tiếp theo.";
            return "Bước 4: Nhấn START để đo một lần với serial này.";
        }
    }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    // ----- Bộ đếm -----

    public string TotalText => _total.ToString("#,##0", Inv);
    public string OkText => _okCount.ToString("#,##0", Inv);
    public string NgText => _ngCount.ToString("#,##0", Inv);

    // ----- Histogram theo nhóm + bảng kết quả (cùng bộ lọc ngày) -----

    public ObservableCollection<HistogramGroupViewModel> HistogramGroups { get; } = [];

    private IReadOnlyList<MeasurementResult> _recentRows = [];
    public IReadOnlyList<MeasurementResult> RecentRows { get => _recentRows; private set => SetProperty(ref _recentRows, value); }

    private string _filterSummary = "";
    public string FilterSummary { get => _filterSummary; private set => SetProperty(ref _filterSummary, value); }

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set { if (SetProperty(ref _fromDate, value?.Date)) RefreshView(); }
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set { if (SetProperty(ref _toDate, value?.Date)) RefreshView(); }
    }

    private void SetDateRange(DateTime? from, DateTime? to)
    {
        bool changed = _fromDate != from || _toDate != to;
        _fromDate = from;
        _toDate = to;
        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));
        if (changed) RefreshView();
    }

    // ================= Vòng đời =================

    public async Task StartAsync()
    {
        _clock.Start();
        Now = DateTime.Now;
        if (_settings.AutoConnectPlc) await ConnectAsync();
        else StatusMessage = "Nhấn KẾT NỐI PLC để bắt đầu đọc dữ liệu";
    }

    public async ValueTask DisposeAsync()
    {
        _clock.Stop();
        await _monitor.StopAsync();
    }

    // ================= Kết nối PLC =================

    private Task ToggleConnectAsync() => PlcActive ? DisconnectAsync() : ConnectAsync();

    private async Task ConnectAsync()
    {
        PlcActive = true;
        ResetDetection();
        await _monitor.StartAsync(_settings.Plc, Specs.ToList());
        StatusMessage = _settings.Plc.Protocol == PlcProtocol.Simulation
            ? "Chế độ mô phỏng – chọn quy cách, model, scan serial rồi START"
            : $"Đang kết nối {ConnectionText}...";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task DisconnectAsync()
    {
        await _monitor.StopAsync();
        PlcActive = false;
        PlcOnline = false;
        LoadcellStable = false;
        StatusMessage = "Đã ngắt kết nối PLC";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnConnectionChanged(bool connected)
    {
        PlcOnline = connected;
        if (connected)
        {
            StatusMessage = "Đã kết nối PLC";
            ResetDetection();
            // PLC có thể vừa khởi động lại: gửi lại bit chủng loại đang chọn.
            _ = SendModelToPlcSafeAsync();
        }
        else
        {
            _measurementActive = _pendingRecord = false;
            _cycleResult = null;
            MachineRunning = false;
            RaiseJudgeChanged();
            LoadcellStable = false;
            if (PlcActive) StatusMessage = "Mất kết nối PLC – đang thử kết nối lại...";
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private void ResetDetection()
    {
        _hasSnapshot = false;
        _lastMeasureDone = false;
        _lastTotal = 0;
        _lastResultValue = 0;
    }

    // ================= Xử lý dữ liệu PLC =================

    /// <summary>Mỗi chu kỳ poll: đổ toàn bộ dữ liệu PLC lên màn hình, phát hiện kết quả mới để ghi lịch sử.</summary>
    private void OnSnapshot(PlcSnapshot s)
    {
        Force = s.Force;
        Distance = s.Distance;
        LoadcellStable = s.LoadcellStable;

        if (RunningFromPlc) MachineRunning = s.Running == true;

        if (_pendingRecord && JudgeFromPlc)
            IsPass = s.JudgeNg == true ? false : s.JudgeOk == true ? true : null;

        if (CountersFromPlc)
            SetCounters(s.Total ?? 0, s.Ok ?? 0, s.Ng ?? 0);

        if (DetectNewResult(s) && _measurementActive) OnNewResult(s);
        else if (_pendingRecord) TryRecord();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Kết quả mới = sườn lên bit Đo xong; nếu không cấu hình bit này thì khi bộ đếm TOTAL tăng;
    /// nếu cũng không có bộ đếm thì khi giá trị Kết quả đo thay đổi. Lần đọc đầu tiên sau khi kết nối không tính.
    /// </summary>
    private bool DetectNewResult(PlcSnapshot s)
    {
        bool first = !_hasSnapshot;
        _hasSnapshot = true;

        if (P.MeasureDone.IsConfigured)
        {
            bool done = s.MeasureDone == true;
            bool rising = done && !_lastMeasureDone && !first;
            _lastMeasureDone = done;
            return rising;
        }
        if (P.TotalCount.IsConfigured)
        {
            int total = s.Total ?? 0;
            bool increased = !first && total > _lastTotal;
            _lastTotal = total;
            return increased;
        }
        if (P.ResultValue.IsConfigured)
        {
            double v = s.ResultValue ?? 0;
            bool changed = !first && v != 0 && Math.Abs(v - _lastResultValue) > 1e-9;
            _lastResultValue = v;
            return changed;
        }
        return false;
    }

    /// <summary>PLC vừa có kết quả mới: chốt giá trị, phân định (nếu PLC không làm) và ghi lịch sử.</summary>
    private void OnNewResult(PlcSnapshot s)
    {
        // Mỗi START chỉ nhận một kết quả; lượt tiếp theo phải scan lại serial.
        _measurementActive = false;
        _serialReady = false;
        if (!RunningFromPlc) MachineRunning = false;
        ResultQrText = ""; // Không để QR của mẫu trước cạnh giá trị mới đang chờ OK/NG.
        if (!double.IsFinite(s.ResultValue ?? s.Distance) || !double.IsFinite(s.Force))
        {
            _pendingRecord = false;
            _cycleResult = null;
            StatusMessage = "Giá trị PLC không hợp lệ. Kiểm tra kiểu dữ liệu/hệ số của tag và đo lại.";
            RaiseJudgeChanged();
            return;
        }
        MeasuredValue = Math.Round(s.ResultValue ?? s.Distance, 2);
        if (JudgeFromPlc) IsPass = s.JudgeNg == true ? false : s.JudgeOk == true ? true : null;
        _cycleResult = new MeasurementResult
        {
            Serial = Serial, Model = Model,
            Lsl = SelectedSpec!.Lsl, Usl = SelectedSpec.Usl,
            Inspector = _settings.Inspector, PcName = Environment.MachineName,
            Value = MeasuredValue.Value,
            Force = Math.Round(s.Force, 2),
            InspectedAt = s.Timestamp == default ? DateTime.Now : s.Timestamp,
        };

        if (!JudgeFromPlc)
        {
            IsPass = _currentSpec is { } spec && MeasuredValue is { } v
                ? v >= spec.Lsl && v <= spec.Usl
                : null;
        }

        _pendingRecord = true;
        TryRecord();
        RaiseJudgeChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Ghi lịch sử khi đã có OK/NG (từ PLC hoặc tự so quy cách).</summary>
    private void TryRecord()
    {
        if (!_pendingRecord) return;

        if (IsPass is not { } ok)
        {
            StatusMessage = JudgeFromPlc
                ? "PLC báo có kết quả mới – đang chờ bit OK/NG"
                : "Có kết quả mới nhưng chưa có quy cách để phân định – kiểm tra danh mục quy cách.";
            return;
        }

        if (_cycleResult is null) return;
        _cycleResult.IsOk = ok;
        _completedResult = _cycleResult;
        _cycleResult = null;
        var qr = ResultQrFormatter.Format(_settings.ResultQrTemplate, _completedResult);
        bool qrTooLong = System.Text.Encoding.UTF8.GetByteCount(qr) > 1500;
        ResultQrText = qrTooLong ? "" : qr;
        _completedResult.QrText = ResultQrText;
        _unsavedResults.Enqueue(_completedResult);
        _pendingRecord = false;
        if (_settings.AutoSaveOnMeasureDone) SaveCurrent();
        else StatusMessage = (ok ? "Kết quả: PASS" : "Kết quả: NG") + " – nhấn LƯU DỮ LIỆU để ghi lịch sử";
        if (qrTooLong) StatusMessage = "Kết quả đã chốt nhưng nội dung QR quá dài. Rút ngắn mẫu trong CÀI ĐẶT.";
        RaiseJudgeChanged();
    }

    private void SetCounters(int total, int ok, int ng)
    {
        if (_total == total && _okCount == ok && _ngCount == ng) return;
        _total = total; _okCount = ok; _ngCount = ng;
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(OkText));
        OnPropertyChanged(nameof(NgText));
    }

    // ================= Scan serial của máy cần đo =================

    private Task ScanAsync()
    {
        if (!CanEditSelection || SelectedSpec is null || SelectedModel is null) return Task.CompletedTask;
        var serial = QrInput.Trim();
        if (serial.Length == 0) return Task.CompletedTask;
        _serialReady = false;
        if (serial.Length > 256 || serial.Any(char.IsControl))
        {
            StatusMessage = "Serial không hợp lệ (tối đa 256 ký tự, không chứa ký tự điều khiển).";
            return Task.CompletedTask;
        }
        ClearCurrentMeasurement();
        Serial = serial;
        QrInput = "";
        _serialReady = true;
        StatusMessage = $"Đã nhận serial {Serial} – {Model}, {SpecText}. Nhấn START.";
        RaiseJudgeChanged();
        return Task.CompletedTask;
    }

    /// <summary>Tắt mọi bit chủng loại khác, bật bit của chủng loại hiện tại; ghi LSL/USL xuống PLC nếu có cấu hình.</summary>
    private async Task SendModelToPlcAsync()
    {
        await _modelWriteGate.WaitAsync();
        try
        {
            var target = (SelectedModel?.PlcBit ?? "").Trim();
            ModelBitText = target;

            if (!PlcOnline)
            {
                if (Model.Length > 0) StatusMessage = "PLC chưa kết nối – bit chủng loại sẽ được gửi khi kết nối lại";
                return;
            }

            var allBits = SpecCatalog.AllPlcBits(Specs);

            foreach (var bit in allBits.Where(b => !SameAddress(b, target)))
                await _monitor.WriteBitAsync(bit, false);
            if (target.Length > 0)
                await _monitor.WriteBitAsync(target, true);

            if (_currentSpec is { } spec)
            {
                await _monitor.WriteNumberAsync(P.SpecLslWrite, spec.Lsl);
                await _monitor.WriteNumberAsync(P.SpecUslWrite, spec.Usl);
            }

            if (target.Length > 0)
                StatusMessage = $"Đã bật bit {target} cho chủng loại {Model} – quy cách {SpecText}";
            else if (Model.Length > 0)
                StatusMessage = $"Chủng loại '{Model}' chưa gán bit PLC trong CÀI ĐẶT – PLC chưa nhận được chủng loại";
        }
        finally { _modelWriteGate.Release(); }
    }

    private async Task SendModelToPlcSafeAsync()
    {
        try { await SendModelToPlcAsync(); }
        catch (Exception ex) { ReportError(ex); }
    }

    private static bool SameAddress(string a, string b)
        => string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

    // ================= START / STOP / RESET → bit lệnh xuống PLC =================

    private async Task StartMachineAsync()
    {
        if (!CanEditSelection || !PlcOnline || !_hasSnapshot) return;
        if (!string.IsNullOrWhiteSpace(QrInput)) await ScanAsync();
        if (!_serialReady || SelectedSpec is null || SelectedModel is null) return;
        _starting = true;
        RaiseJudgeChanged();
        await _commandGate.WaitAsync();
        try
        {
            await SendModelToPlcAsync();
            ClearCurrentMeasurement();
            _measurementActive = true;
            await _monitor.WriteCommandAsync(P.StartCommand, true);
            if (!RunningFromPlc) MachineRunning = true;
            StatusMessage = $"Đã gửi START ({P.StartCommand.Address}) – đo một lần với serial {Serial}";
        }
        catch
        {
            _measurementActive = false;
            _cycleResult = null;
            throw;
        }
        finally { _commandGate.Release(); _starting = false; RaiseJudgeChanged(); }
    }

    private async Task StopMachineAsync()
    {
        await _commandGate.WaitAsync();
        try
        {
            await _monitor.WriteCommandAsync(P.StopCommand, true);
            _measurementActive = _pendingRecord = false;
            _cycleResult = null;
            if (!RunningFromPlc) MachineRunning = false;
            RaiseJudgeChanged();
            StatusMessage = $"Đã gửi STOP ({P.StopCommand.Address}) – máy tạm dừng";
        }
        finally { _commandGate.Release(); }
    }

    private async Task ResetAsync()
    {
        await _commandGate.WaitAsync();
        try
        {
            if (PlcOnline && P.ResetCommand.IsConfigured)
                await _monitor.WriteCommandAsync(P.ResetCommand, true);
            _measurementActive = _pendingRecord = false;
            _cycleResult = null;
            if (!RunningFromPlc) MachineRunning = false;
            InvalidateSerial();
            RaiseJudgeChanged();
            StatusMessage = "Đã RESET – scan serial mới để đo.";
        }
        finally { _commandGate.Release(); }
    }

    // ================= Lưu / xuất =================

    private void SaveCurrent()
    {
        int saved = 0;
        while (_unsavedResults.TryPeek(out var result))
        {
            result.No = _store.NextNo;
            try { _store.Append(result); }
            catch (Exception ex)
            {
                ReportError(new IOException("Không ghi được file kết quả: " + ex.Message, ex));
                break; // Giữ kết quả chưa ghi để thử lại, không mất các mẫu khi vẫn đang đo.
            }
            _unsavedResults.Dequeue();
            saved++;
            StatusMessage = $"Đã lưu kết quả #{result.No} ({result.ResultText}) – serial {result.Serial}";
        }
        if (saved > 0) RefreshView();
        RaiseJudgeChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ExportAsync()
    {
        var defaultName = $"ExportResultData_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var path = ChooseExportPath?.Invoke(defaultName);
        if (string.IsNullOrEmpty(path)) return;

        var rows = RecentRows.OrderBy(r => r.No).ToList();
        StatusMessage = "Đang xuất Excel...";
        await Task.Run(() => ExcelExporter.Export(rows, path));
        StatusMessage = $"Đã xuất {rows.Count} dòng ra Excel";
        ShowMessage?.Invoke($"Đã xuất {rows.Count} kết quả ({RangeText()}) ra file:\n{path}", false);
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

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        bool reconnect = PlcActive;
        // Xóa cả bit model vừa bị xóa khỏi danh mục trước khi chuyển sang cấu hình mới.
        await _modelWriteGate.WaitAsync();
        try
        {
            if (PlcOnline)
                foreach (var bit in SpecCatalog.AllPlcBits(Specs)) await _monitor.WriteBitAsync(bit, false);
        }
        finally { _modelWriteGate.Release(); }
        await DisconnectAsync();
        _settings = settings;
        SettingsService.Save(settings);


        var resultPath = SettingsService.ResolvePath(settings.ResultFilePath);
        if (!string.Equals(resultPath, _store.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            _store = new ResultStore(resultPath);
            TryLoadStore();
        }

        _pendingRecord = false;
        ClearCurrentMeasurement();
        if (!RunningFromPlc) MachineRunning = false;
        InvalidateSerial();
        LoadCatalog();
        RebuildHistogramGroups();
        RefreshView();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CompanyLabel));
        OnPropertyChanged(nameof(ForceUnit));
        OnPropertyChanged(nameof(InspectorText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(HistogramBins));
        RaiseJudgeChanged();

        if (reconnect)
        {
            await ConnectAsync();
            StatusMessage = "Đã áp dụng cài đặt mới, đang kết nối lại PLC...";
        }
        else
        {
            StatusMessage = "Đã áp dụng cài đặt mới";
        }
    }

    // ================= Bộ lọc ngày → bảng kết quả + histogram + bộ đếm =================

    private IEnumerable<MeasurementResult> FilteredResults()
    {
        IEnumerable<MeasurementResult> q = _store.All;
        if (_fromDate is { } from) q = q.Where(r => r.InspectedAt >= from);
        if (_toDate is { } to) q = q.Where(r => r.InspectedAt < to.AddDays(1));
        return q;
    }

    private string RangeText()
        => (_fromDate, _toDate) switch
        {
            (null, null) => "tất cả các ngày",
            ({ } f, null) => $"từ {f:dd/MM/yyyy}",
            (null, { } t) => $"đến {t:dd/MM/yyyy}",
            ({ } f, { } t) when f == t => $"ngày {f:dd/MM/yyyy}",
            ({ } f, { } t) => $"{f:dd/MM/yyyy} – {t:dd/MM/yyyy}",
        };

    private void RefreshView()
    {
        var rows = FilteredResults().ToList();
        RecentRows = rows.OrderByDescending(r => r.InspectedAt).ThenByDescending(r => r.No).ToList();

        int ok = rows.Count(r => r.IsOk);
        FilterSummary = $"{rows.Count:#,##0} kết quả ({RangeText()})   |   OK: {ok:#,##0}   |   NG: {rows.Count - ok:#,##0}";
        if (!CountersFromPlc) SetCounters(rows.Count, ok, rows.Count - ok);

        RefreshHistograms(rows);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Tạo lại danh sách khối histogram theo các nhóm trong cài đặt quy cách.</summary>
    private void RebuildHistogramGroups()
    {
        HistogramGroups.Clear();
        foreach (var spec in Specs)
            HistogramGroups.Add(new HistogramGroupViewModel(spec.Name)
            {
                Lsl = spec.Lsl, Usl = spec.Usl,
                SpecText = string.Join(" · ", spec.Models.Select(m => m.Name)),
            });
    }

    private void RefreshHistograms(IReadOnlyList<MeasurementResult> rows)
    {
        foreach (var hg in HistogramGroups)
            hg.Values = rows.Where(r => Math.Abs(r.Lsl - hg.Lsl) < 1e-6 && Math.Abs(r.Usl - hg.Usl) < 1e-6)
                            .Select(r => r.Value).ToList();
    }

    // ================= Hỗ trợ =================

    private void ClearCurrentMeasurement()
    {
        MeasuredValue = null;
        IsPass = null;
        _completedResult = null;
        ResultQrText = "";
        _unsavedResults.Clear();
    }

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

    private void ReportError(Exception ex)
    {
        StatusMessage = "Lỗi: " + ex.Message;
        if (ex is IOException or UnauthorizedAccessException)
            ShowMessage?.Invoke(ex.Message, true);
    }
}
