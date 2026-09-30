namespace HeThongDoKhoangCach.Models;

/// <summary>Giao thức truyền thông với PLC.</summary>
public enum PlcProtocol
{
    /// <summary>Mô phỏng nội bộ, không cần PLC thật.</summary>
    Simulation,
    /// <summary>Mitsubishi MC Protocol (SLMP) – 3E frame, mã Binary, qua TCP.</summary>
    McProtocol3E,
    /// <summary>Modbus TCP (holding/input register, coil, discrete input).</summary>
    ModbusTcp,
}

/// <summary>Kiểu dữ liệu của một tag trên PLC.</summary>
public enum TagDataType
{
    Bit,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Float32,
    /// <summary>Chuỗi ASCII lưu trong dãy word liên tiếp (2 ký tự/word), độ dài = <see cref="TagDefinition.Length"/> word.</summary>
    String,
}

/// <summary>Thứ tự 2 word khi ghép thành giá trị 32 bit (với String: thứ tự 2 byte trong một word).</summary>
public enum WordOrder
{
    /// <summary>Word thấp trước, word cao sau (mặc định Mitsubishi). String: byte thấp là ký tự trước.</summary>
    LowHigh,
    /// <summary>Word cao trước, word thấp sau (thường gặp ở Modbus). String: byte cao là ký tự trước.</summary>
    HighLow,
}

/// <summary>Định nghĩa một tag: địa chỉ + cách giải mã. Địa chỉ để trống = không dùng tag này.</summary>
public class TagDefinition
{
    public string Address { get; set; } = "";
    public TagDataType DataType { get; set; } = TagDataType.Int16;
    /// <summary>Hệ số nhân sau khi đọc (vd: PLC lưu 370 → 3.70 mm thì Scale = 0.01).</summary>
    public double Scale { get; set; } = 1.0;
    public WordOrder WordOrder { get; set; } = WordOrder.LowHigh;
    /// <summary>Số word của chuỗi (chỉ dùng với <see cref="TagDataType.String"/>).</summary>
    public int Length { get; set; } = 10;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Address);

    /// <summary>Số word cần đọc cho kiểu dữ liệu này (0 với Bit).</summary>
    public int WordCount => DataType switch
    {
        TagDataType.Bit => 0,
        TagDataType.Int16 or TagDataType.UInt16 => 1,
        TagDataType.String => Math.Clamp(Length, 1, 120),
        _ => 2,
    };

    public TagDefinition Clone() => (TagDefinition)MemberwiseClone();
}

/// <summary>
/// Bảng địa chỉ PLC theo luồng vận hành mới:
/// phần mềm ghi bit START (M0) / STOP (M15) / RESET và bit chủng loại (M40..M50) khi chọn model;
/// PLC gửi liên tục lực căng, khoảng cách, kết quả đo, OK/NG, bộ đếm qua thanh ghi D / bit M
/// và phần mềm đọc theo chu kỳ để hiển thị đồng thời.
/// </summary>
public class PlcSettings
{
    public PlcProtocol Protocol { get; set; } = PlcProtocol.Simulation;
    public string IpAddress { get; set; } = "192.168.1.10";
    public int Port { get; set; } = 5000;
    /// <summary>Unit ID (chỉ dùng cho Modbus).</summary>
    public byte UnitId { get; set; } = 1;
    public int PollIntervalMs { get; set; } = 200;
    public int TimeoutMs { get; set; } = 2000;

    // ----- Giá trị PLC gửi liên tục -----
    public TagDefinition LoadcellValue { get; set; } = new() { Address = "D100", DataType = TagDataType.Float32 };
    public TagDefinition DistanceValue { get; set; } = new() { Address = "D102", DataType = TagDataType.Float32 };
    /// <summary>Kết quả đo chốt của lần đo gần nhất (ô KẾT QUẢ ĐO). Trống → lấy khoảng cách tại lúc Đo xong.</summary>
    public TagDefinition ResultValue { get; set; } = new() { Address = "D104", DataType = TagDataType.Float32 };

    // ----- Bộ đếm từ PLC (trống → phần mềm tự đếm theo lịch sử đã lưu) -----
    public TagDefinition TotalCount { get; set; } = new() { Address = "D110", DataType = TagDataType.Int32 };
    public TagDefinition OkCount { get; set; } = new() { Address = "D112", DataType = TagDataType.Int32 };
    public TagDefinition NgCount { get; set; } = new() { Address = "D114", DataType = TagDataType.Int32 };

    // ----- Bit trạng thái -----
    public TagDefinition LoadcellStable { get; set; } = new() { Address = "M100", DataType = TagDataType.Bit };
    /// <summary>
    /// Sườn lên = PLC vừa có một kết quả đo mới (phần mềm ghi vào lịch sử).
    /// Trống → phần mềm nhận biết kết quả mới khi bộ đếm TOTAL tăng, hoặc khi giá trị Kết quả đo thay đổi.
    /// </summary>
    public TagDefinition MeasureDone { get; set; } = new() { Address = "M101", DataType = TagDataType.Bit };
    /// <summary>Kết quả phân định của PLC. Trống cả hai → phần mềm tự so với quy cách của chủng loại.</summary>
    public TagDefinition JudgeOk { get; set; } = new() { Address = "M102", DataType = TagDataType.Bit };
    public TagDefinition JudgeNg { get; set; } = new() { Address = "M103", DataType = TagDataType.Bit };
    /// <summary>PLC đang ở trạng thái chạy. Trống → theo nút START/STOP trên phần mềm.</summary>
    public TagDefinition RunningState { get; set; } = new() { Address = "M104", DataType = TagDataType.Bit };

