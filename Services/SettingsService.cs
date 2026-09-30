using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services;

/// <summary>Đọc/ghi appsettings.json cạnh file exe.</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppSettings Load()
    {
        AppSettings? settings = null;
        if (File.Exists(FilePath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8), JsonOptions);
            }
            catch (JsonException)
            {
                // File hỏng: giữ bản sao để người dùng xem lại, dùng mặc định.
                try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch { /* bỏ qua */ }
            }
        }

        settings ??= new AppSettings();
        settings.Plc ??= new PlcSettings();
        if (settings.HistogramBins < 4) settings.HistogramBins = 24;
        if (settings.Plc.PollIntervalMs < 50) settings.Plc.PollIntervalMs = 200;
        settings.Plc.SpecLslWrite ??= new TagDefinition { DataType = TagDataType.Float32 };
        settings.Plc.SpecUslWrite ??= new TagDefinition { DataType = TagDataType.Float32 };

        // Tiêu đề mặc định của bản trước → tiêu đề theo mock mới của khách.
        if (string.Equals(settings.Title, AppSettings.LegacyDefaultTitle, StringComparison.Ordinal))
            settings.Title = new AppSettings().Title;

        if (!File.Exists(FilePath))
        {
            try { Save(settings); } catch { /* thư mục chỉ đọc – bỏ qua */ }
        }
        return settings;
    }

    public static void Save(AppSettings settings)
        => File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);

    public static AppSettings Clone(AppSettings settings)
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions) ?? new AppSettings();

    /// <summary>Đường dẫn tương đối được tính từ thư mục chứa exe.</summary>
    public static string ResolvePath(string path)
        => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
