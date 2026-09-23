using System.Globalization;
using System.Net.Sockets;

namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>
/// Client Modbus TCP. Địa chỉ chấp nhận:
/// "HR100" (holding register, FC03/FC06/FC16), "IR100" (input register, FC04),
/// "C100" (coil, FC01/FC05), "DI100" (discrete input, FC02),
/// hoặc kiểu 5/6 chữ số: 40101 → holding 100, 30101 → input 100, 00101 → coil 100, 10101 → DI 100.
/// Số thuần dưới 5 chữ số được hiểu là holding register (offset 0-based).
/// </summary>
public sealed class ModbusTcpClient : TcpPlcClientBase
{
    private enum Area { Coil, DiscreteInput, InputRegister, HoldingRegister }

    private readonly byte _unitId;
    private ushort _transactionId;

    public ModbusTcpClient(string host, int port, byte unitId, int timeoutMs) : base(host, port, timeoutMs)
    {
        _unitId = unitId;
    }

    private static (Area Area, int Offset) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new PlcException("Địa chỉ Modbus đang để trống");

        var s = address.Trim().ToUpperInvariant().Replace(" ", "");

        (Area, string)? prefixed = s switch
        {
            _ when s.StartsWith("HR") => (Area.HoldingRegister, s[2..]),
            _ when s.StartsWith("IR") => (Area.InputRegister, s[2..]),
            _ when s.StartsWith("DI") => (Area.DiscreteInput, s[2..]),
            _ when s.StartsWith("C") => (Area.Coil, s[1..]),
            _ => null,
        };

        if (prefixed is { } p)
        {
            if (int.TryParse(p.Item2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var off) && off is >= 0 and <= 0xFFFF)
                return (p.Item1, off);
            throw new PlcException($"Offset Modbus không hợp lệ: '{address}'");
        }

        if (!s.All(char.IsDigit))
            throw new PlcException($"Địa chỉ Modbus không hợp lệ: '{address}'");

        if (s.Length >= 5)
        {
            var area = s[0] switch
            {
                '0' => Area.Coil,
                '1' => Area.DiscreteInput,
                '3' => Area.InputRegister,
                '4' => Area.HoldingRegister,
                _ => throw new PlcException($"Địa chỉ Modbus '{address}' phải bắt đầu bằng 0/1/3/4"),
            };
            int n = int.Parse(s[1..], CultureInfo.InvariantCulture) - 1;
            if (n is < 0 or > 0xFFFF) throw new PlcException($"Địa chỉ Modbus ngoài phạm vi: '{address}'");
            return (area, n);
        }

