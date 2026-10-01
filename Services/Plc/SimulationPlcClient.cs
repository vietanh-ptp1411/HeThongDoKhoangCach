using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>
/// PLC mô phỏng theo luồng vận hành của khách hàng:
/// phần mềm bật bit START (máy chạy) / STOP (tạm dừng) / RESET (xóa giá trị) và bật bit chủng loại (M40..M50) khi chọn model;
/// mỗi START đo một lần trong 1,5 giây, ghi kết quả đo, phân định OK/NG,
/// tăng bộ đếm rồi bật bit Đo xong (giữ 0,6 giây), dừng và chờ START tiếp theo.
/// Lực căng và khoảng cách được cập nhật liên tục ở mỗi lần đọc.
/// </summary>
public sealed class SimulationPlcClient : IPlcClient
{
    private static readonly TimeSpan MeasureDuration = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan DonePulse = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan CycleGap = TimeSpan.FromSeconds(3);

    private enum Phase { Idle, Measuring, Done }

    private readonly PlcSettings _s;
    private readonly IReadOnlyList<SpecDefinition> _specs;
    private readonly Dictionary<string, ushort> _words = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _bits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new();
    private readonly object _gate = new();

    private bool _running;
    private Phase _phase = Phase.Idle;
    private DateTime _phaseAt;
    private int _total, _ok, _ng;
    private double _lsl = 3.0, _usl = 4.0;
    private double _target = 3.70;
    private double _distance = 3.70;
    private double _force = 2.0;

    public SimulationPlcClient(PlcSettings settings, IReadOnlyList<SpecDefinition> specs)
    {
        _s = settings;
        _specs = specs;
    }

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        lock (_gate)
        {
            SetBit(_s.LoadcellStable.Address, true);
            PublishCounters();
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
                _phase = Phase.Idle;
                _running = false;
                _total = _ok = _ng = 0;
                SetBit(_s.MeasureDone.Address, false);
                SetBit(_s.JudgeOk.Address, false);
                SetBit(_s.JudgeNg.Address, false);
                StoreNumber(_s.ResultValue, 0);
                PublishCounters();
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

    /// <summary>Quy cách hiện hành: theo LSL/USL phần mềm ghi xuống (nếu có), không thì theo bit chủng loại đang bật.</summary>
    private bool TryResolveSpec()
    {
        if (_s.SpecLslWrite.IsConfigured && _s.SpecUslWrite.IsConfigured)
        {
            double lsl = ReadNumber(_s.SpecLslWrite), usl = ReadNumber(_s.SpecUslWrite);
            if (usl > lsl) { _lsl = lsl; _usl = usl; return true; }
        }
        foreach (var spec in _specs)
        {
            if (spec.Models.Any(m => m.HasPlcBit && IsModelBitSet(m.PlcBit)) && spec.Usl > spec.Lsl)
            {
                _lsl = spec.Lsl;
                _usl = spec.Usl;
                return true;
            }
        }
        return false;
    }

    private bool IsModelBitSet(string bit)
    {
        var (prefix, number) = Split(bit);
        return _bits.GetValueOrDefault(Key(prefix, number));
    }

    private void BeginMeasure(DateTime now)
    {
        // ≈85% trong quy cách, còn lại lệch ra ngoài
        bool inSpec = _rng.NextDouble() < 0.85;
        double span = _usl - _lsl;
        _target = inSpec
            ? _lsl + span * (0.2 + _rng.NextDouble() * 0.6)
            : (_rng.NextDouble() < 0.5 ? _lsl - 0.1 - _rng.NextDouble() * 0.4 : _usl + 0.1 + _rng.NextDouble() * 0.6);
        SetBit(_s.MeasureDone.Address, false);
        _phase = Phase.Measuring;
        _phaseAt = now;
    }

    /// <summary>Cập nhật thanh ghi mô phỏng theo thời gian thực (gọi ở mỗi lần đọc).</summary>
    private void Tick()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _force = 2.0 + (_rng.NextDouble() - 0.5) * 0.6;

            switch (_phase)
            {
                case Phase.Idle:
                    _distance = 3.70 + (_rng.NextDouble() - 0.5) * 0.06;
                    if (_running && TryResolveSpec()) BeginMeasure(now);
                    break;

                case Phase.Measuring:
                {
                    if (!_running || !TryResolveSpec())
                    {
                        _phase = Phase.Idle;
                        break;
                    }
                    var elapsed = now - _phaseAt;
                    if (elapsed >= MeasureDuration)
                    {
                        _distance = Math.Round(_target, 2);
                        bool ok = _distance >= _lsl && _distance <= _usl;
                        _total++;
                        if (ok) _ok++; else _ng++;
                        StoreNumber(_s.ResultValue, _distance);
                        SetBit(_s.JudgeOk.Address, ok);
                        SetBit(_s.JudgeNg.Address, !ok);
                        PublishCounters();
                        SetBit(_s.MeasureDone.Address, true);   // bật cuối cùng, sau khi mọi dữ liệu đã sẵn
                        _running = false;
                        _phase = Phase.Done;
                        _phaseAt = now;
                    }
                    else
                    {
                        double remain = (MeasureDuration - elapsed).TotalSeconds;
                        _distance = _target + (_rng.NextDouble() - 0.5) * 0.4 * remain;
                    }
                    break;
                }

                case Phase.Done:
                {
                    var elapsed = now - _phaseAt;
                    if (elapsed >= DonePulse) SetBit(_s.MeasureDone.Address, false);
                    if (elapsed >= CycleGap) _phase = Phase.Idle;
                    break;
                }
            }

            SetBit(_s.RunningState.Address, _running);
            StoreNumber(_s.LoadcellValue, _force);
            StoreNumber(_s.DistanceValue, _distance);
        }
    }

    private void PublishCounters()
    {
        StoreNumber(_s.TotalCount, _total);
        StoreNumber(_s.OkCount, _ok);
        StoreNumber(_s.NgCount, _ng);
    }

    private double ReadNumber(TagDefinition tag)
    {
        if (!tag.IsConfigured || tag.DataType is TagDataType.Bit or TagDataType.String) return 0;
        var (prefix, number) = Split(tag.Address);
        var words = new ushort[tag.WordCount];
        for (int i = 0; i < words.Length; i++)
            words[i] = _words.GetValueOrDefault(Key(prefix, number + i));
        return TagCodec.Decode(tag, words);
    }

    private void StoreNumber(TagDefinition tag, double value)
    {
        if (!tag.IsConfigured) return;
        if (tag.DataType == TagDataType.Bit) { SetBit(tag.Address, value != 0); return; }
        if (tag.DataType == TagDataType.String) return;
        var (prefix, number) = Split(tag.Address);
        var words = TagCodec.Encode(tag, value);
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
