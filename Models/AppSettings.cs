using System.Globalization;

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

/// <summary>Định nghĩa một tag: địa chỉ + cách giải mã. Địa chỉ để trống = PLC không cung cấp dữ liệu này.</summary>
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
/// Bảng địa chỉ PLC. PLC là nơi xử lý toàn bộ (đo, giữ serial, thông tin đơn hàng, quy cách, phân định, đếm);
/// phần mềm chỉ đọc lên hiển thị và ghi 3 bit lệnh START/STOP/RESET.
/// </summary>
public class PlcSettings
{
    public PlcProtocol Protocol { get; set; } = PlcProtocol.Simulation;
    public string IpAddress { get; set; } = "192.168.1.10";
    public int Port { get; set; } = 5000;
    /// <summary>Unit ID (chỉ dùng cho Modbus).</summary>
    public byte UnitId { get; set; } = 1;
    public int PollIntervalMs { get; set; } = 250;
    public int TimeoutMs { get; set; } = 2000;

    // ----- Giá trị đo -----
    public TagDefinition LoadcellValue { get; set; } = new() { Address = "D100", DataType = TagDataType.Float32 };
    public TagDefinition DistanceValue { get; set; } = new() { Address = "D102", DataType = TagDataType.Float32 };
    /// <summary>Kết quả đo chốt của chu kỳ (ô KẾT QUẢ). Trống → lấy khoảng cách tại thời điểm Đo xong.</summary>
    public TagDefinition ResultValue { get; set; } = new() { Address = "D104", DataType = TagDataType.Float32 };

    // ----- Quy cách từ PLC (trống → tra theo chủng loại trong cài đặt) -----
    public TagDefinition SpecLsl { get; set; } = new() { Address = "D106", DataType = TagDataType.Float32 };
    public TagDefinition SpecUsl { get; set; } = new() { Address = "D108", DataType = TagDataType.Float32 };

    // ----- Bộ đếm từ PLC (trống → phần mềm tự đếm theo lịch sử đã lưu) -----
    public TagDefinition TotalCount { get; set; } = new() { Address = "D110", DataType = TagDataType.Int32 };
    public TagDefinition OkCount { get; set; } = new() { Address = "D112", DataType = TagDataType.Int32 };
    public TagDefinition NgCount { get; set; } = new() { Address = "D114", DataType = TagDataType.Int32 };

    // ----- Chuỗi thông tin đơn hàng (trống → serial nhập tại phần mềm, tra file master) -----
    public TagDefinition SerialText { get; set; } = new() { Address = "D200", DataType = TagDataType.String, Length = 10 };
    public TagDefinition OrderNoText { get; set; } = new() { Address = "D210", DataType = TagDataType.String, Length = 10 };
    public TagDefinition LineText { get; set; } = new() { Address = "D220", DataType = TagDataType.String, Length = 5 };
    public TagDefinition ModelText { get; set; } = new() { Address = "D225", DataType = TagDataType.String, Length = 10 };

    // ----- Bit trạng thái -----
    public TagDefinition LoadcellStable { get; set; } = new() { Address = "M100", DataType = TagDataType.Bit };
    /// <summary>Sườn lên = PLC vừa có một kết quả đo mới (phần mềm ghi vào lịch sử).</summary>
    public TagDefinition MeasureDone { get; set; } = new() { Address = "M101", DataType = TagDataType.Bit };
    /// <summary>Kết quả phân định của PLC. Trống cả hai → phần mềm tự so với quy cách.</summary>
    public TagDefinition JudgeOk { get; set; } = new() { Address = "M102", DataType = TagDataType.Bit };
    public TagDefinition JudgeNg { get; set; } = new() { Address = "M103", DataType = TagDataType.Bit };
    /// <summary>PLC đang ở trạng thái chạy. Trống → theo nút START/STOP trên phần mềm.</summary>
    public TagDefinition RunningState { get; set; } = new() { Address = "M104", DataType = TagDataType.Bit };

    // ----- Bit lệnh phần mềm ghi xuống -----
    public TagDefinition StartCommand { get; set; } = new() { Address = "M110", DataType = TagDataType.Bit };
    public TagDefinition StopCommand { get; set; } = new() { Address = "M111", DataType = TagDataType.Bit };
    public TagDefinition ResetCommand { get; set; } = new() { Address = "M112", DataType = TagDataType.Bit };

    public void ApplyMcDefaults()
    {
        Port = 5000;
        LoadcellValue = new() { Address = "D100", DataType = TagDataType.Float32 };
        DistanceValue = new() { Address = "D102", DataType = TagDataType.Float32 };
        ResultValue = new() { Address = "D104", DataType = TagDataType.Float32 };
        SpecLsl = new() { Address = "D106", DataType = TagDataType.Float32 };
        SpecUsl = new() { Address = "D108", DataType = TagDataType.Float32 };
        TotalCount = new() { Address = "D110", DataType = TagDataType.Int32 };
        OkCount = new() { Address = "D112", DataType = TagDataType.Int32 };
        NgCount = new() { Address = "D114", DataType = TagDataType.Int32 };
        SerialText = new() { Address = "D200", DataType = TagDataType.String, Length = 10 };
        OrderNoText = new() { Address = "D210", DataType = TagDataType.String, Length = 10 };
        LineText = new() { Address = "D220", DataType = TagDataType.String, Length = 5 };
        ModelText = new() { Address = "D225", DataType = TagDataType.String, Length = 10 };
        LoadcellStable = new() { Address = "M100", DataType = TagDataType.Bit };
        MeasureDone = new() { Address = "M101", DataType = TagDataType.Bit };
        JudgeOk = new() { Address = "M102", DataType = TagDataType.Bit };
        JudgeNg = new() { Address = "M103", DataType = TagDataType.Bit };
        RunningState = new() { Address = "M104", DataType = TagDataType.Bit };
        StartCommand = new() { Address = "M110", DataType = TagDataType.Bit };
        StopCommand = new() { Address = "M111", DataType = TagDataType.Bit };
        ResetCommand = new() { Address = "M112", DataType = TagDataType.Bit };
    }

