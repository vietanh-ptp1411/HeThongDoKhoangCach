using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services.Plc;

public static class PlcClientFactory
{
    public static IPlcClient Create(PlcSettings s) => s.Protocol switch
    {
        PlcProtocol.Simulation => new SimulationPlcClient(s),
        PlcProtocol.McProtocol3E => new McProtocolClient(s.IpAddress.Trim(), s.Port, s.TimeoutMs),
        PlcProtocol.ModbusTcp => new ModbusTcpClient(s.IpAddress.Trim(), s.Port, s.UnitId, s.TimeoutMs),
        _ => throw new PlcException($"Giao thức không hỗ trợ: {s.Protocol}"),
    };
}
