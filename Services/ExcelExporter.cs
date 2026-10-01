using ClosedXML.Excel;
using System.IO;
using QRCoder;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services;

/// <summary>Xuất bảng ExportResultData ra file .xlsx (ClosedXML, không cần cài Excel).</summary>
public static class ExcelExporter
{
    private static readonly string[] Headers =
    [
        "No", "Chủng loại", "Mã quét (Serial)", "Hạng mục kiểm tra",
        "Quy cách", "Lực căng (gf)", "Giá trị (mm)", "Kết quả", "Ngày kiểm tra", "Người kiểm tra", "Máy tính", "Ghi chú", "QR",
    ];

    public static void Export(IReadOnlyList<MeasurementResult> results, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ExportResultData");

        for (int c = 0; c < Headers.Length; c++)
            ws.Cell(1, c + 1).Value = Headers[c];

        var header = ws.Range(1, 1, 1, Headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#9BBB59");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Height = 30;

        int row = 2;
        foreach (var r in results)
        {
            ws.Cell(row, 1).Value = r.No;
            ws.Cell(row, 2).Value = r.Model;
            ws.Cell(row, 3).Value = r.Serial;
            ws.Cell(row, 4).Value = r.ItemName;
            ws.Cell(row, 5).Value = r.SpecText;
            ws.Cell(row, 6).Value = r.Force;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.00";
            ws.Cell(row, 7).Value = r.Value;
            ws.Cell(row, 7).Style.NumberFormat.Format = "0.00";
            ws.Cell(row, 8).Value = r.ResultText;
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = r.IsOk ? XLColor.FromHtml("#C6EFCE") : XLColor.FromHtml("#FFC7CE");
            ws.Cell(row, 8).Style.Font.FontColor = r.IsOk ? XLColor.FromHtml("#006100") : XLColor.FromHtml("#9C0006");
            ws.Cell(row, 9).Value = r.InspectedAt;
            ws.Cell(row, 9).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
            ws.Cell(row, 10).Value = r.Inspector;
            ws.Cell(row, 11).Value = r.PcName;
            ws.Cell(row, 12).Value = r.Note;
            ws.Cell(row, 13).Value = r.QrText ?? "";
            if (!string.IsNullOrWhiteSpace(r.QrText))
            {
                using var data = QRCodeGenerator.GenerateQrCode(r.QrText, QRCodeGenerator.ECCLevel.M);
                using var qr = new PngByteQRCode(data);
                using var stream = new MemoryStream(qr.GetGraphic(6));
                // Giữ ít nhất hai pixel/module để mã dài cũng quét được khi mở Excel.
                int side = Math.Max(128, data.ModuleMatrix.Count * 2);
                ws.AddPicture(stream).MoveTo(ws.Cell(row, 13), 4, 4).WithSize(side, side);
                ws.Row(row).Height = (side + 52) * 0.75; // Excel dùng point; ảnh dùng pixel.
                ws.Cell(row, 13).Style.Alignment.Vertical = XLAlignmentVerticalValues.Bottom;
                ws.Cell(row, 13).Style.Alignment.WrapText = true;
            }
            row++;
        }

        if (row > 2)
        {
            var body = ws.Range(2, 1, row - 1, Headers.Length);
            body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            body.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        }

        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, row - 1), Headers.Length).SetAutoFilter();
        ws.Columns(1, 12).AdjustToContents();
        int qrSide = ws.Pictures.Any() ? ws.Pictures.Max(p => p.Width) : 128;
        ws.Column(13).Width = Math.Max(26, (qrSide + 16) / 7.0);
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 20);
        ws.Column(12).Width = Math.Max(ws.Column(12).Width, 24);

        wb.SaveAs(path);
    }
}
