# Hệ thống đo khoảng cách – PLC & Loadcell

Ứng dụng WPF (.NET 10) hiển thị lực căng (loadcell) và khoảng cách đo được từ PLC,
tra thông tin đơn hàng theo số serial, phân định PASS/NG theo quy cách từng chủng loại,
vẽ histogram theo nhóm, trend chart theo line, lưu kết quả, báo cáo/thống kê và xuất Excel
(`ExportResultData`). Giao diện bám theo bản mock của BISG trong tài liệu
"PHẢN HỒI NHÀ CUNG CẤP MC".

## Chạy ứng dụng

```powershell
dotnet build
dotnet run
```

Mặc định ứng dụng chạy ở **chế độ mô phỏng** (không cần PLC): nhấn **START**, PLC giả sẽ tự "scan" serial,
đo, phân định và đếm từng sản phẩm; phần mềm hiển thị và ghi lịch sử để vẽ histogram, trend chart.

## Màn hình chính

| Khu vực | Nội dung |
|---------|----------|
| Tiêu đề | Nút Home (đưa con trỏ về ô scan), tên hệ thống, ngày, giờ |
| LOADCELL / KHOẢNG CÁCH | Giá trị thời gian thực đọc từ PLC |
| THÔNG TIN ĐƠN HÀNG | Serial và bảng DÒNG HÀNG / SỐ MÁY / LINE / CHỦNG LOẠI đọc từ PLC (hoặc scan tại máy tính + file master nếu PLC không gửi); dòng nhắc đỏ về trạng thái |
| QUY CÁCH / KẾT QUẢ | Giới hạn của chủng loại và giá trị đo được |
| PHÂN ĐỊNH | PASS (xanh) / NG (đỏ) / ĐANG CHỜ (vàng) – theo bit OK/NG của PLC |
| GHI CHÚ | Ghi chú lưu kèm kết quả đo |
| START / STOP / RESET | Gửi lệnh tới PLC |
| HISTOGRAM | Một biểu đồ cho mỗi **nhóm** chủng loại (cột Nhóm trong cài đặt quy cách). Nút RESET chỉ xóa dữ liệu hiển thị của biểu đồ đó, kết quả đã lưu vẫn giữ |
| TREND CHART | Trung bình theo ngày của từng Line trong N ngày gần nhất có dữ liệu, kèm giới hạn trên/dưới |
| LƯU DỮ LIỆU | Lưu thủ công (khi tắt tự lưu) |
| XUẤT DỮ LIỆU | Xuất toàn bộ kết quả ra Excel |
| CÀI ĐẶT | Giao thức PLC, IP/port, bảng địa chỉ, quy cách, file master, người kiểm tra... |
| THỐNG KÊ | OK/NG, tỉ lệ, trung bình, min/max, độ lệch chuẩn, Cpk theo chủng loại và theo line |
| BÁO CÁO | Lưới kết quả có bộ lọc (ngày, line, chủng loại, kết quả, từ khóa) và xuất Excel |
| ĐĂNG NHẬP | Đặt mã người kiểm tra (cột Người KT) |
| Thanh trạng thái | PLC, LOADCELL, TOTAL/OK/NG (bộ đếm của PLC), người KT, giao thức, phiên bản |

Quy trình vận hành (PLC làm toàn bộ, phần mềm hiển thị):

1. **START** → phần mềm ghi bit Start xuống PLC và hiển thị trạng thái chạy.
2. Người vận hành scan serial **vào PLC** (máy quét nối PLC). PLC điền serial, dòng hàng, line, chủng loại,
   quy cách vào thanh ghi → phần mềm đọc lên bảng THÔNG TIN ĐƠN HÀNG và ô QUY CÁCH.
3. PLC đo, ghi kết quả, phân định OK/NG, tăng bộ đếm rồi **bật bit Đo xong**. Phần mềm bắt sườn lên,
   hiển thị KẾT QUẢ, PASS/NG, TOTAL/OK/NG và ghi một dòng vào lịch sử (để vẽ histogram, trend, báo cáo, Excel).
4. **STOP** / **RESET** chỉ ghi bit lệnh xuống PLC.

Ô nào PLC không cung cấp (để trống địa chỉ trong CÀI ĐẶT) thì phần mềm tự bù: serial gõ/scan tại máy tính,
tra file master để lấy đơn hàng/line/chủng loại, quy cách theo bảng chủng loại, tự so quy cách để ra OK/NG,
tự đếm theo lịch sử đã lưu.

Ở chế độ mô phỏng, sau START PLC giả tự "scan" một serial, đo trong 1,5 giây, phân định và đếm; sản phẩm
kế tiếp sau 6 giây.

## Truyền thông PLC

Hai giao thức được cài đặt trực tiếp trong mã nguồn (không phụ thuộc thư viện ngoài):

| Giao thức | Chi tiết | Cú pháp địa chỉ |
|-----------|----------|-----------------|
| **MC Protocol (SLMP) 3E frame, Binary, TCP** | Lệnh 0401/1401 đọc/ghi word và bit theo khối. Dùng cho Mitsubishi Q / L / iQ-R / iQ-F (FX5U) và FX3U + module Ethernet cấu hình *MC protocol, 3E frame, Binary*. | `D100`, `R200`, `ZR100`, `W1A0` (hex), `M100`, `L10`, `B1F` (hex), `X10` / `Y20` (hex), `SM`, `SD`, `TN`, `CN`… |
| **Modbus TCP** | FC01/02/03/04/05/06/16. Unit ID cấu hình được. | `HR100` (holding), `IR100` (input register), `C100` (coil), `DI100` (discrete input) hoặc dạng số `40101` / `30101` / `00101` / `10101` |

