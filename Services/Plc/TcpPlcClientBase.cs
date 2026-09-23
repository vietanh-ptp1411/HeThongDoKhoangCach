using System.IO;
using System.Net.Sockets;

namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>
/// Lớp nền cho các client PLC chạy trên TCP: quản lý socket, timeout,
/// khóa tuần tự (một giao dịch tại một thời điểm) và chuyển lỗi mạng thành <see cref="PlcException"/>.
/// </summary>
public abstract class TcpPlcClientBase : IPlcClient
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    protected TcpPlcClientBase(string host, int port, int timeoutMs)
    {
        Host = host;
        Port = port;
        TimeoutMs = Math.Max(200, timeoutMs);
    }

    public string Host { get; }
    public int Port { get; }
    public int TimeoutMs { get; }

    public bool IsConnected => _tcp is { Connected: true } && _stream is not null;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            CloseSocket();
            var tcp = new TcpClient { NoDelay = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeoutMs);
            try
            {
                await tcp.ConnectAsync(Host, Port, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                tcp.Dispose();
                throw new PlcException($"Hết thời gian kết nối tới {Host}:{Port} ({TimeoutMs} ms)");
            }
            catch (SocketException ex)
            {
                tcp.Dispose();
                throw new PlcException($"Không kết nối được {Host}:{Port}: {ex.SocketErrorCode}", ex);
            }
            _tcp = tcp;
            _stream = tcp.GetStream();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task DisconnectAsync()
    {
        CloseSocket();
        return Task.CompletedTask;
    }

    private void CloseSocket()
    {
        try { _stream?.Dispose(); } catch { /* bỏ qua */ }
        try { _tcp?.Dispose(); } catch { /* bỏ qua */ }
        _stream = null;
        _tcp = null;
    }

    /// <summary>
    /// Thực hiện một giao dịch request/response trên socket dưới khóa tuần tự và timeout.
    /// Mọi lỗi mạng đều đóng socket (để lần poll sau kết nối lại) và ném <see cref="PlcException"/>.
    /// </summary>
    protected async Task<T> ExecuteAsync<T>(Func<NetworkStream, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            var stream = _stream;
            if (stream is null || _tcp is not { Connected: true })
                throw new PlcException("Chưa kết nối PLC");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeoutMs);
            try
            {
                return await action(stream, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                CloseSocket();
                throw new PlcException($"PLC không phản hồi trong {TimeoutMs} ms");
            }
            catch (IOException ex)
            {
                CloseSocket();
                throw new PlcException("Lỗi truyền thông: " + ex.Message, ex);
            }
            catch (SocketException ex)
            {
                CloseSocket();
                throw new PlcException("Lỗi socket: " + ex.SocketErrorCode, ex);
            }
            catch (ObjectDisposedException)
            {
                CloseSocket();
                throw new PlcException("Kết nối đã bị đóng");
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>Đọc đúng <paramref name="count"/> byte vào đầu buffer.</summary>
    protected static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n == 0) throw new IOException("PLC đã đóng kết nối");
            read += n;
        }
    }

    public abstract Task<ushort[]> ReadWordsAsync(string address, int count, CancellationToken ct = default);
    public abstract Task WriteWordsAsync(string address, ushort[] values, CancellationToken ct = default);
    public abstract Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct = default);
    public abstract Task WriteBitAsync(string address, bool value, CancellationToken ct = default);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _ioLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
