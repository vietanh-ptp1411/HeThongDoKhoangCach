namespace HeThongDoKhoangCach.Models;

/// <summary>Thông tin đơn hàng tra được từ file master theo số serial.</summary>
public sealed class OrderInfo
{
    public string Serial { get; init; } = "";
    public string OrderNo { get; init; } = "";
    public string Line { get; init; } = "";
    public string Model { get; init; } = "";
}