### Bảng tag (địa chỉ mặc định MC / Modbus)

| Nhóm | Tag | Kiểu | MC | Modbus | Ghi chú |
|------|-----|------|----|--------|---------|
| Giá trị đo | Lực căng (Loadcell) * | Float32 | D100 | HR100 | Hiển thị liên tục |
| Giá trị đo | Khoảng cách * | Float32 | D102 | HR102 | Hiển thị liên tục |
| Giá trị đo | Kết quả đo chốt | Float32 | D104 | HR104 | Trống → lấy khoảng cách tại lúc Đo xong |
| Quy cách | LSL / USL | Float32 | D106 / D108 | HR106 / HR108 | Trống → tra theo chủng loại trong Cài đặt |
| Đơn hàng | Serial (Số máy) | String 10 word | D200 | HR200 | Trống → nhập/scan tại máy tính |
| Đơn hàng | Dòng hàng | String 10 word | D210 | HR210 | Trống → tra file master |
| Đơn hàng | Line | String 5 word | D220 | HR220 | Trống → tra file master |
| Đơn hàng | Chủng loại | String 10 word | D225 | HR225 | Trống → tra file master |
| Phân định | Kết quả OK / NG | Bit | M102 / M103 | C102 / C103 | Trống cả hai → phần mềm tự so quy cách |
| Bộ đếm | TOTAL / OK / NG | Int32 | D110 / D112 / D114 | HR110 / HR112 / HR114 | Trống → đếm theo lịch sử |
| Trạng thái | Loadcell ổn định | Bit | M100 | C100 | Tùy chọn |
| Trạng thái | Đo xong * | Bit | M101 | C101 | Sườn lên = kết quả mới, giữ ≥ 1 chu kỳ đọc, hạ trước lần đo sau |
| Trạng thái | PLC đang chạy | Bit | M104 | C104 | Trống → theo nút START/STOP trên phần mềm |
| Lệnh | START * / STOP / RESET | Bit | M110 / M111 / M112 | C110 / C111 / C112 | Phần mềm ghi mức 1, PLC tự xóa |

(*) bắt buộc. Mỗi tag đổi được **kiểu dữ liệu** (Bit, Int16, UInt16, Int32, UInt32, Float32, String),
**độ dài** (số word cho String), **hệ số** (PLC lưu 370 → hệ số 0.01 để được 3.70) và **thứ tự word**.
Chuỗi lưu ASCII 2 ký tự/word; với Mitsubishi ($MOV) byte thấp là ký tự trước (LowHigh), Modbus thường HighLow.

### Yêu cầu phía PLC

- Trước khi bật bit Đo xong, PLC phải ghi xong kết quả đo, OK/NG, bộ đếm và các chuỗi đơn hàng
  (phần mềm đọc tất cả ngay tại sườn lên).
- Bit Đo xong giữ mức 1 ít nhất một chu kỳ đọc (mặc định 250 ms, nên ≥ 500 ms) và hạ về 0 trước lần đo kế tiếp.
- PLC tự xóa 3 bit lệnh START/STOP/RESET sau khi xử lý.
- Kết nối Ethernet, IP tĩnh cùng mạng máy tính; MC Protocol cấu hình *3E frame, Binary* (port ví dụ 5000)
  hoặc Modbus TCP (port 502, Unit ID).

## File master (BISG)

Chỉ dùng khi PLC không gửi thông tin đơn hàng. `Data\master.csv` – CSV UTF-8, cột `Key,OrderNo,Line,Model`. `Key` là serial đầy đủ hoặc tiền tố
serial (ví dụ `985X57200` khớp với `985X57200-00123`). Khớp chính xác được ưu tiên, sau đó là tiền tố
dài nhất. File được nạp lại tự động khi thay đổi, có thể trỏ tới file dùng chung trên mạng.
Trên màn hình: DÒNG HÀNG = OrderNo, SỐ MÁY = serial đã scan, LINE = Line, CHỦNG LOẠI = Model.

## Dữ liệu kết quả

- `Data\results.jsonl`: mỗi dòng một kết quả (JSON Lines, chỉ ghi thêm).
- Xuất Excel: sheet `ExportResultData` với các cột No, Dòng hàng, Line, Chủng loại, Mã quét (Serial),
  Hạng mục kiểm tra, Quy cách, Lực căng (gf), Giá trị (mm), Kết quả, Ngày kiểm tra, Người kiểm tra,
  Máy tính, Ghi chú.
- `appsettings.json` cạnh file exe: toàn bộ cài đặt (kể cả thời điểm RESET từng histogram).

## Cấu trúc mã nguồn

```
Models/            AppSettings, PlcSettings, TagDefinition, ModelSpec, MeasurementResult, OrderInfo, TrendSeries
Services/Plc/      IPlcClient, TcpPlcClientBase, McProtocolClient, ModbusTcpClient,
                   SimulationPlcClient, TagCodec, PlcClientFactory
Services/          PlcMonitor (poll + reconnect), MasterDataService, ResultStore,
                   ExcelExporter (ClosedXML), SettingsService
ViewModels/        MainViewModel, SettingsViewModel, ReportViewModel, StatisticsViewModel, Commands, Converters
Controls/          HistogramControl, TrendChartControl, ChartMath (vẽ bằng DrawingContext)
Views/             SettingsWindow, ReportWindow, StatisticsWindow, LoginWindow
MainWindow.xaml    Màn hình chính theo mock BISG
```
