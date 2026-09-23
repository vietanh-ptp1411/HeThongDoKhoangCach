namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>Lỗi truyền thông hoặc lỗi giao thức từ PLC.</summary>
public sealed class PlcException : Exception
{
    public PlcException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Giao diện chung cho mọi giao thức PLC (MC Protocol, Modbus TCP, mô phỏng).
/// Địa chỉ được truyền dạng chuỗi theo quy ước của từng giao thức
/// (MC: "D100", "M200", "W1A0"... – Modbus: "HR100", "IR100", "C100", "DI100" hoặc "40101").
/// </summary>
public interface IPlcClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    /// <summary>Đọc liên tiếp <paramref name="count"/> word (16 bit) từ địa chỉ.</summary>
    Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default);

    /// <summary>Ghi liên tiếp các word từ địa chỉ.</summary>
    Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default);

    /// <summary>Đọc liên tiếp <paramref name="count"/> bit từ địa chỉ thiết bị bit.</summary>
    Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default);

    /// <summary>Ghi một bit.</summary>
    Task WriteBitAsync(string address, bool value, CancellationToken ct = default);
}
