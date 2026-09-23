using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.Services.Plc;

namespace HeThongDoKhoangCach.Services;

/// <summary>
/// Ảnh chụp toàn bộ tag đọc được trong một chu kỳ poll.
/// Trường nullable = tag không được cấu hình (PLC không cung cấp) → phần mềm tự bù.
/// </summary>
public sealed class PlcSnapshot
{
    public double Force { get; init; }
    public double Distance { get; init; }
    public double? ResultValue { get; init; }
    public double? Lsl { get; init; }
    public double? Usl { get; init; }
    public int? Total { get; init; }
    public int? Ok { get; init; }
    public int? Ng { get; init; }
    public string? Serial { get; init; }
    public string? OrderNo { get; init; }
    public string? Line { get; init; }
    public string? Model { get; init; }
    public bool LoadcellStable { get; init; }
    public bool MeasureDone { get; init; }
    public bool? JudgeOk { get; init; }
    public bool? JudgeNg { get; init; }
    public bool? Running { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Vòng lặp nền: kết nối PLC, đọc các tag theo chu kỳ, tự kết nối lại khi lỗi,
/// và cung cấp hàm ghi lệnh (START/STOP/RESET). Các sự kiện được phát trên thread nền.
/// </summary>
public sealed class PlcMonitor : IAsyncDisposable
{
    private const int ReconnectDelayMs = 2000;

    private PlcSettings _settings = new();
    private IPlcClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _connected;

    public event Action<PlcSnapshot>? SnapshotReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? ErrorOccurred;

    public bool IsConnected => _connected;

    public async Task StartAsync(PlcSettings settings)
    {
        await StopAsync();
        _settings = settings;
        _client = PlcClientFactory.Create(settings);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token), token);
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        var client = _client;
        _cts = null;
        _loop = null;
        _client = null;

        if (cts is not null)
        {
            cts.Cancel();
            if (loop is not null)
            {
                try { await loop.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { /* huỷ hoặc timeout */ }
            }
            cts.Dispose();
        }
        if (client is not null)
        {
            try { await client.DisposeAsync(); } catch { /* bỏ qua */ }
        }
        SetConnected(false);
    }

    /// <summary>Ghi lệnh (bit hoặc word 1/0) tới PLC.</summary>
    public async Task WriteCommandAsync(TagDefinition tag, bool value, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected)
            throw new PlcException("PLC chưa kết nối, không gửi được lệnh");
        if (!tag.IsConfigured)
            throw new PlcException("Địa chỉ lệnh chưa được cấu hình");

        if (tag.DataType == TagDataType.Bit)
            await client.WriteBitAsync(tag.Address, value, ct);
        else
            await client.WriteWordsAsync(tag.Address, TagCodec.Encode(tag, value ? 1 : 0), ct);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var client = _client!;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(ct);
                    SetConnected(true);
                }

                var snapshot = await ReadSnapshotAsync(client, ct);
                SnapshotReceived?.Invoke(snapshot);
                await Task.Delay(Math.Max(50, _settings.PollIntervalMs), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetConnected(false);
                ErrorOccurred?.Invoke(ex.Message);
                try { await client.DisconnectAsync(); } catch { /* bỏ qua */ }
                try { await Task.Delay(ReconnectDelayMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<PlcSnapshot> ReadSnapshotAsync(IPlcClient client, CancellationToken ct)
    {
        var s = _settings;
        return new PlcSnapshot
        {
            Force = await ReadNumberAsync(client, s.LoadcellValue, ct) ?? 0,
            Distance = await ReadNumberAsync(client, s.DistanceValue, ct) ?? 0,
            ResultValue = await ReadNumberAsync(client, s.ResultValue, ct),
            Lsl = await ReadNumberAsync(client, s.SpecLsl, ct),
            Usl = await ReadNumberAsync(client, s.SpecUsl, ct),
            Total = ToInt(await ReadNumberAsync(client, s.TotalCount, ct)),
            Ok = ToInt(await ReadNumberAsync(client, s.OkCount, ct)),
            Ng = ToInt(await ReadNumberAsync(client, s.NgCount, ct)),
            Serial = await ReadStringAsync(client, s.SerialText, ct),
            OrderNo = await ReadStringAsync(client, s.OrderNoText, ct),
            Line = await ReadStringAsync(client, s.LineText, ct),
            Model = await ReadStringAsync(client, s.ModelText, ct),
            LoadcellStable = await ReadFlagAsync(client, s.LoadcellStable, ct) ?? true,
            MeasureDone = await ReadFlagAsync(client, s.MeasureDone, ct) ?? false,
            JudgeOk = await ReadFlagAsync(client, s.JudgeOk, ct),
            JudgeNg = await ReadFlagAsync(client, s.JudgeNg, ct),
            Running = await ReadFlagAsync(client, s.RunningState, ct),
            Timestamp = DateTime.Now,
        };
    }

    private static int? ToInt(double? v) => v is { } d ? (int)Math.Round(d) : null;

    private static async Task<double?> ReadNumberAsync(IPlcClient client, TagDefinition tag, CancellationToken ct)
    {
        if (!tag.IsConfigured) return null;
        if (tag.DataType == TagDataType.Bit)
        {
            var bits = await client.ReadBitsAsync(tag.Address, 1, ct);
            return bits[0] ? 1 : 0;
        }
        if (tag.DataType == TagDataType.String)
            throw new PlcException($"Tag {tag.Address} được cấu hình là chuỗi nhưng đang dùng cho giá trị số");
        var words = await client.ReadWordsAsync(tag.Address, tag.WordCount, ct);
        return TagCodec.Decode(tag, words);
    }

    private static async Task<string?> ReadStringAsync(IPlcClient client, TagDefinition tag, CancellationToken ct)
    {
        if (!tag.IsConfigured) return null;
        if (tag.DataType != TagDataType.String)
        {
            // Cho phép cấu hình số cho các ô chuỗi (vd mã line dạng số)
            var n = await ReadNumberAsync(client, tag, ct);
            return n is { } d ? d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        var words = await client.ReadWordsAsync(tag.Address, tag.WordCount, ct);
        return TagCodec.DecodeString(tag, words);
    }

    private static async Task<bool?> ReadFlagAsync(IPlcClient client, TagDefinition tag, CancellationToken ct)
    {
        if (!tag.IsConfigured) return null;
        if (tag.DataType == TagDataType.Bit)
        {
            var bits = await client.ReadBitsAsync(tag.Address, 1, ct);
            return bits[0];
        }
        if (tag.DataType == TagDataType.String)
            throw new PlcException($"Tag {tag.Address} được cấu hình là chuỗi nhưng đang dùng cho bit");
        var words = await client.ReadWordsAsync(tag.Address, tag.WordCount, ct);
        return TagCodec.Decode(tag, words) != 0;
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        ConnectionChanged?.Invoke(value);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
