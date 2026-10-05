using BeltTensionMeasurement.Models;

namespace BeltTensionMeasurement.Services.Plc;

public static class PlcClientFactory
{
    /// <param name="s">Cài đặt kết nối và bảng tag.</param>
    /// <param name="specs">Bảng chủng loại (PLC mô phỏng dùng để biết bit chủng loại nào ứng với quy cách nào).</param>
    public static IPlcClient Create(PlcSettings s, IReadOnlyList<SpecDefinition>? specs = null) => s.Protocol switch
    {
        PlcProtocol.Simulation => new SimulationPlcClient(s, specs ?? []),
        PlcProtocol.McProtocol3E => new McProtocolClient(s.IpAddress.Trim(), s.Port, s.TimeoutMs),
        PlcProtocol.ModbusTcp => new ModbusTcpClient(s.IpAddress.Trim(), s.Port, s.UnitId, s.TimeoutMs),
        _ => throw new PlcException($"Giao thức không hỗ trợ: {s.Protocol}"),
    };
}
