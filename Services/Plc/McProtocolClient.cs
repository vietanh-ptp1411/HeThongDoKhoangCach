using System.Globalization;
using System.Net.Sockets;

namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>Thiết bị Mitsubishi đã phân tích từ chuỗi địa chỉ (vd "D100", "M200", "W1A0").</summary>
internal readonly record struct McDevice(string Prefix, byte Code, int Number, bool IsBit)
{
    // Thứ tự quan trọng: tiền tố 2 chữ phải đứng trước tiền tố 1 chữ.
    private static readonly (string Prefix, byte Code, bool IsBit, bool Hex)[] Table =
    [
        ("SM", 0x91, true,  false), ("SD", 0xA9, false, false), ("ZR", 0xB0, false, false),
        ("TS", 0xC1, true,  false), ("TC", 0xC0, true,  false), ("TN", 0xC2, false, false),
        ("SS", 0xC7, true,  false), ("SC", 0xC6, true,  false), ("SN", 0xC8, false, false),
        ("CS", 0xC4, true,  false), ("CC", 0xC3, true,  false), ("CN", 0xC5, false, false),
        ("SB", 0xA1, true,  true ), ("SW", 0xB5, false, true ),
        ("DX", 0xA2, true,  true ), ("DY", 0xA3, true,  true ),
        ("X",  0x9C, true,  true ), ("Y",  0x9D, true,  true ),
        ("M",  0x90, true,  false), ("L",  0x92, true,  false), ("F",  0x93, true,  false),
        ("V",  0x94, true,  false), ("B",  0xA0, true,  true ), ("S",  0x98, true,  false),
        ("D",  0xA8, false, false), ("W",  0xB4, false, true ), ("R",  0xAF, false, false),
        ("Z",  0xCC, false, false),
    ];

    public static McDevice Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new PlcException("Địa chỉ MC đang để trống");

        var s = address.Trim().ToUpperInvariant().Replace(" ", "");
        foreach (var (prefix, code, isBit, hex) in Table)
        {
            if (!s.StartsWith(prefix, StringComparison.Ordinal) || s.Length <= prefix.Length) continue;

            var numPart = s[prefix.Length..];
            var style = hex ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (int.TryParse(numPart, style, CultureInfo.InvariantCulture, out var n) && n >= 0 && n <= 0xFFFFFF)
                return new McDevice(prefix, code, n, isBit);

            throw new PlcException($"Số thiết bị không hợp lệ trong địa chỉ MC '{address}'"
                                   + (hex ? " (thiết bị này dùng số HEX)" : ""));
        }
        throw new PlcException($"Địa chỉ MC không hợp lệ: '{address}'");
    }
}

/// <summary>
/// Client Mitsubishi MC Protocol (SLMP), 3E frame, mã Binary, qua TCP.
/// Hỗ trợ: đọc/ghi word theo khối (0401/1401 sub 0000), đọc/ghi bit theo khối (0401/1401 sub 0001).
/// Dùng được cho Q/L/iQ-R/iQ-F (FX5U) và FX3U + module Ethernet cấu hình MC 3E Binary.
/// </summary>
public sealed class McProtocolClient : TcpPlcClientBase
{
    private const ushort CmdBatchRead = 0x0401;
    private const ushort CmdBatchWrite = 0x1401;
    private const ushort SubWord = 0x0000;
    private const ushort SubBit = 0x0001;
    /// <summary>Monitoring timer: đơn vị 250 ms → 0x0010 = 4 giây.</summary>
    private const ushort MonitoringTimer = 0x0010;
    private const int MaxWordPoints = 960;
    private const int MaxBitPoints = 7168;

    public McProtocolClient(string host, int port, int timeoutMs) : base(host, port, timeoutMs) { }

