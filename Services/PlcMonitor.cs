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
    public int? Total { get; init; }
    public int? Ok { get; init; }
    public int? Ng { get; init; }
    public bool LoadcellStable { get; init; }
    public bool? MeasureDone { get; init; }
    public bool? JudgeOk { get; init; }
    public bool? JudgeNg { get; init; }
    public bool? Running { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Vòng lặp nền: kết nối PLC, đọc các tag theo chu kỳ (PLC gửi liên tục qua thanh ghi D), tự kết nối lại khi lỗi,
/// và cung cấp hàm ghi bit lệnh (START/STOP/RESET), bit chủng loại và quy cách. Các sự kiện được phát trên thread nền.
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

    /// <summary>Đang chạy vòng đọc (đã bấm KẾT NỐI PLC), dù có thể tạm thời mất kết nối và đang thử lại.</summary>
    public bool IsActive => _cts is not null;

    public async Task StartAsync(PlcSettings settings, IReadOnlyList<SpecDefinition> specs)
    {
        await StopAsync();
        _settings = settings;
        _client = PlcClientFactory.Create(settings, specs);
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
        var client = RequireClient();
        if (!tag.IsConfigured)
            throw new PlcException("Địa chỉ lệnh chưa được cấu hình");

        if (tag.DataType == TagDataType.Bit)
            await client.WriteBitAsync(tag.Address, value, ct);
        else
            await client.WriteWordsAsync(tag.Address, TagCodec.Encode(tag, value ? 1 : 0), ct);
    }

    /// <summary>Ghi một bit theo địa chỉ (bit chủng loại M40..M50).</summary>
    public Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
    {
        var client = RequireClient();
        if (string.IsNullOrWhiteSpace(address))
            throw new PlcException("Địa chỉ bit đang để trống");
        return client.WriteBitAsync(address.Trim(), value, ct);
    }

    /// <summary>Ghi một giá trị số theo định nghĩa tag (quy cách LSL/USL xuống PLC).</summary>
    public async Task WriteNumberAsync(TagDefinition tag, double value, CancellationToken ct = default)
    {
        var client = RequireClient();
        if (!tag.IsConfigured) return;
        if (tag.DataType == TagDataType.Bit)
        {
            await client.WriteBitAsync(tag.Address, value != 0, ct);
            return;
        }
        if (tag.DataType == TagDataType.String)
            throw new PlcException($"Tag {tag.Address} là chuỗi, không ghi giá trị số được");
        await client.WriteWordsAsync(tag.Address, TagCodec.Encode(tag, value), ct);
    }

    private IPlcClient RequireClient()
    {
        var client = _client;
        if (client is null || !client.IsConnected)
            throw new PlcException("PLC chưa kết nối, không gửi được lệnh");
        return client;
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
        // Đọc tín hiệu hoàn tất trước dữ liệu: nếu PLC chốt giữa chu kỳ poll,
        // chu kỳ kế tiếp sẽ nhận sườn lên cùng với giá trị đã được chốt.
        var done = await ReadFlagAsync(client, s.MeasureDone, ct);
        var total = ToInt(await ReadNumberAsync(client, s.TotalCount, ct));
        return new PlcSnapshot
        {
            Force = await ReadNumberAsync(client, s.LoadcellValue, ct) ?? 0,
            Distance = await ReadNumberAsync(client, s.DistanceValue, ct) ?? 0,
            ResultValue = await ReadNumberAsync(client, s.ResultValue, ct),
            Total = total,
            Ok = ToInt(await ReadNumberAsync(client, s.OkCount, ct)),
            Ng = ToInt(await ReadNumberAsync(client, s.NgCount, ct)),
            LoadcellStable = await ReadFlagAsync(client, s.LoadcellStable, ct) ?? true,
            MeasureDone = done,
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
