using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>
/// PLC mô phỏng "làm hết mọi việc" như PLC thật của BISG: sau START, mỗi chu kỳ PLC
/// tự "scan" một serial (ghi serial, đơn hàng, line, chủng loại, quy cách vào thanh ghi),
/// đo trong 1.5 giây, ghi kết quả, phân định OK/NG, tăng bộ đếm rồi bật bit Đo xong.
/// Chu kỳ tiếp theo bắt đầu sau 6 giây. Phần mềm chỉ đọc và hiển thị.
/// </summary>
public sealed class SimulationPlcClient : IPlcClient
{
    private sealed record Product(string Model, string OrderNo, string Line, string SerialPrefix, double Lsl, double Usl);

    private static readonly Product[] Products =
    [
        new("CPX",     "BB09320001", "WAA2", "985X57200-", 3.0, 4.0),
        new("NF 8.3",  "BB09319998", "WAA1", "CP201111-",  3.0, 5.0),
        new("GSM RUP", "BB09319996", "WAA3", "GS0501-",    3.0, 4.0),
        new("M1-M2",   "BB09319997", "WAA4", "WA01-",      3.0, 5.0),
        new("PP1",     "BB09319995", "WAA5", "PP0101-",    3.0, 5.0),
    ];

    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(0.4);      // scan xong → bắt đầu đo
    private static readonly TimeSpan MeasureDuration = TimeSpan.FromSeconds(1.5); // thời gian đo
    private static readonly TimeSpan CycleGap = TimeSpan.FromSeconds(6);          // đo xong → sản phẩm kế tiếp

    private enum Phase { Idle, Scanned, Measuring, Done }

    private readonly PlcSettings _s;
    private readonly Dictionary<string, ushort> _words = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _bits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new();
    private readonly object _gate = new();

    private bool _running;
    private Phase _phase = Phase.Idle;
    /// <summary>Thời điểm bắt đầu chu kỳ (scan) hoặc thời điểm Đo xong – tùy pha.</summary>
    private DateTime _phaseAt;
    private int _productIndex;
    private int _serialCounter = 100;
    private int _total, _ok, _ng;
    private Product _current = Products[0];
    private double _target = 3.70;
    private double _distance = 3.70;
    private double _force = 101.0;

    public SimulationPlcClient(PlcSettings settings) => _s = settings;

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        lock (_gate)
        {
            SetBit(_s.LoadcellStable.Address, true);
            PublishStatic();
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default)
    {
        EnsureConnected();
        Tick();
        lock (_gate)
        {
            var (prefix, number) = Split(address);
            var r = new ushort[count];
            for (int i = 0; i < count; i++)
                r[i] = _words.GetValueOrDefault(Key(prefix, number + i));
            return Task.FromResult(r);
        }
    }

