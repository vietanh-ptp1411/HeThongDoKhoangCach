using ClosedXML.Excel;
using HeThongDoKhoangCach.Models;

namespace HeThongDoKhoangCach.Services;

/// <summary>Xuất bảng ExportResultData ra file .xlsx (ClosedXML, không cần cài Excel).</summary>
public static class ExcelExporter
{
    private static readonly string[] Headers =
    [
        "No", "Dòng hàng", "Line", "Chủng loại", "Mã quét (Serial)", "Hạng mục kiểm tra",
        "Quy cách", "Lực căng (gf)", "Giá trị (mm)", "Kết quả", "Ngày kiểm tra", "Người kiểm tra", "Máy tính", "Ghi chú",
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
            ws.Cell(row, 2).Value = r.OrderNo;
            ws.Cell(row, 3).Value = r.Line;
            ws.Cell(row, 4).Value = r.Model;
            ws.Cell(row, 5).Value = r.Serial;
            ws.Cell(row, 6).Value = r.ItemName;
            ws.Cell(row, 7).Value = r.SpecText;
            ws.Cell(row, 8).Value = r.Force;
            ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";
            ws.Cell(row, 9).Value = r.Value;
            ws.Cell(row, 9).Style.NumberFormat.Format = "0.00";
            ws.Cell(row, 10).Value = r.ResultText;
            ws.Cell(row, 10).Style.Font.Bold = true;
            ws.Cell(row, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 10).Style.Fill.BackgroundColor = r.IsOk ? XLColor.FromHtml("#C6EFCE") : XLColor.FromHtml("#FFC7CE");
            ws.Cell(row, 10).Style.Font.FontColor = r.IsOk ? XLColor.FromHtml("#006100") : XLColor.FromHtml("#9C0006");
            ws.Cell(row, 11).Value = r.InspectedAt;
            ws.Cell(row, 11).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
            ws.Cell(row, 12).Value = r.Inspector;
            ws.Cell(row, 13).Value = r.PcName;
            ws.Cell(row, 14).Value = r.Note;
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
        ws.Columns().AdjustToContents();
        ws.Column(5).Width = Math.Max(ws.Column(5).Width, 20);
        ws.Column(14).Width = Math.Max(ws.Column(14).Width, 24);

        wb.SaveAs(path);
    }
}