    public override async Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default)
    {
        if (count is < 1 or > MaxWordPoints)
            throw new PlcException($"Số word đọc phải trong 1..{MaxWordPoints}");
        var dev = McDevice.Parse(address);

        var resp = await TransactAsync(CmdBatchRead, SubWord, BuildDeviceBlock(dev, count), ct);
        if (resp.Length < count * 2)
            throw new PlcException($"Phản hồi MC thiếu dữ liệu ({resp.Length} byte, cần {count * 2})");

        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = (ushort)(resp[2 * i] | resp[2 * i + 1] << 8);
        return result;
    }

    public override async Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default)
    {
        if (values.Length is < 1 or > MaxWordPoints)
            throw new PlcException($"Số word ghi phải trong 1..{MaxWordPoints}");
        var dev = McDevice.Parse(address);
        if (dev.IsBit)
            throw new PlcException($"'{address}' là thiết bị bit, không ghi theo word được");

        var data = BuildDeviceBlock(dev, values.Length, values.Length * 2);
        for (int i = 0; i < values.Length; i++)
        {
            data[6 + 2 * i] = (byte)values[i];
            data[7 + 2 * i] = (byte)(values[i] >> 8);
        }
        await TransactAsync(CmdBatchWrite, SubWord, data, ct);
    }

    public override async Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default)
    {
        if (count is < 1 or > MaxBitPoints)
            throw new PlcException($"Số bit đọc phải trong 1..{MaxBitPoints}");
        var dev = McDevice.Parse(address);
        if (!dev.IsBit)
            throw new PlcException($"'{address}' là thiết bị word; hãy đặt kiểu dữ liệu Int16 để đọc dạng word");

        var resp = await TransactAsync(CmdBatchRead, SubBit, BuildDeviceBlock(dev, count), ct);
        int needed = (count + 1) / 2;
        if (resp.Length < needed)
            throw new PlcException($"Phản hồi MC thiếu dữ liệu bit ({resp.Length} byte, cần {needed})");

        // Mỗi byte chứa 2 điểm: nibble cao = điểm chẵn, nibble thấp = điểm lẻ.
        var result = new bool[count];
        for (int i = 0; i < count; i++)
        {
            byte b = resp[i / 2];
            int nibble = (i % 2 == 0) ? (b >> 4) : (b & 0x0F);
            result[i] = nibble != 0;
        }
        return result;
    }

    public override async Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
    {
        var dev = McDevice.Parse(address);
        if (!dev.IsBit)
            throw new PlcException($"'{address}' là thiết bị word, không ghi theo bit được");

        var data = BuildDeviceBlock(dev, 1, 1);
        data[6] = value ? (byte)0x10 : (byte)0x00;
        await TransactAsync(CmdBatchWrite, SubBit, data, ct);
    }

    // ----- Khung 3E -----

    /// <summary>Head device (3 byte LE) + device code (1) + số điểm (2 LE) + chỗ trống cho dữ liệu.</summary>
    private static byte[] BuildDeviceBlock(McDevice dev, int points, int extraBytes = 0)
    {
        var b = new byte[6 + extraBytes];
        b[0] = (byte)dev.Number;
        b[1] = (byte)(dev.Number >> 8);
        b[2] = (byte)(dev.Number >> 16);
        b[3] = dev.Code;
        b[4] = (byte)points;
        b[5] = (byte)(points >> 8);
        return b;
    }

    private static byte[] BuildFrame(ushort command, ushort subcommand, byte[] data)
    {
        // Request data length = monitoring timer(2) + command(2) + subcommand(2) + data
        // Header cố định 9 byte: subheader(2) net(1) pc(1) io(2) station(1) length(2)
        int dataLen = 6 + data.Length;
        var f = new byte[9 + dataLen];
        f[0] = 0x50; f[1] = 0x00;          // Subheader 3E
        f[2] = 0x00;                       // Network No.
        f[3] = 0xFF;                       // PC No.
        f[4] = 0xFF; f[5] = 0x03;          // Request destination module I/O No. (0x03FF)
        f[6] = 0x00;                       // Request destination module station No.
        f[7] = (byte)dataLen; f[8] = (byte)(dataLen >> 8);
        f[9] = (byte)MonitoringTimer; f[10] = (byte)(MonitoringTimer >> 8);
        f[11] = (byte)command; f[12] = (byte)(command >> 8);
        f[13] = (byte)subcommand; f[14] = (byte)(subcommand >> 8);
        Buffer.BlockCopy(data, 0, f, 15, data.Length);
        return f;
    }

    /// <summary>Gửi lệnh, nhận phản hồi, kiểm tra end code và trả về phần dữ liệu.</summary>
    private Task<byte[]> TransactAsync(ushort command, ushort subcommand, byte[] data, CancellationToken ct)
        => ExecuteAsync(async (stream, token) =>
        {
            await stream.WriteAsync(BuildFrame(command, subcommand, data), token);

            // Header phản hồi: subheader(2) net(1) pc(1) io(2) station(1) length(2)
            var header = new byte[9];
            await ReadExactAsync(stream, header, header.Length, token);
            if (header[0] != 0xD0 || header[1] != 0x00)
                throw new PlcException($"Phản hồi MC không hợp lệ (subheader {header[0]:X2} {header[1]:X2}). "
                                       + "Kiểm tra PLC đã cấu hình 3E frame / Binary chưa.");

            int len = header[7] | header[8] << 8;
            if (len < 2 || len > 8192)
                throw new PlcException($"Độ dài phản hồi MC bất thường: {len}");

            var body = new byte[len];
            await ReadExactAsync(stream, body, len, token);

            ushort endCode = (ushort)(body[0] | body[1] << 8);
            if (endCode != 0)
                throw new PlcException($"PLC trả lỗi MC 0x{endCode:X4}: {DescribeEndCode(endCode)}");

            return body[2..];
        }, ct);

    private static string DescribeEndCode(ushort code) => code switch
    {
        0x0055 => "Không cho phép ghi khi CPU đang RUN (bật 'Enable online change')",
        0xC050 => "Sai mã dữ liệu ASCII/Binary (kiểm tra Communication Data Code của PLC)",
        0xC051 or 0xC052 or 0xC053 or 0xC054 => "Số điểm đọc/ghi vượt giới hạn cho phép",
        0xC056 => "Địa chỉ thiết bị vượt quá phạm vi",
        0xC058 => "Độ dài dữ liệu yêu cầu không khớp",
        0xC059 => "Lệnh hoặc sub-command không được hỗ trợ",
        0xC05B => "CPU không xử lý được thiết bị được chỉ định",
        0xC05C => "Nội dung yêu cầu sai (vd: đọc word trên thiết bị bit)",
        0xC05F => "Yêu cầu không thực hiện được trên CPU đích",
        0xC060 => "Dữ liệu thiết bị sai",
        0xC061 => "Độ dài dữ liệu không khớp số điểm",
        _ => "Tra mã lỗi trong tài liệu MC Protocol / SLMP của Mitsubishi",
    };
}
