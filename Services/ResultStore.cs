using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services;

/// <summary>Lưu kết quả đo dạng JSON Lines (mỗi dòng một bản ghi, chỉ ghi thêm – an toàn khi mất điện).</summary>
public sealed class ResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly List<MeasurementResult> _all = [];

    public ResultStore(string filePath) => FilePath = filePath;

    public string FilePath { get; }
    public IReadOnlyList<MeasurementResult> All => _all;
    public int NextNo => _all.Count == 0 ? 1 : _all.Max(r => r.No) + 1;

    public void Load()
    {
        _all.Clear();
        if (!File.Exists(FilePath)) return;
        foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var r = JsonSerializer.Deserialize<MeasurementResult>(line, JsonOptions);
                if (r is not null) _all.Add(r);
            }
            catch (JsonException)
            {
                // bỏ qua dòng hỏng
            }
        }
    }

    public void Append(MeasurementResult result)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.AppendAllText(FilePath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine, Encoding.UTF8);
        _all.Add(result);
    }
}
