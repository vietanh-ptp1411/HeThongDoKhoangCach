using System.IO;
using System.Text;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services;

/// <summary>
/// Tra thông tin đơn hàng từ file master (CSV) của BISG theo số serial.
/// Cột: Key, OrderNo, Line, Model. Key có thể là serial đầy đủ hoặc tiền tố
/// (vd "985X57200" khớp với serial "985X57200-00123"). File được tự nạp lại khi thay đổi.
/// </summary>
public sealed class MasterDataService
{
    private sealed record MasterRow(string Key, string OrderNo, string Line, string Model);

    private readonly object _gate = new();
    private List<MasterRow> _rows = [];
    private DateTime _loadedStamp = DateTime.MinValue;
    private long _loadedLength = -1;

    public MasterDataService(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public int Count
    {
        get { EnsureLoaded(); lock (_gate) return _rows.Count; }
    }

    public OrderInfo? Lookup(string serial)
    {
        var s = serial.Trim();
        if (s.Length == 0) return null;
        EnsureLoaded();

        MasterRow? row;
        lock (_gate)
        {
            row = _rows.FirstOrDefault(r => r.Key.Equals(s, StringComparison.OrdinalIgnoreCase))
                  ?? _rows.Where(r => s.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(r => r.Key.Length)
                          .FirstOrDefault();
        }

        return row is null ? null : new OrderInfo
        {
            Serial = s,
            OrderNo = row.OrderNo,
            Line = row.Line,
            Model = row.Model,
        };
    }

    private void EnsureLoaded()
    {
        var fi = new FileInfo(FilePath);
        if (!fi.Exists)
        {
            lock (_gate) { _rows = []; _loadedStamp = DateTime.MinValue; _loadedLength = -1; }
            return;
        }
        if (fi.LastWriteTimeUtc == _loadedStamp && fi.Length == _loadedLength) return;

        var rows = new List<MasterRow>();
        string[] lines;
        try
        {
            lines = File.ReadAllLines(FilePath, Encoding.UTF8);
        }
        catch (IOException)
        {
            return; // file đang được ghi, giữ dữ liệu cũ
        }

        char sep = ',';
        if (lines.Length > 0 && lines[0].Contains(';') && !lines[0].Contains(',')) sep = ';';

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cells = SplitCsv(line, sep);
            if (cells.Count < 4) continue;

            // Bỏ dòng tiêu đề
            if (i == 0 && (cells[0].Equals("Key", StringComparison.OrdinalIgnoreCase)
                        || cells[0].Contains("serial", StringComparison.OrdinalIgnoreCase)))
                continue;

            rows.Add(new MasterRow(cells[0].Trim(), cells[1].Trim(), cells[2].Trim(), cells[3].Trim()));
        }

        lock (_gate)
        {
            _rows = rows;
            _loadedStamp = fi.LastWriteTimeUtc;
            _loadedLength = fi.Length;
        }
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == sep) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