    public void ApplyModbusDefaults()
    {
        Port = 502;
        LoadcellValue = new() { Address = "HR100", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        DistanceValue = new() { Address = "HR102", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        ResultValue = new() { Address = "HR104", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        SpecLsl = new() { Address = "HR106", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        SpecUsl = new() { Address = "HR108", DataType = TagDataType.Float32, WordOrder = WordOrder.HighLow };
        TotalCount = new() { Address = "HR110", DataType = TagDataType.Int32, WordOrder = WordOrder.HighLow };
        OkCount = new() { Address = "HR112", DataType = TagDataType.Int32, WordOrder = WordOrder.HighLow };
        NgCount = new() { Address = "HR114", DataType = TagDataType.Int32, WordOrder = WordOrder.HighLow };
        SerialText = new() { Address = "HR200", DataType = TagDataType.String, Length = 10, WordOrder = WordOrder.HighLow };
        OrderNoText = new() { Address = "HR210", DataType = TagDataType.String, Length = 10, WordOrder = WordOrder.HighLow };
        LineText = new() { Address = "HR220", DataType = TagDataType.String, Length = 5, WordOrder = WordOrder.HighLow };
        ModelText = new() { Address = "HR225", DataType = TagDataType.String, Length = 10, WordOrder = WordOrder.HighLow };
        LoadcellStable = new() { Address = "C100", DataType = TagDataType.Bit };
        MeasureDone = new() { Address = "C101", DataType = TagDataType.Bit };
        JudgeOk = new() { Address = "C102", DataType = TagDataType.Bit };
        JudgeNg = new() { Address = "C103", DataType = TagDataType.Bit };
        RunningState = new() { Address = "C104", DataType = TagDataType.Bit };
        StartCommand = new() { Address = "C110", DataType = TagDataType.Bit };
        StopCommand = new() { Address = "C111", DataType = TagDataType.Bit };
        ResetCommand = new() { Address = "C112", DataType = TagDataType.Bit };
    }
}

/// <summary>Quy cách (giới hạn dưới/trên) theo chủng loại – dùng khi PLC không gửi LSL/USL.</summary>
public class ModelSpec
{
    public string Model { get; set; } = "";
    /// <summary>Nhóm dùng để gộp dữ liệu vẽ histogram (vd: "CPX - GSM").</summary>
    public string Group { get; set; } = "";
    public double Lsl { get; set; }
    public double Usl { get; set; }
    public string Unit { get; set; } = "mm";

    public string SpecText => string.Create(CultureInfo.InvariantCulture, $"{Lsl:0.#} ~ {Usl:0.#} {Unit}");

    public ModelSpec Clone() => (ModelSpec)MemberwiseClone();
}

public class AppSettings
{
    public const string LegacyDefaultTitle = "HỆ THỐNG ĐO LỰC CĂNG BELT – PLC & LOADCELL";

    public string Title { get; set; } = "HỆ THỐNG ĐO KHOẢNG CÁCH – PLC XYZ & LOADCELL";
    public string CompanyLabel { get; set; } = "MVA Lab";
    public string Inspector { get; set; } = "23474";
    public string ForceUnit { get; set; } = "gf";

    public PlcSettings Plc { get; set; } = new();
    public List<ModelSpec> ModelSpecs { get; set; } = DefaultModelSpecs();

    /// <summary>File master của BISG: Key(serial hoặc tiền tố serial),OrderNo,Line,Model – chỉ dùng khi PLC không gửi thông tin đơn hàng.</summary>
    public string MasterFilePath { get; set; } = @"Data\master.csv";
    /// <summary>File lưu lịch sử kết quả (JSON Lines, mỗi dòng một kết quả).</summary>
    public string ResultFilePath { get; set; } = @"Data\results.jsonl";

    /// <summary>Tự ghi vào lịch sử khi PLC báo đo xong và đã có kết quả OK/NG.</summary>
    public bool AutoSaveOnMeasureDone { get; set; } = true;
    public int HistogramBins { get; set; } = 24;
    /// <summary>Số ngày (có dữ liệu) gần nhất hiển thị trên TREND CHART.</summary>
    public int TrendDays { get; set; } = 10;

    /// <summary>Thời điểm nhấn RESET trên từng histogram: chỉ vẽ kết quả đo sau thời điểm này.</summary>
    public Dictionary<string, DateTime> HistogramResetTimes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static List<ModelSpec> DefaultModelSpecs() =>
    [
        new() { Model = "CPX",     Group = "CPX - GSM",           Lsl = 3.0, Usl = 4.0 },
        new() { Model = "GSM RUP", Group = "CPX - GSM",           Lsl = 3.0, Usl = 4.0 },
        new() { Model = "NF 8.3",  Group = "NF - M1 - M2 - PP1",  Lsl = 3.0, Usl = 5.0 },
        new() { Model = "M1-M2",   Group = "NF - M1 - M2 - PP1",  Lsl = 3.0, Usl = 5.0 },
        new() { Model = "PP1",     Group = "NF - M1 - M2 - PP1",  Lsl = 3.0, Usl = 5.0 },
    ];
}
