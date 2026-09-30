using System.IO;
using HeThongDoKhoangCach.Models;
using Microsoft.Data.Sqlite;

namespace HeThongDoKhoangCach.Services;

/// <summary>
/// Database SQLite chứa bảng quy cách và model (mặc định Data\HeThongDo.db):
/// QuyCach(Id, SortOrder, Lsl, Usl, Unit, Condition) – Model(Id, QuyCachId, SortOrder, Name, PlcBit).
/// File chưa có thì tạo mới và nạp bảng quy cách theo tài liệu khách hàng.
/// Có thể mở/sửa bằng công cụ SQLite bất kỳ; phần mềm đọc lại khi khởi động và sau khi lưu ở màn DANH MỤC.
/// </summary>
public sealed class SpecDatabase
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS QuyCach (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            SortOrder INTEGER NOT NULL,
            Lsl       REAL    NOT NULL,
            Usl       REAL    NOT NULL,
            Unit      TEXT    NOT NULL DEFAULT 'mm',
            Condition TEXT    NOT NULL DEFAULT ''
        );
        CREATE TABLE IF NOT EXISTS Model (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            QuyCachId INTEGER NOT NULL REFERENCES QuyCach(Id) ON DELETE CASCADE,
            SortOrder INTEGER NOT NULL,
            Name      TEXT    NOT NULL,
            PlcBit    TEXT    NOT NULL DEFAULT ''
        );
        """;

    public SpecDatabase(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    /// <summary>Đọc toàn bộ quy cách (kèm model), sắp theo SortOrder.</summary>
    public List<SpecDefinition> Load()
    {
        using var conn = Open();
        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM QuyCach";
            if (Convert.ToInt64(count.ExecuteScalar()) == 0) WriteAll(conn, SpecCatalog.Defaults());
        }

        var specs = new List<SpecDefinition>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, SortOrder, Lsl, Usl, Unit, Condition FROM QuyCach ORDER BY SortOrder, Id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                specs.Add(new SpecDefinition
                {
                    Id = r.GetInt32(0),
                    SortOrder = r.GetInt32(1),
                    Lsl = r.GetDouble(2),
                    Usl = r.GetDouble(3),
                    Unit = r.GetString(4),
                    Condition = r.GetString(5),
                });
            }
        }

        var byId = specs.ToDictionary(s => s.Id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, QuyCachId, SortOrder, Name, PlcBit FROM Model ORDER BY QuyCachId, SortOrder, Id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byId.TryGetValue(r.GetInt32(1), out var spec)) continue;
                spec.Models.Add(new ModelDefinition
                {
                    Id = r.GetInt32(0),
                    SortOrder = r.GetInt32(2),
                    Name = r.GetString(3),
                    PlcBit = r.GetString(4),
                });
            }
        }
        return specs;
    }

    /// <summary>Ghi đè toàn bộ bảng quy cách – model trong một transaction (kết quả đo không tham chiếu Id nên không ảnh hưởng).</summary>
    public void Save(IReadOnlyList<SpecDefinition> specs)
    {
        var error = Validate(specs);
        if (error is not null) throw new ArgumentException(error, nameof(specs));
        using var conn = Open();
        WriteAll(conn, specs);
    }

    public static string? Validate(IReadOnlyList<SpecDefinition> specs)
    {
        if (specs.Count == 0) return "Cần ít nhất một quy cách.";
        var limits = new HashSet<(double, double)>();
        var bits = new Dictionary<string, SpecDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            if (!double.IsFinite(spec.Lsl) || !double.IsFinite(spec.Usl) || spec.Lsl >= spec.Usl)
                return "LSL và USL phải là số hữu hạn, LSL nhỏ hơn USL.";
            if (!limits.Add((spec.Lsl, spec.Usl))) return $"Quy cách {spec.Name} bị trùng.";
            if (spec.Unit != "mm") return "Đơn vị quy cách phải là mm.";
            if (spec.Models.Count == 0) return $"Quy cách {spec.Name} cần ít nhất một model.";
            var names = new HashSet<string>();
            foreach (var model in spec.Models)
            {
                var name = SpecCatalog.NormalizeModel(model.Name);
                if (name.Length == 0 || !names.Add(name)) return $"Model trống hoặc trùng trong quy cách {spec.Name}.";
                var bit = (model.PlcBit ?? "").Replace(" ", "").Trim();
                if (bit.Length == 0) continue;
                if (bits.TryGetValue(bit, out var owner) && owner != spec)
                    return $"Bit {bit} không thể dùng cho hai quy cách khác nhau.";
                bits[bit] = spec; // M1 và M2 được phép dùng chung M42 trong cùng quy cách.
            }
        }
        return null;
    }

    private SqliteConnection Open()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            ForeignKeys = true,
            Pooling = false,   // không giữ file mở sau khi đọc/ghi → có thể sao lưu, chép file bất cứ lúc nào
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static void WriteAll(SqliteConnection conn, IReadOnlyList<SpecDefinition> specs)
    {
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Model; DELETE FROM QuyCach;";
            del.ExecuteNonQuery();
        }

        using var insSpec = conn.CreateCommand();
        insSpec.Transaction = tx;
        insSpec.CommandText = "INSERT INTO QuyCach (SortOrder, Lsl, Usl, Unit, Condition) VALUES ($o, $lsl, $usl, $unit, $cond); SELECT last_insert_rowid();";
        var pOrder = insSpec.Parameters.Add("$o", SqliteType.Integer);
        var pLsl = insSpec.Parameters.Add("$lsl", SqliteType.Real);
        var pUsl = insSpec.Parameters.Add("$usl", SqliteType.Real);
        var pUnit = insSpec.Parameters.Add("$unit", SqliteType.Text);
        var pCond = insSpec.Parameters.Add("$cond", SqliteType.Text);

        using var insModel = conn.CreateCommand();
        insModel.Transaction = tx;
        insModel.CommandText = "INSERT INTO Model (QuyCachId, SortOrder, Name, PlcBit) VALUES ($spec, $o, $name, $bit); SELECT last_insert_rowid();";
        var mSpec = insModel.Parameters.Add("$spec", SqliteType.Integer);
        var mOrder = insModel.Parameters.Add("$o", SqliteType.Integer);
        var mName = insModel.Parameters.Add("$name", SqliteType.Text);
        var mBit = insModel.Parameters.Add("$bit", SqliteType.Text);

        foreach (var spec in specs)
        {
            pOrder.Value = spec.SortOrder;
            pLsl.Value = spec.Lsl;
            pUsl.Value = spec.Usl;
            pUnit.Value = string.IsNullOrWhiteSpace(spec.Unit) ? "mm" : spec.Unit.Trim();
            pCond.Value = (spec.Condition ?? "").Trim();
            spec.Id = Convert.ToInt32(insSpec.ExecuteScalar());

            foreach (var model in spec.Models)
            {
                mSpec.Value = spec.Id;
                mOrder.Value = model.SortOrder;
                mName.Value = model.Name.Trim();
                mBit.Value = (model.PlcBit ?? "").Trim();
                model.Id = Convert.ToInt32(insModel.ExecuteScalar());
            }
        }
        tx.Commit();
    }
}
