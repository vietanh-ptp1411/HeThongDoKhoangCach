using System.Text;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services.Plc;

/// <summary>Chuyển đổi giữa word thô của PLC và giá trị số/chuỗi theo <see cref="TagDefinition"/>.</summary>
public static class TagCodec
{
    public static double Decode(TagDefinition tag, ushort[] words)
    {
        if (tag.DataType == TagDataType.String)
            throw new PlcException($"Tag {tag.Address} là chuỗi, hãy dùng DecodeString");
        if (words.Length < tag.WordCount)
            throw new PlcException($"Tag {tag.Address}: cần {tag.WordCount} word, chỉ nhận {words.Length}");

        // Ép từng nhánh về double: nếu không, switch expression sẽ chọn kiểu chung là float
        // và làm mất độ chính xác của Int32/UInt32 lớn.
        double raw = tag.DataType switch
        {
            TagDataType.Int16 => (double)(short)words[0],
            TagDataType.UInt16 => (double)words[0],
            TagDataType.Int32 => (double)(int)Combine(tag.WordOrder, words),
            TagDataType.UInt32 => (double)Combine(tag.WordOrder, words),
            TagDataType.Float32 => (double)BitConverter.Int32BitsToSingle((int)Combine(tag.WordOrder, words)),
            TagDataType.Bit => words[0] != 0 ? 1.0 : 0.0,
            _ => throw new PlcException($"Kiểu dữ liệu không hỗ trợ: {tag.DataType}"),
        };
        return raw * tag.Scale;
    }

    public static ushort[] Encode(TagDefinition tag, double value)
    {
        double raw = tag.Scale == 0 ? value : value / tag.Scale;
        return tag.DataType switch
        {
            TagDataType.Int16 => [(ushort)(short)Math.Round(raw)],
            TagDataType.UInt16 => [(ushort)Math.Round(raw)],
            TagDataType.Bit => [(ushort)(raw != 0 ? 1 : 0)],
            TagDataType.Int32 => Split(tag.WordOrder, (uint)(int)Math.Round(raw)),
            TagDataType.UInt32 => Split(tag.WordOrder, (uint)Math.Round(raw)),
            TagDataType.Float32 => Split(tag.WordOrder, (uint)BitConverter.SingleToInt32Bits((float)raw)),
            _ => throw new PlcException($"Kiểu dữ liệu không hỗ trợ: {tag.DataType}"),
        };
    }

    /// <summary>
    /// Giải mã chuỗi ASCII từ dãy word: mỗi word 2 ký tự. LowHigh = byte thấp là ký tự trước (Mitsubishi $MOV),
    /// HighLow = byte cao là ký tự trước (thường gặp ở Modbus). Dừng ở ký tự NUL, bỏ khoảng trắng hai đầu.
    /// </summary>
    public static string DecodeString(TagDefinition tag, ushort[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (int i = 0; i < words.Length; i++)
        {
            byte lo = (byte)words[i], hi = (byte)(words[i] >> 8);
            if (tag.WordOrder == WordOrder.LowHigh) { bytes[2 * i] = lo; bytes[2 * i + 1] = hi; }
            else { bytes[2 * i] = hi; bytes[2 * i + 1] = lo; }
        }
        int len = Array.IndexOf(bytes, (byte)0);
        if (len < 0) len = bytes.Length;
        // Loại ký tự điều khiển (một số PLC đệm bằng 0x20 hoặc 0xFF)
        var text = Encoding.Latin1.GetString(bytes, 0, len);
        return new string(text.Where(c => c >= 0x20 && c < 0x7F).ToArray()).Trim();
    }

    /// <summary>Mã hóa chuỗi thành đúng <see cref="TagDefinition.WordCount"/> word (đệm NUL).</summary>
    public static ushort[] EncodeString(TagDefinition tag, string text)
    {
        int wordCount = tag.WordCount;
        var bytes = new byte[wordCount * 2];
        var src = Encoding.ASCII.GetBytes(text ?? "");
        Array.Copy(src, bytes, Math.Min(src.Length, bytes.Length));

        var words = new ushort[wordCount];
        for (int i = 0; i < wordCount; i++)
        {
            byte first = bytes[2 * i], second = bytes[2 * i + 1];
            words[i] = tag.WordOrder == WordOrder.LowHigh
                ? (ushort)(first | second << 8)
                : (ushort)(second | first << 8);
        }
        return words;
    }

    private static uint Combine(WordOrder order, ushort[] w) =>
        order == WordOrder.LowHigh
            ? (uint)(w[0] | w[1] << 16)
            : (uint)(w[1] | w[0] << 16);

    private static ushort[] Split(WordOrder order, uint v)
    {
        ushort lo = (ushort)v, hi = (ushort)(v >> 16);
        return order == WordOrder.LowHigh ? [lo, hi] : [hi, lo];
    }
}
