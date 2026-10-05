using System.Globalization;
using System.IO;
using System.Text;
using BeltTensionMeasurement.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using QRCoder;

namespace BeltTensionMeasurement.Services;

/// <summary>Báo cáo A4 ngang, phân trang tự động, phông tiếng Việt và QR đã lưu của từng kết quả.</summary>
public static class PdfExporter
{
    public static void Export(IReadOnlyList<MeasurementResult> results, string path, string rangeText, string companyLabel = "")
    {
        if (results.Count == 0) throw new ArgumentException("Không có kết quả để xuất PDF.", nameof(results));
        var document = new Document();
        document.Info.Title = "Báo cáo kết quả đo khoảng cách";
        document.Info.Author = companyLabel;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 8;
        normal.ParagraphFormat.SpaceAfter = 0;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromMillimeter(10);
        section.PageSetup.TopMargin = Unit.FromMillimeter(12);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(14);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(6);

        var title = section.AddParagraph("BÁO CÁO KẾT QUẢ ĐO KHOẢNG CÁCH");
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.FromRgb(24, 61, 101);
        title.Format.SpaceAfter = Unit.FromMillimeter(3);
        if (!string.IsNullOrWhiteSpace(companyLabel)) section.AddParagraph(companyLabel);
        section.AddParagraph("Bộ lọc: " + rangeText);
        int ok = results.Count(r => r.IsOk);
        section.AddParagraph($"Tổng: {results.Count}    |    OK: {ok}    |    NG: {results.Count - ok}    |    Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
            .Format.SpaceAfter = Unit.FromMillimeter(4);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText("Belt Tension Measurement  •  Trang ");
        footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();

        var table = section.AddTable();
        table.Borders.Width = 0.35;
        table.Borders.Color = Color.FromRgb(210, 220, 232);
        table.Rows.LeftIndent = 0;
        table.TopPadding = table.BottomPadding = Unit.FromMillimeter(2);
        table.Rows.VerticalAlignment = VerticalAlignment.Center;
        string[] headings = ["No", "Serial", "Dòng hàng", "Quy cách (mm)", "Lực căng (gf)", "Giá trị (mm)", "Kết quả", "Thời gian", "MSNV", "Mục đích đo", "QR"];
        double[] widths = [12, 40, 22, 24, 20, 20, 17, 32, 23, 35, 32]; // 277 mm = A4 ngang trừ lề.
        foreach (double width in widths) table.AddColumn(Unit.FromMillimeter(width));
        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Color.FromRgb(24, 61, 101);
        header.Format.Font.Color = Colors.White;
        header.Format.Font.Bold = true;
        for (int c = 0; c < headings.Length; c++) header.Cells[c].AddParagraph(headings[c]);

        foreach (var result in results)
        {
            var row = table.AddRow();
            string[] values = [result.No.ToString(CultureInfo.InvariantCulture), result.Serial, result.Model,
                result.SpecText, result.Force.ToString("0.00", CultureInfo.InvariantCulture),
                result.Value.ToString("0.00", CultureInfo.InvariantCulture), result.ResultText,
                result.InspectedAt.ToString("dd/MM/yyyy\nHH:mm:ss", CultureInfo.InvariantCulture), result.Inspector, result.Purpose ?? ""];
            for (int c = 0; c < values.Length; c++) row.Cells[c].AddParagraph(WrapLongWords(values[c]));
            row.Cells[6].Format.Font.Bold = true;
            row.Cells[6].Format.Font.Color = result.IsOk ? Color.FromRgb(22, 130, 53) : Color.FromRgb(198, 40, 40);
            row.Cells[6].Format.Alignment = ParagraphAlignment.Center;
            if (!string.IsNullOrWhiteSpace(result.QrText))
            {
                using var data = QRCodeGenerator.GenerateQrCode(result.QrText, QRCodeGenerator.ECCLevel.M);
                using var qr = new PngByteQRCode(data);
                var paragraph = row.Cells[10].AddParagraph();
                paragraph.Format.Alignment = ParagraphAlignment.Center;
                var image = paragraph.AddImage("base64:" + Convert.ToBase64String(qr.GetGraphic(6)));
                image.Width = Unit.FromMillimeter(28);
                image.LockAspectRatio = true;
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        // Ghi tệp tạm cùng thư mục, chỉ thay file đích sau khi PDF hoàn tất.
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (renderer.PdfDocument) renderer.Save(temporaryPath);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static string WrapLongWords(string? text)
    {
        var builder = new StringBuilder();
        int run = 0;
        foreach (char c in text ?? "")
        {
            if (char.IsWhiteSpace(c)) run = 0;
            else if (run++ == 14) { builder.Append('\u200B'); run = 1; }
            builder.Append(c);
        }
        return builder.ToString();
    }
}