    // ----- Bit lệnh phần mềm ghi xuống (theo yêu cầu khách: Start M0, Stop M15) -----
    public TagDefinition StartCommand { get; set; } = new() { Address = "M0", DataType = TagDataType.Bit };
    public TagDefinition StopCommand { get; set; } = new() { Address = "M15", DataType = TagDataType.Bit };
    public TagDefinition ResetCommand { get; set; } = new() { Address = "M16", DataType = TagDataType.Bit };

    // ----- Quy cách ghi xuống PLC khi chọn model (tùy chọn, nếu PLC muốn nhận LSL/USL từ phần mềm) -----
    public TagDefinition SpecLslWrite { get; set; } = new() { Address = "", DataType = TagDataType.Float32 };
    public TagDefinition SpecUslWrite { get; set; } = new() { Address = "", DataType = TagDataType.Float32 };

    public void ApplyMcDefaults()
    {
        Port = 5000;
        LoadcellValue = new() { Address = "D100", DataType = TagDataType.Float32 };
        DistanceValue = new() { Address = "D102", DataType = TagDataType.Float32 };
        ResultValue = new() { Address = "D104", DataType = TagDataType.Float32 };
        TotalCount = new() { Address = "D110", DataType = TagDataType.Int32 };
        OkCount = new() { Address = "D112", DataType = TagDataType.Int32 };
        NgCount = new() { Address = "D114", DataType = TagDataType.Int32 };
        LoadcellStable = new() { Address = "M100", DataType = TagDataType.Bit };
        MeasureDone = new() { Address = "M101", DataType = TagDataType.Bit };
        JudgeOk = new() { Address = "M102", DataType = TagDataType.Bit };
        JudgeNg = new() { Address = "M103", DataType = TagDataType.Bit };
        RunningState = new() { Address = "M104", DataType = TagDataType.Bit };
        StartCommand = new() { Address = "M0", DataType = TagDataType.Bit };
        StopCommand = new() { Address = "M15", DataType = TagDataType.Bit };
        ResetCommand = new() { Address = "M16", DataType = TagDataType.Bit };
        SpecLslWrite = new() { Address = "", DataType = TagDataType.Float32 };
        SpecUslWrite = new() { Address = "", DataType = TagDataType.Float32 };
    }

    public void ApplyModbusDefaults()
    {
        Port = 502;
        LoadcellValue = new() { Address = "HR100", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        DistanceValue = new() { Address = "HR102", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        ResultValue = new() { Address = "HR104", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        TotalCount = new() { Address = "HR110", DataType = TagDataType.Int32, WordOrder = WordOrder.HighLow };
        OkCount = new() { Address = "HR112", DataType = TagDataType.Int32, WordOrder = WordOrder.HighLow };
        NgCount = new() { Address = "HR114", DataType = TagDataType.Int32, WordOrder = WordOrder.HighLow };
        LoadcellStable = new() { Address = "C100", DataType = TagDataType.Bit };
        MeasureDone = new() { Address = "C101", DataType = TagDataType.Bit };
        JudgeOk = new() { Address = "C102", DataType = TagDataType.Bit };
        JudgeNg = new() { Address = "C103", DataType = TagDataType.Bit };
        RunningState = new() { Address = "C104", DataType = TagDataType.Bit };
        StartCommand = new() { Address = "C0", DataType = TagDataType.Bit };
        StopCommand = new() { Address = "C15", DataType = TagDataType.Bit };
        ResetCommand = new() { Address = "C16", DataType = TagDataType.Bit };
        SpecLslWrite = new() { Address = "", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        SpecUslWrite = new() { Address = "", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
    }
}

public class AppSettings
{
    /// <summary>Tiêu đề mặc định của bản trước (đổi sang tiêu đề theo mock mới khi nạp cài đặt cũ).</summary>
    public const string LegacyDefaultTitle = "HỆ THỐNG ĐO KHOẢNG CÁCH – PLC XYZ & LOADCELL";

    public string Title { get; set; } = "HỆ THỐNG ĐO LỰC CĂNG BELT – PLC & LOADCELL";
    public string CompanyLabel { get; set; } = "MVA Lab";
    public string Inspector { get; set; } = "23474";
    public string ForceUnit { get; set; } = "gf";

    public PlcSettings Plc { get; set; } = new();

    /// <summary>Tự kết nối PLC ngay khi mở phần mềm (vẫn có nút KẾT NỐI PLC để nối/ngắt thủ công).</summary>
    public bool AutoConnectPlc { get; set; } = true;

    /// <summary>Database SQLite chứa bảng quy cách – model (chọn ở Bước 1, Bước 2 trên màn hình chính).</summary>
    public string DatabaseFilePath { get; set; } = @"Data\HeThongDo.db";
    /// <summary>File master của BISG: Key(serial hoặc tiền tố serial),OrderNo,Line,Model – tra đơn hàng/line theo serial vừa scan.</summary>
    public string MasterFilePath { get; set; } = @"Data\master.csv";
    /// <summary>File lưu lịch sử kết quả (JSON Lines, mỗi dòng một kết quả).</summary>
    public string ResultFilePath { get; set; } = @"Data\results.jsonl";

    /// <summary>Mẫu nội dung mã QR của kết quả đo, vd "{Value}" hoặc "{Serial};{Model};{Value};{Result}".</summary>
    public string ResultQrTemplate { get; set; } = "{Value}";

    /// <summary>Tự ghi vào lịch sử ngay khi PLC gửi kết quả mới và đã có OK/NG.</summary>
    public bool AutoSaveOnMeasureDone { get; set; } = true;
    public int HistogramBins { get; set; } = 24;

    /// <summary>Quy cách / model đang chọn lần trước (tên hiển thị) – chọn lại sẵn khi mở phần mềm.</summary>
    public string LastSpec { get; set; } = "";
    public string LastModel { get; set; } = "";
}
