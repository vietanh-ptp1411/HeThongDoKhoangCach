using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows.Input;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.Services;
using BeltTensionMeasurement.Services.Plc;

namespace BeltTensionMeasurement.ViewModels;

public enum SettingsSection { Connection, Tags, Catalog, Qr, General }

/// <summary>Một dòng trong bảng địa chỉ PLC của màn hình cài đặt.</summary>
public sealed class TagRow : ViewModelBase
{
    public TagRow(string group, string name, string key, bool required)
    {
        Group = group;
        Name = name;
        Key = key;
        Required = required;
    }

    public string Group { get; }
    public string Name { get; }
    public string Key { get; }
    public bool Required { get; }
    public string DisplayName => Required ? Name + " *" : Name;

    private string _address = "";
    public string Address { get => _address; set => SetProperty(ref _address, value); }

    private TagDataType _dataType;
    public TagDataType DataType { get => _dataType; set => SetProperty(ref _dataType, value); }

    private double _scale = 1;
    public double Scale { get => _scale; set => SetProperty(ref _scale, value); }

    private WordOrder _wordOrder;
    public WordOrder WordOrder { get => _wordOrder; set => SetProperty(ref _wordOrder, value); }

    private int _length = 10;
    public int Length { get => _length; set => SetProperty(ref _length, value); }

    public void LoadFrom(TagDefinition t)
    {
        Address = t.Address;
        DataType = t.DataType;
        Scale = t.Scale;
        WordOrder = t.WordOrder;
        Length = t.Length;
    }

    public TagDefinition ToDefinition() => new()
    {
        Address = Address.Trim(),
        DataType = DataType,
        Scale = Scale == 0 ? 1 : Scale,
        WordOrder = WordOrder,
        Length = Length < 1 ? 1 : Length,
    };
}

public sealed class SettingsViewModel : ViewModelBase
{
    public sealed record ProtocolOption(PlcProtocol Value, string Label);

    public static IReadOnlyList<ProtocolOption> Protocols { get; } =
    [
        new(PlcProtocol.Simulation, "Mô phỏng (không cần PLC)"),
        new(PlcProtocol.McProtocol3E, "MC Protocol – 3E frame, Binary (Mitsubishi / SLMP)"),
        new(PlcProtocol.ModbusTcp, "Modbus TCP"),
    ];

    public static IReadOnlyList<TagDataType> DataTypes { get; } = Enum.GetValues<TagDataType>();
    public static IReadOnlyList<WordOrder> WordOrders { get; } = Enum.GetValues<WordOrder>();

    /// <summary>Danh mục tag: nhóm, tên hiển thị, thuộc tính trong <see cref="PlcSettings"/>, bắt buộc, getter/setter.</summary>
    private static readonly (string Group, string Name, string Key, bool Required, Func<PlcSettings, TagDefinition> Get, Action<PlcSettings, TagDefinition> Set)[] TagMap =
    [
        ("START", "App → PLC: START (đo một lần)", nameof(PlcSettings.StartCommand), true, p => p.StartCommand, (p, t) => p.StartCommand = t),
        ("Quy cách", "Mục đích đo (1..5)", nameof(PlcSettings.PurposeWrite), false, p => p.PurposeWrite, (p, t) => p.PurposeWrite = t),
        ("Lệnh", "STOP (tạm dừng)", nameof(PlcSettings.StopCommand), false, p => p.StopCommand, (p, t) => p.StopCommand = t),
        ("Lệnh", "RESET (xóa giá trị)", nameof(PlcSettings.ResetCommand), false, p => p.ResetCommand, (p, t) => p.ResetCommand = t),
        ("Giá trị đo", "Lực căng (Loadcell)", nameof(PlcSettings.LoadcellValue), true, p => p.LoadcellValue, (p, t) => p.LoadcellValue = t),
        ("Giá trị đo", "Khoảng cách", nameof(PlcSettings.DistanceValue), true, p => p.DistanceValue, (p, t) => p.DistanceValue = t),
        ("Giá trị đo", "Kết quả đo (chốt)", nameof(PlcSettings.ResultValue), false, p => p.ResultValue, (p, t) => p.ResultValue = t),
        ("Phân định", "Kết quả OK", nameof(PlcSettings.JudgeOk), false, p => p.JudgeOk, (p, t) => p.JudgeOk = t),
        ("Phân định", "Kết quả NG", nameof(PlcSettings.JudgeNg), false, p => p.JudgeNg, (p, t) => p.JudgeNg = t),
        ("Bộ đếm", "TOTAL", nameof(PlcSettings.TotalCount), false, p => p.TotalCount, (p, t) => p.TotalCount = t),
        ("Bộ đếm", "OK", nameof(PlcSettings.OkCount), false, p => p.OkCount, (p, t) => p.OkCount = t),
        ("Bộ đếm", "NG", nameof(PlcSettings.NgCount), false, p => p.NgCount, (p, t) => p.NgCount = t),
        ("Trạng thái", "Đo xong (kết quả mới)", nameof(PlcSettings.MeasureDone), false, p => p.MeasureDone, (p, t) => p.MeasureDone = t),
        ("Trạng thái", "Loadcell ổn định", nameof(PlcSettings.LoadcellStable), false, p => p.LoadcellStable, (p, t) => p.LoadcellStable = t),
        ("Trạng thái", "Máy đang chạy", nameof(PlcSettings.RunningState), false, p => p.RunningState, (p, t) => p.RunningState = t),
        ("Quy cách", "LSL ghi xuống PLC", nameof(PlcSettings.SpecLslWrite), false, p => p.SpecLslWrite, (p, t) => p.SpecLslWrite = t),
        ("Quy cách", "USL ghi xuống PLC", nameof(PlcSettings.SpecUslWrite), false, p => p.SpecUslWrite, (p, t) => p.SpecUslWrite = t),
    ];