    public Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default)
    {
        EnsureConnected();
        lock (_gate)
        {
            var (prefix, number) = Split(address);
            for (int i = 0; i < values.Length; i++)
                _words[Key(prefix, number + i)] = values[i];
        }
        return Task.CompletedTask;
    }

    public Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default)
    {
        EnsureConnected();
        Tick();
        lock (_gate)
        {
            var (prefix, number) = Split(address);
            var r = new bool[count];
            for (int i = 0; i < count; i++)
                r[i] = _bits.GetValueOrDefault(Key(prefix, number + i));
            return Task.FromResult(r);
        }
    }

    public Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
    {
        EnsureConnected();
        lock (_gate)
        {
            SetBit(address, value);
            if (!value) return Task.CompletedTask;

            if (Same(address, _s.StartCommand.Address))
            {
                _running = true;
                BeginCycle();
                SetBit(address, false);          // PLC tự xóa bit lệnh
            }
            else if (Same(address, _s.StopCommand.Address))
            {
                _running = false;
                _phase = Phase.Idle;
                SetBit(_s.MeasureDone.Address, false);
                SetBit(address, false);
            }
            else if (Same(address, _s.ResetCommand.Address))
            {
                _running = false;
                _phase = Phase.Idle;
                _total = _ok = _ng = 0;
                SetBit(_s.MeasureDone.Address, false);
                SetBit(_s.JudgeOk.Address, false);
                SetBit(_s.JudgeNg.Address, false);
                StoreString(_s.SerialText, "");
                StoreString(_s.OrderNoText, "");
                StoreString(_s.LineText, "");
                StoreString(_s.ModelText, "");
                StoreNumber(_s.ResultValue, 0);
                StoreNumber(_s.SpecLsl, 0);
                StoreNumber(_s.SpecUsl, 0);
                PublishStatic();
                SetBit(address, false);
            }
        }
        return Task.CompletedTask;
    }

    // ----- nội bộ -----

    private void EnsureConnected()
    {
        if (!IsConnected) throw new PlcException("PLC mô phỏng chưa kết nối");
    }

    /// <summary>Bắt đầu một sản phẩm: PLC "nhận serial từ máy quét" và điền thông tin.</summary>
    private void BeginCycle()
    {
        _current = Products[_productIndex % Products.Length];
        _productIndex++;
        _serialCounter++;

        StoreString(_s.SerialText, $"{_current.SerialPrefix}{_serialCounter:00000}");
        StoreString(_s.OrderNoText, _current.OrderNo);
        StoreString(_s.LineText, _current.Line);
        StoreString(_s.ModelText, _current.Model);
        StoreNumber(_s.SpecLsl, _current.Lsl);
        StoreNumber(_s.SpecUsl, _current.Usl);
        SetBit(_s.MeasureDone.Address, false);
        SetBit(_s.JudgeOk.Address, false);
        SetBit(_s.JudgeNg.Address, false);

        // ≈85% trong quy cách, còn lại lệch ra ngoài
        bool inSpec = _rng.NextDouble() < 0.85;
        double span = _current.Usl - _current.Lsl;
        _target = inSpec
            ? _current.Lsl + span * (0.2 + _rng.NextDouble() * 0.6)
            : (_rng.NextDouble() < 0.5 ? _current.Lsl - 0.1 - _rng.NextDouble() * 0.4 : _current.Usl + 0.1 + _rng.NextDouble() * 0.6);

        _phase = Phase.Scanned;
        _phaseAt = DateTime.UtcNow;
    }

    /// <summary>Cập nhật thanh ghi mô phỏng theo thời gian thực (gọi ở mỗi lần đọc).</summary>
    private void Tick()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _force = 101.0 + (_rng.NextDouble() - 0.5) * 0.6;

            // Pha được tính theo thời gian trôi qua kể từ đầu chu kỳ, không phụ thuộc tần suất đọc.
            switch (_phase)
            {
                case Phase.Scanned or Phase.Measuring:
                {
                    var sinceScan = now - _phaseAt;
                    if (sinceScan >= ScanDelay + MeasureDuration)
                    {
                        _distance = Math.Round(_target, 2);
                        bool ok = _distance >= _current.Lsl && _distance <= _current.Usl;
                        _total++;
                        if (ok) _ok++; else _ng++;
                        StoreNumber(_s.ResultValue, _distance);
                        SetBit(_s.JudgeOk.Address, ok);
                        SetBit(_s.JudgeNg.Address, !ok);
                        PublishStatic();
                        SetBit(_s.MeasureDone.Address, true);   // bật cuối cùng, sau khi mọi dữ liệu đã sẵn
                        _phase = Phase.Done;
                        _phaseAt = now;
                    }
                    else if (sinceScan >= ScanDelay)
                    {
                        _phase = Phase.Measuring;
                        double remain = (ScanDelay + MeasureDuration - sinceScan).TotalSeconds;
                        _distance = _target + (_rng.NextDouble() - 0.5) * 0.4 * remain;
                    }
                    break;
                }

                case Phase.Done when _running && now - _phaseAt >= CycleGap:
                    BeginCycle();
                    break;

                case Phase.Idle:
                    _distance = 3.70 + (_rng.NextDouble() - 0.5) * 0.06;
                    break;
            }

            SetBit(_s.RunningState.Address, _running);
            StoreNumber(_s.LoadcellValue, _force);
            StoreNumber(_s.DistanceValue, _distance);
        }
    }

    private void PublishStatic()
    {
        StoreNumber(_s.TotalCount, _total);
        StoreNumber(_s.OkCount, _ok);
        StoreNumber(_s.NgCount, _ng);
    }

    private void StoreNumber(TagDefinition tag, double value)
    {
        if (!tag.IsConfigured) return;
        if (tag.DataType == TagDataType.Bit) { SetBit(tag.Address, value != 0); return; }
        if (tag.DataType == TagDataType.String) return;
        WriteWords(tag.Address, TagCodec.Encode(tag, value));
    }

    private void StoreString(TagDefinition tag, string text)
    {
        if (!tag.IsConfigured || tag.DataType != TagDataType.String) return;
        WriteWords(tag.Address, TagCodec.EncodeString(tag, text));
    }

    private void WriteWords(string address, ushort[] words)
    {
        var (prefix, number) = Split(address);
        for (int i = 0; i < words.Length; i++)
            _words[Key(prefix, number + i)] = words[i];
    }

    private void SetBit(string address, bool value)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        var (prefix, number) = Split(address);
        _bits[Key(prefix, number)] = value;
    }

    private static bool Same(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var (pa, na) = Split(a);
        var (pb, nb) = Split(b);
        return pa == pb && na == nb;
    }

    /// <summary>Tách "D100" → ("D", 100). Phần số được hiểu thập phân (đủ dùng cho mô phỏng).</summary>
    private static (string Prefix, long Number) Split(string address)
    {
        var s = address.Trim().ToUpperInvariant().Replace(" ", "");
        int i = 0;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        long.TryParse(s[i..], out var n);
        return (s[..i], n);
    }

    private static string Key(string prefix, long number) => prefix + number;
}