        return (Area.HoldingRegister, int.Parse(s, CultureInfo.InvariantCulture));
    }

    public override async Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default)
    {
        if (count is < 1 or > 125) throw new PlcException("Số register đọc phải trong 1..125");
        var (area, offset) = ParseAddress(address);
        byte fc = area switch
        {
            Area.HoldingRegister => 0x03,
            Area.InputRegister => 0x04,
            _ => throw new PlcException($"'{address}' không phải register; dùng HRxxx hoặc IRxxx"),
        };

        var pdu = new byte[] { fc, (byte)(offset >> 8), (byte)offset, (byte)(count >> 8), (byte)count };
        var resp = await TransactAsync(pdu, ct);
        if (resp.Length < 2 || resp[1] < count * 2 || resp.Length < 2 + count * 2)
            throw new PlcException("Phản hồi Modbus thiếu dữ liệu");

        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = (ushort)(resp[2 + 2 * i] << 8 | resp[3 + 2 * i]);
        return result;
    }

    public override async Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default)
    {
        if (values.Length is < 1 or > 123) throw new PlcException("Số register ghi phải trong 1..123");
        var (area, offset) = ParseAddress(address);
        if (area != Area.HoldingRegister)
            throw new PlcException($"Chỉ ghi được holding register (HRxxx), địa chỉ '{address}' không hợp lệ");

        byte[] pdu;
        if (values.Length == 1)
        {
            pdu = [0x06, (byte)(offset >> 8), (byte)offset, (byte)(values[0] >> 8), (byte)values[0]];
        }
        else
        {
            pdu = new byte[6 + values.Length * 2];
            pdu[0] = 0x10;
            pdu[1] = (byte)(offset >> 8); pdu[2] = (byte)offset;
            pdu[3] = (byte)(values.Length >> 8); pdu[4] = (byte)values.Length;
            pdu[5] = (byte)(values.Length * 2);
            for (int i = 0; i < values.Length; i++)
            {
                pdu[6 + 2 * i] = (byte)(values[i] >> 8);
                pdu[7 + 2 * i] = (byte)values[i];
            }
        }
        await TransactAsync(pdu, ct);
    }

    public override async Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default)
    {
        if (count is < 1 or > 2000) throw new PlcException("Số bit đọc phải trong 1..2000");
        var (area, offset) = ParseAddress(address);
        byte fc = area switch
        {
            Area.Coil => 0x01,
            Area.DiscreteInput => 0x02,
            _ => throw new PlcException($"'{address}' không phải coil/discrete input; dùng Cxxx hoặc DIxxx"),
        };

        var pdu = new byte[] { fc, (byte)(offset >> 8), (byte)offset, (byte)(count >> 8), (byte)count };
        var resp = await TransactAsync(pdu, ct);
        int needed = (count + 7) / 8;
        if (resp.Length < 2 + needed)
            throw new PlcException("Phản hồi Modbus thiếu dữ liệu bit");

        var result = new bool[count];
        for (int i = 0; i < count; i++)
            result[i] = (resp[2 + i / 8] >> (i % 8) & 1) == 1;
        return result;
    }

    public override async Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
    {
        var (area, offset) = ParseAddress(address);
        if (area != Area.Coil)
            throw new PlcException($"Chỉ ghi được coil (Cxxx), địa chỉ '{address}' không hợp lệ");

        var pdu = new byte[] { 0x05, (byte)(offset >> 8), (byte)offset, value ? (byte)0xFF : (byte)0x00, 0x00 };
        await TransactAsync(pdu, ct);
    }

    /// <summary>Đóng gói MBAP + PDU, gửi, nhận và kiểm tra exception. Trả về PDU phản hồi.</summary>
    private Task<byte[]> TransactAsync(byte[] pdu, CancellationToken ct)
        => ExecuteAsync(async (stream, token) =>
        {
            ushort tid = ++_transactionId;
            var frame = new byte[7 + pdu.Length];
            frame[0] = (byte)(tid >> 8); frame[1] = (byte)tid;   // Transaction ID
            frame[2] = 0; frame[3] = 0;                          // Protocol ID = 0
            int len = pdu.Length + 1;
            frame[4] = (byte)(len >> 8); frame[5] = (byte)len;   // Length (unit id + pdu)
            frame[6] = _unitId;
            Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
            await stream.WriteAsync(frame, token);

            var header = new byte[7];
            await ReadExactAsync(stream, header, header.Length, token);
            int rlen = header[4] << 8 | header[5];
            if (rlen < 2 || rlen > 260)
                throw new PlcException($"Độ dài phản hồi Modbus bất thường: {rlen}");

            var body = new byte[rlen - 1];
            await ReadExactAsync(stream, body, body.Length, token);

            ushort rtid = (ushort)(header[0] << 8 | header[1]);
            if (rtid != tid)
                throw new PlcException($"Transaction ID không khớp (gửi {tid}, nhận {rtid})");

            if ((body[0] & 0x80) != 0)
            {
                byte ex = body.Length > 1 ? body[1] : (byte)0;
                throw new PlcException($"Modbus exception {ex} (FC {pdu[0]:X2}): {DescribeException(ex)}");
            }
            if (body[0] != pdu[0])
                throw new PlcException($"Mã hàm phản hồi không khớp ({body[0]:X2} ≠ {pdu[0]:X2})");

            return body;
        }, ct);

    private static string DescribeException(byte code) => code switch
    {
        1 => "Illegal Function – thiết bị không hỗ trợ mã hàm này",
        2 => "Illegal Data Address – địa chỉ không tồn tại",
        3 => "Illegal Data Value – giá trị hoặc số lượng không hợp lệ",
        4 => "Slave Device Failure",
        5 => "Acknowledge",
        6 => "Slave Device Busy",
        10 => "Gateway Path Unavailable",
        11 => "Gateway Target Device Failed To Respond",
        _ => "Mã lỗi không xác định",
    };
}