    private readonly AppSettings _draft;

    public SettingsViewModel(AppSettings source)
    {
        _draft = SettingsService.Clone(source);

        Tags = new ObservableCollection<TagRow>(TagMap.Select(m => new TagRow(m.Group, m.Name, m.Key, m.Required)));
        LoadTagsFrom(_draft.Plc);

        Specs = new ObservableCollection<SpecDefinition>(new SpecDatabase(SettingsService.ResolvePath(_draft.DatabaseFilePath)).Load());
        SelectedSpec = Specs.FirstOrDefault();

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, null, ex => TestResult = "Lỗi: " + ex.Message);
        UseMcDefaultsCommand = new RelayCommand(() => ApplyDefaults(mc: true));
        UseModbusDefaultsCommand = new RelayCommand(() => ApplyDefaults(mc: false));
        AddSpecCommand = new RelayCommand(() =>
        {
            var spec = new SpecDefinition { SortOrder = Specs.Count + 1, Lsl = 3, Usl = 4, Condition = SpecCatalog.DefaultCondition };
            Specs.Add(spec);
            SelectedSpec = spec;
        });
        RemoveSpecCommand = new RelayCommand(() => { if (SelectedSpec is { } s) Specs.Remove(s); SelectedSpec = Specs.FirstOrDefault(); }, () => SelectedSpec is not null);
        AddModelCommand = new RelayCommand(() => SelectedSpec?.Models.Add(new ModelDefinition { SortOrder = (SelectedSpec?.Models.Count ?? 0) + 1 }), () => SelectedSpec is not null);
        RemoveModelCommand = new RelayCommand(() => { if (SelectedModel is { } m) SelectedSpec?.Models.Remove(m); }, () => SelectedModel is not null);
    }

    public ObservableCollection<TagRow> Tags { get; }
    public ObservableCollection<SpecDefinition> Specs { get; }
    private SpecDefinition? _selectedSpec;
    public SpecDefinition? SelectedSpec { get => _selectedSpec; set { if (SetProperty(ref _selectedSpec, value)) SelectedModel = null; } }
    private ModelDefinition? _selectedModel;
    public ModelDefinition? SelectedModel { get => _selectedModel; set => SetProperty(ref _selectedModel, value); }
    public ICommand TestConnectionCommand { get; }
    public ICommand UseMcDefaultsCommand { get; }
    public ICommand UseModbusDefaultsCommand { get; }
    public ICommand AddSpecCommand { get; }
    public ICommand RemoveSpecCommand { get; }
    public ICommand AddModelCommand { get; }
    public ICommand RemoveModelCommand { get; }
    public string DatabaseFilePath => _draft.DatabaseFilePath;
    public string ResultQrTemplate
    {
        get => _draft.ResultQrTemplate;
        set { _draft.ResultQrTemplate = value; OnPropertyChanged(); OnPropertyChanged(nameof(QrPreview)); }
    }
    public string QrPreview => ResultQrFormatter.Format(ResultQrTemplate, ResultQrFormatter.Sample());
    public string QrFields => string.Join("  ", ResultQrFormatter.Fields.Select(f => "{" + f + "}"));

    // ----- Kết nối -----

    public PlcProtocol Protocol
    {
        get => _draft.Plc.Protocol;
        set
        {
            if (_draft.Plc.Protocol == value) return;
            _draft.Plc.Protocol = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNetworkProtocol));
            OnPropertyChanged(nameof(IsModbus));
        }
    }
    public bool IsNetworkProtocol => Protocol != PlcProtocol.Simulation;
    public bool IsModbus => Protocol == PlcProtocol.ModbusTcp;

    public string IpAddress { get => _draft.Plc.IpAddress; set { _draft.Plc.IpAddress = value; OnPropertyChanged(); } }
    public int Port { get => _draft.Plc.Port; set { _draft.Plc.Port = value; OnPropertyChanged(); } }
    public byte UnitId { get => _draft.Plc.UnitId; set { _draft.Plc.UnitId = value; OnPropertyChanged(); } }
    public int PollIntervalMs { get => _draft.Plc.PollIntervalMs; set { _draft.Plc.PollIntervalMs = value; OnPropertyChanged(); } }
    public int TimeoutMs { get => _draft.Plc.TimeoutMs; set { _draft.Plc.TimeoutMs = value; OnPropertyChanged(); } }
    public bool AutoConnectPlc { get => _draft.AutoConnectPlc; set { _draft.AutoConnectPlc = value; OnPropertyChanged(); } }

    private string _testResult = "";
    public string TestResult { get => _testResult; private set => SetProperty(ref _testResult, value); }

    // ----- Khác -----

    public string Title { get => _draft.Title; set { _draft.Title = value; OnPropertyChanged(); } }
    public string CompanyLabel { get => _draft.CompanyLabel; set { _draft.CompanyLabel = value; OnPropertyChanged(); } }
    public string Inspector { get => _draft.Inspector; set { _draft.Inspector = value; OnPropertyChanged(); } }
    public string ForceUnit { get => _draft.ForceUnit; set { _draft.ForceUnit = value; OnPropertyChanged(); } }
    public string ResultFilePath { get => _draft.ResultFilePath; set { _draft.ResultFilePath = value; OnPropertyChanged(); } }
    public int HistogramBins { get => _draft.HistogramBins; set { _draft.HistogramBins = value; OnPropertyChanged(); } }

    // ----- Kiểm tra & kết xuất -----

    public SettingsSection ValidationSection { get; private set; }

    /// <summary>Trả về thông báo lỗi đầu tiên, hoặc null nếu hợp lệ.</summary>
    public string? Validate()
    {
        ValidationSection = SettingsSection.Connection;
        if (IsNetworkProtocol)
        {
            if (!IPAddress.TryParse(IpAddress.Trim(), out _) && Uri.CheckHostName(IpAddress.Trim()) == UriHostNameType.Unknown)
                return "Địa chỉ IP/hostname của PLC không hợp lệ.";
            if (Port is < 1 or > 65535) return "Port phải trong 1..65535.";
        }
        if (PollIntervalMs < 50) return "Chu kỳ đọc tối thiểu 50 ms.";
        if (TimeoutMs < 200) return "Timeout tối thiểu 200 ms.";

        ValidationSection = SettingsSection.Tags;
        var acknowledge = Tags.First(t => t.Key == nameof(PlcSettings.StartCommand));
        if (acknowledge.DataType != TagDataType.Bit) return "Lệnh START phải dùng kiểu Bit.";
        string NormalizeAddress(string address) => address.Replace(" ", "").ToUpperInvariant();
        if (Tags.Any(t => t.Key != acknowledge.Key && !string.IsNullOrWhiteSpace(t.Address) && NormalizeAddress(t.Address) == NormalizeAddress(acknowledge.Address)))
            return "Bit START phải khác các tag còn lại.";
        if (Specs.SelectMany(s => s.Models).Any(m => m.HasPlcBit &&
            NormalizeAddress(m.PlcBit) == NormalizeAddress(acknowledge.Address)))
            return "Bit model không được trùng bit START.";
        foreach (var row in Tags)
        {
            bool hasAddress = !string.IsNullOrWhiteSpace(row.Address);
            if (row.Required && !hasAddress) return $"Địa chỉ '{row.Name}' là bắt buộc, không được để trống.";
            if (!hasAddress) continue;

            if (row.DataType == TagDataType.String)
                return $"'{row.Name}': không dùng kiểu String cho tag này.";
            if (row.Group is "Giá trị đo" or "Quy cách" or "Bộ đếm" && row.DataType == TagDataType.Bit)
                return $"'{row.Name}' phải là kiểu số (Int16/Int32/Float32...).";

            if (IsNetworkProtocol)
            {
                var err = ValidateAddress(row.Address, mustBeBit: row.Group == "START");
                if (err is not null) return $"Địa chỉ '{row.Name}' = '{row.Address}': {err}";
            }
        }

        ValidationSection = SettingsSection.Catalog;
        var catalogError = SpecDatabase.Validate(Specs.ToList());
        if (catalogError is not null) return catalogError;
        foreach (var model in Specs.SelectMany(s => s.Models).Where(m => m.HasPlcBit))
        {
            if (!IsNetworkProtocol) continue;
            var err = ValidateAddress(model.PlcBit, mustBeBit: true);
            if (err is not null) return $"Bit của model '{model.Name}': {err}";
        }
        ValidationSection = SettingsSection.Qr;
        if (ResultQrTemplate.Length > 512) return "Mẫu QR tối đa 512 ký tự.";
        var unknown = System.Text.RegularExpressions.Regex.Matches(ResultQrTemplate, @"\{(\w+)\}")
            .Select(m => m.Groups[1].Value).FirstOrDefault(f => !ResultQrFormatter.Fields.Contains(f, StringComparer.OrdinalIgnoreCase));
        if (unknown is not null) return $"Trường QR không hợp lệ: {{{unknown}}}.";

        ValidationSection = SettingsSection.General;
        if (HistogramBins is < 4 or > 100) return "Số cột histogram phải trong 4..100.";
        if (string.IsNullOrWhiteSpace(ResultFilePath)) return "Chưa đặt đường dẫn file kết quả.";
        return null;
    }

    private string? ValidateAddress(string address, bool mustBeBit)
    {
        try
        {
            if (Protocol == PlcProtocol.McProtocol3E)
            {
                var dev = McDevice.Parse(address);
                if (mustBeBit && !dev.IsBit) return "phải là thiết bị bit (M, L, B, Y...)";
            }
            else if (Protocol == PlcProtocol.ModbusTcp)
            {
                var s = address.Trim().ToUpperInvariant();
                bool ok = s.StartsWith("HR") || s.StartsWith("IR") || s.StartsWith("DI") || s.StartsWith("C") || s.All(char.IsDigit);
                if (!ok) return "dùng HRxxx, IRxxx, Cxxx, DIxxx hoặc địa chỉ số 4xxxx/3xxxx/0xxxx/1xxxx";
                if (mustBeBit && !(s.StartsWith("C") || (s.All(char.IsDigit) && s.StartsWith('0'))))
                    return "phải là coil (Cxxx hoặc 0xxxx)";
            }
            return null;
        }
        catch (PlcException ex)
        {
            return ex.Message;
        }
    }

    public AppSettings Build()
    {
        ApplyTagsTo(_draft.Plc);
        _draft.Plc.IpAddress = _draft.Plc.IpAddress.Trim();
        new SpecDatabase(SettingsService.ResolvePath(_draft.DatabaseFilePath)).Save(Specs.ToList());
        _draft.ResultFilePath = _draft.ResultFilePath.Trim();
        return _draft;
    }

    private async Task TestConnectionAsync()
    {
        var plc = SettingsService.Clone(_draft).Plc;
        ApplyTagsTo(plc);

        TestResult = "Đang kết nối...";
        await using var client = PlcClientFactory.Create(plc, Specs.ToList());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(plc.TimeoutMs + 1500));
        await client.ConnectAsync(cts.Token);

        var tag = plc.LoadcellValue;
        if (tag.DataType == TagDataType.Bit)
        {
            var bits = await client.ReadBitsAsync(tag.Address, 1, cts.Token);
            TestResult = $"Kết nối OK. {tag.Address} = {(bits[0] ? "ON" : "OFF")}";
        }
        else
        {
            var words = await client.ReadWordsAsync(tag.Address, tag.WordCount, cts.Token);
            var raw = string.Join(" ", words.Select(w => w.ToString("X4")));
            TestResult = $"Kết nối OK. {tag.Address} = {TagCodec.Decode(tag, words).ToString("0.###", CultureInfo.InvariantCulture)}  (raw: {raw})";
        }
    }

    private void ApplyDefaults(bool mc)
    {
        if (mc) _draft.Plc.ApplyMcDefaults();
        else _draft.Plc.ApplyModbusDefaults();
        LoadTagsFrom(_draft.Plc);
        OnPropertyChanged(nameof(Port));

        // Bit chủng loại: M40.. (MC) ↔ C40.. (Modbus coil)
        foreach (var model in Specs.SelectMany(s => s.Models))
        {
            var bit = (model.PlcBit ?? "").Trim().ToUpperInvariant();
            if (bit.Length < 2) continue;
            if (mc && bit.StartsWith('C') && bit[1..].All(char.IsDigit)) model.PlcBit = "M" + bit[1..];
            else if (!mc && bit.StartsWith('M') && bit[1..].All(char.IsDigit)) model.PlcBit = "C" + bit[1..];
        }
        foreach (var spec in Specs)
        {
            var models = spec.Models.ToList();
            spec.Models.Clear();
            foreach (var model in models) spec.Models.Add(model);
        }
    }

    private void LoadTagsFrom(PlcSettings plc)
    {
        foreach (var row in Tags)
        {
            var entry = TagMap.First(m => m.Key == row.Key);
            row.LoadFrom(entry.Get(plc));
        }
    }

    private void ApplyTagsTo(PlcSettings plc)
    {
        foreach (var row in Tags)
        {
            var entry = TagMap.First(m => m.Key == row.Key);
            entry.Set(plc, row.ToDefinition());
        }
    }
}
