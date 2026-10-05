# Belt Tension Measurement — Hệ thống đo lực căng Belt

Tên project/namespace: `BeltTensionMeasurement`. Mở `BeltTensionMeasurement.slnx` trong Visual Studio;
file thực thi sau build là `BeltTensionMeasurement.exe`.
Thư mục workspace hiện tại: `D:\BeltTensionMeasurement`.

Ứng dụng WPF (.NET 10) theo **003.pdf**: chọn quy cách → dòng hàng (model) → scan serial và MSNV →
chọn/scan mục đích → bấm START trên phần mềm → nhận một kết quả, OK/NG và QR.

## Chạy và thử mô phỏng

```powershell
dotnet build
dotnet run --project BeltTensionMeasurement.csproj
```

Chọn `3.5 ~ 6 mm` → `CPX`, bấm ô Serial rồi quét/gõ mã + Enter; bấm ô MSNV rồi quét mã nhân viên + Enter; chọn mục đích hoặc bấm ô Quét mục đích và quét `1` + Enter.
Bấm **START** bằng chuột: App gửi model, quy cách, mục đích nếu cấu hình rồi ghi bit START xuống PLC.
Cùng cách bấm này áp dụng cho cả máy thật và mô phỏng; mỗi lần bấm đo một lần, không chờ bit START từ PLC.

## Trình tự theo mục 3–4 của 003.pdf

1. Chọn quy cách và **Dòng hàng** (model). Chỉ hiện model thuộc quy cách; không dùng file master/đơn hàng/line.
2. Bấm ô **Serial** rồi quét serial; bấm ô **MSNV** rồi quét mã nhân viên. Enter xác nhận mã và chọn sẵn nội dung để lần quét sau thay thế.
   Khi nhập tay, không bắt buộc Enter: điền Serial/MSNV hợp lệ và chọn mục đích rồi bấm START; App chốt đúng các giá trị đang hiển thị.
   Nếu START bị khóa, dòng hướng dẫn và tooltip của nút nêu lý do cụ thể (PLC, mục đích, dữ liệu nhập hoặc trạng thái máy).
   Không tự chuyển ô hoặc tự bắt mã từ bảng/nút. Có thể nhập hai ô theo thứ tự bất kỳ; sửa ô này giữ nguyên ô kia và mục đích đã chọn.
3. Chọn **Mục đích đo của sản phẩm** từ danh sách hoặc bấm ô **Quét mục đích (1–5)** rồi quét barcode:

| Mã barcode | Mục đích |
|---|---|
| 1 | Đo kiểm tra đặc thù hằng ngày |
| 2 | Đo khởi đầu công việc |
| 3 | Điều tra lỗi |
| 4 | Yêu cầu các phòng ban |
| 5 | Khác |

Barcode nhận `1`..`5`, `PURPOSE:1`..`PURPOSE:5`, tên mục đích hoặc tên có số thứ tự như trong danh sách.
Đây là quy ước triển khai vì PDF chưa có mẫu nội dung barcode mục đích của khách.
Không suy đoán MSNV hay mục đích từ hình dạng serial. Mã không hợp lệ phải được quét lại/chọn lại.

4. Bấm nút **START** trên phần mềm. Chỉ bật nút khi đủ serial, MSNV, mục đích, model, PLC online và không còn lượt đo/kết quả chờ lưu/hộp thoại đang mở.
5. App gửi bit model, giới hạn nếu cấu hình, mã mục đích nếu cấu hình; cuối cùng ghi bit START. PLC nhận lệnh và đo một lần.
6. Chốt một kết quả, MSNV và mục đích theo thông tin tại lúc chấp nhận START. Hiển thị giá trị ở trên, **OK/NG lớn bên trái, QR bên phải** như PDF.
   Có bit OK/NG thì nhận phân định PLC; để trống cả hai thì so với LSL/USL (bao gồm hai biên).
   QR mặc định chỉ chứa giá trị đo như `3.70`. Kết quả lưu kèm MSNV, mục đích và QR; báo cáo có bộ lọc mục đích, Excel có cột Mục đích đo.
7. Lượt tiếp theo bấm ô Serial và scan lại. MSNV và mục đích giữ nguyên; bấm ô tương ứng nếu muốn đổi. STOP hủy lượt đang chạy; RESET xóa thông tin của lượt đang chuẩn bị.

Kết quả **luôn tự động lưu** ngay khi có OK/NG, kể cả cấu hình cũ từng tắt tự lưu. Nếu ghi file lỗi, giữ kết quả
và tự thử lại mỗi 5 giây; tạm khóa lượt mới, RESET và đóng cửa sổ cho đến khi lưu thành công.
Kiểm tra quyền ghi, dung lượng ổ đĩa hoặc file đang bị chương trình khác khóa khi có lỗi lưu.
Khi mất kết nối, hủy lượt chưa hoàn tất;
sau khi nối lại, kiểm tra thông tin và bấm START để đo lại. Dữ liệu PLC không tự khởi động lượt đo.

## Lệnh START từ App tới PLC

- Người vận hành bấm START bằng chuột trên màn hình. App gửi bit model, giới hạn và mục đích (nếu cấu hình), sau đó ghi StartCommand=1.
- StartCommand mặc định `M0` (MC) / `C0` (Modbus). PLC tự xóa lệnh sau khi xử lý. Không còn đọc StartRequest/M105/C105.
- STOP/RESET có thể hủy lượt đang chuẩn bị/chạy. Lỗi gửi dữ liệu hoặc mất kết nối sẽ không tiếp tục gửi lệnh START.
- Serial và MSNV lưu trong App; PurposeWrite tùy chọn ghi mã mục đích 1..5. Địa chỉ PLC cần khớp chương trình tại máy.

## Danh mục quy cách và model

Database mặc định: `Data/HeThongDo.db`, đường dẫn tính từ thư mục chứa file chạy. Tự tạo ở lần đầu.
Hai bảng: `QuyCach` (LSL, USL, thứ tự, đơn vị, điều kiện) và `Model` (quy cách cha, tên, thứ tự, bit PLC).

| Quy cách | Model | Bit PLC mặc định (MC) |
|---|---|---|
| 3 ~ 4 mm | LCP | M45 |
| 4 ~ 5 mm | M1, M2, NF, PP1 | M42, M42, M43, M41 |
| 3.5 ~ 6 mm | GSM, CPX | M40, M44 |

Điều kiện mặc định theo PDF: **Tác động lực 2gf**. Bit model kế thừa bảng địa chỉ cũ;
LCP dùng M45. Cần đối chiếu với chương trình PLC tại máy. Modbus dùng coil C thay cho M.

Màn **CÀI ĐẶT** có 5 mục ở thanh bên trái: **Kết nối PLC**, **Địa chỉ PLC**, **Quy cách & model**,
**QR kết quả**, **Thông tin & dữ liệu**. Chọn trực tiếp mục cần sửa; nút Lưu/Hủy luôn nằm ở cuối cửa sổ.
Trong **Quy cách & model**, chọn dòng quy cách bên trái rồi thêm/sửa/xóa model ở bảng bên phải.
**Lưu & áp dụng** ghi toàn bộ danh mục trong một transaction. **Hủy** không lưu các thay đổi danh mục.
Cho phép M1/M2 dùng chung bit trong cùng quy cách, không cho cùng bit điều khiển hai quy cách khác nhau.
Giới hạn phải hợp lệ, model không trống/trùng trong một quy cách. Quy cách và model được nhớ cho lần mở tiếp theo;
serial cần scan lại cho mỗi lượt đo mới. Cấu hình `DatabaseFilePath` trong `appsettings.json`
nếu cần đặt database ở vị trí khác.

## Máy quét serial Zebra DS8178

Máy đọc serial đầu vào: Zebra DS8178 + đế thu USB. Xem [Quick Start Guide chính thức của Zebra](https://www.zebra.com/content/dam/support-dam/en/documentation/unrestricted/guide/product/ds8178-qsg-en.pdf):
trang 5 là kết nối cáp đế thu, trang 6 có mã cấu hình **USB Keyboard HID**, trang 10 có mã **Add Enter Key (Carriage Return/Line Feed)**.
Nếu đọc được nhãn nhưng máy tính không nhận, kiểm tra ghép cặp máy quét với đế (trang 13).
Thử Notepad: serial phải hiện đủ và xuống dòng sau khi quét. Phần mềm dùng đầu vào bàn phím nên không cần chọn cổng COM.

## QR kết quả

Mặc định QR chứa **`3.70`** khi giá trị đo là 3.70 mm. PDF chưa quy định chi tiết payload phía Brother,
nên mẫu có thể thay đổi trong **CÀI ĐẶT → QR kết quả**, có xem trước nội dung ngay khi nhập.

- `{Value}`: giá trị có hai chữ số thập phân, dấu chấm.
- `{Serial};{Model};{Value};{Result}`: ví dụ `985X57200-00123;CPX;3.70;OK`.
- Các trường khác: `{Spec}`, `{Lsl}`, `{Usl}`, `{Force}`, `{Date}`, `{Time}`, `{DateTime}`, `{Inspector}`, `{Purpose}`, `{PurposeCode}`.

Mã QR vẽ đen/trắng, có vùng trắng bao quanh, căn theo pixel. Chỉ sinh QR khi có kết quả mới hoàn chỉnh;
scan serial khác hoặc RESET sẽ xóa QR cũ. Nội dung quá dài sẽ báo lỗi thay vì hiển thị mã không đọc được.
Nội dung QR được lưu cùng từng kết quả đo. Trong **BÁO CÁO**, cột **QR** hiển thị ảnh mã và nội dung;
rê chuột để xem mã phóng to. Có thể tìm kiếm theo nội dung QR. Đổi mẫu QR trong Cài đặt không thay đổi mã đã lưu.
Mẫu QR cũ có `{OrderNo}` hoặc `{Line}` được trả về `{Value}` khi nạp cài đặt. QR đã lưu trong lịch sử vẫn giữ nguyên.
File Excel cũng có cột **QR**, gồm ảnh mã và nội dung dạng chữ. Các bản ghi cũ chưa lưu QR để trống cột này.
Cần thử quét trên màn hình thực tế bằng Zebra DS8178 và xác nhận định dạng mà hệ thống Brother nhận.

## Lịch sử, biểu đồ và xuất Excel

Nút **XUẤT PDF** trên màn hình chính xuất các kết quả theo bộ lọc ngày hiện tại. PDF A4 ngang có
serial, dòng hàng, quy cách, lực căng, giá trị, OK/NG, thời gian, MSNV, mục đích đo và QR đã lưu.
Tiêu đề bảng lặp lại trên mỗi trang; số trang và tổng OK/NG được ghi trong báo cáo. Có thể chọn nơi lưu
file `.pdf` trực tiếp, không cần máy in PDF. Không có kết quả trong bộ lọc thì nút xuất bị khóa.
Trong cửa sổ **BÁO CÁO**, nút **Xuất PDF** nằm cạnh Excel và xuất đúng bảng sau khi bấm **Tìm**,
bao gồm bộ lọc ngày, dòng hàng, mục đích, OK/NG và từ khóa. Tiêu đề PDF ghi bộ lọc đã áp dụng cho bảng.

Mỗi quy cách có một histogram, gộp theo LSL/USL **đã lưu trong kết quả**. Các model cùng quy cách dùng chung biểu đồ.
Bảng kết quả, histogram và Excel tuân theo bộ lọc ngày; bộ đếm lấy từ PLC nếu cấu hình, nếu không thì đếm lịch sử đã lọc.
Thống kê tách theo model và giới hạn lịch sử để thay đổi danh mục không làm thay đổi quy cách của các lần đo cũ.
Lịch sử JSONL cũ vẫn đọc được; các giới hạn cũ không có trong danh mục mới vẫn hiện trong bảng, báo cáo và thống kê.

## Truyền thông PLC

Hai giao thức được cài đặt trực tiếp trong mã nguồn (không phụ thuộc thư viện ngoài):

| Giao thức | Chi tiết | Cú pháp địa chỉ |
|-----------|----------|-----------------|
| **MC Protocol (SLMP) 3E frame, Binary, TCP** | Lệnh 0401/1401 đọc/ghi word và bit theo khối. Dùng cho Mitsubishi Q / L / iQ-R / iQ-F (FX5U) và FX3U + module Ethernet cấu hình *MC protocol, 3E frame, Binary*. | `D100`, `R200`, `ZR100`, `W1A0` (hex), `M100`, `L10`, `B1F` (hex), `X10` / `Y20` (hex), `SM`, `SD`, `TN`, `CN`… |
| **Modbus TCP** | FC01/02/03/04/05/06/16. Unit ID cấu hình được. | `HR100` (holding), `IR100` (input register), `C100` (coil), `DI100` (discrete input) hoặc dạng số `40101` / `30101` / `00101` / `10101` |

### Bảng tag (địa chỉ mặc định MC / Modbus)

| Chiều | Nhóm | Tag | Kiểu | MC | Modbus | Ghi chú |
|-------|------|-----|------|----|--------|---------|
| PC → PLC | Lệnh | START * | Bit | M0 | C0 | Bật sau khi gửi xong dữ liệu; PLC tự xóa |
| PC → PLC | Thông tin | Mục đích đo | Int16 | (trống) | (trống) | Tùy chọn, mã 1..5 |
| PC → PLC | Lệnh | STOP | Bit | M15 | C15 | Máy tạm dừng |
| PC → PLC | Lệnh | RESET | Bit | M16 | C16 | Xóa giá trị / bộ đếm |
| PC → PLC | Chủng loại | Bit theo chủng loại | Bit | M40..M50 | C40..C50 | Mức 1 cho model đang chọn, các bit khác mức 0 (DANH MỤC trong CÀI ĐẶT) |
| PC → PLC | Quy cách | LSL / USL ghi xuống | Float32 | (trống) | (trống) | Tùy chọn, ghi khi bấm START, trước lệnh chạy |
| PLC → PC | Giá trị đo | Lực căng (Loadcell) * | Float32 | D100 | HR100 | Đọc liên tục |
| PLC → PC | Giá trị đo | Khoảng cách * | Float32 | D102 | HR102 | Đọc liên tục |
| PLC → PC | Giá trị đo | Kết quả đo (chốt) | Float32 | D104 | HR104 | Trống → lấy khoảng cách tại lúc Đo xong. Nhận kết quả theo tín hiệu hoàn tất, kể cả giá trị 0 |
| PLC → PC | Phân định | Kết quả OK / NG | Bit | M102 / M103 | C102 / C103 | Trống cả hai → phần mềm tự so quy cách |
| PLC → PC | Bộ đếm | TOTAL / OK / NG | Int32 | D110 / D112 / D114 | HR110 / HR112 / HR114 | Trống → đếm theo lịch sử đã lọc |
| PLC → PC | Trạng thái | Đo xong | Bit | M101 | C101 | Sườn lên = kết quả mới. Trống → theo TOTAL tăng; cũng trống → theo Kết quả đo thay đổi |
| PLC → PC | Trạng thái | Loadcell ổn định | Bit | M100 | C100 | Tùy chọn |
| PLC → PC | Trạng thái | Máy đang chạy | Bit | M104 | C104 | Trống → theo nút START/STOP vừa bấm |

(*) bắt buộc. Mỗi tag đổi được **kiểu dữ liệu** (Bit, Int16, UInt16, Int32, UInt32, Float32), **hệ số**
(PLC lưu 370 → hệ số 0.01 để được 3.70) và **thứ tự word**. Chu kỳ đọc mặc định 200 ms.

### Yêu cầu phía PLC

- Khi có kết quả mới, PLC ghi xong kết quả đo, OK/NG, bộ đếm rồi mới bật bit Đo xong; bit giữ mức 1 ít nhất một
  chu kỳ đọc (nên ≥ 500 ms) và hạ về 0 trước lần đo kế tiếp. Nếu không có bit này, PLC tăng TOTAL sau mỗi lần đo.
- Mỗi START thực hiện một lần đo cho serial đã scan. PLC hạ bit máy đang chạy khi hoàn tất; STOP hủy lượt đang đo.
- PLC tự xóa 3 bit lệnh START/STOP/RESET sau khi xử lý. Bit chủng loại do phần mềm giữ mức (không cần xóa).
- Kết nối Ethernet, IP tĩnh cùng mạng máy tính; MC Protocol cấu hình *3E frame, Binary* (port ví dụ 5000)
  hoặc Modbus TCP (port 502, Unit ID).

## Dữ liệu kết quả

- `Data\results.jsonl`: mỗi dòng một kết quả (JSON Lines, chỉ ghi thêm), kèm `QrText` là nội dung QR tại lúc chốt kết quả.
- Xuất Excel: sheet `ExportResultData` với các cột No, Chủng loại, Mã quét (Serial),
  Quy cách, Lực căng (gf), Giá trị (mm), Kết quả, Ngày kiểm tra, Người kiểm tra,
  Máy tính, Ghi chú, QR (ảnh mã và nội dung đã lưu), Mục đích đo.
- `appsettings.json` cạnh file exe: kết nối, tag PLC, đường dẫn database/lịch sử, mẫu QR và lựa chọn gần nhất.
  Cấu hình kết nối và lịch sử cũ được giữ; bảng `ModelSpecs` và định dạng QR đầu vào cũ được thay bằng danh mục SQLite mới.

## Kiểm tra tự động

```powershell
dotnet run --project Tests/WorkflowChecks.csproj
```

Chạy trên Windows/.NET 10, không cần PLC thật. Kiểm tra database, chọn quy cách/model, chốt serial và kết quả,
phân định muộn, chống lưu trùng, QR, lưu thủ công, Excel, render WPF và luồng PLC mô phỏng chạy nền.
Dữ liệu và ảnh kiểm tra nằm trong thư mục tạm riêng, đường dẫn được in khi hoàn tất.

## Cấu trúc mã nguồn

```text
Models/       AppSettings, SpecDefinition, ModelDefinition, SpecCatalog, MeasurementResult
Services/     SpecDatabase, ResultQrFormatter, ResultStore, ExcelExporter,
              SettingsService, PlcMonitor
Services/Plc/ IPlcClient, McProtocolClient, ModbusTcpClient, SimulationPlcClient, TagCodec
ViewModels/   MainViewModel, SettingsViewModel, StatisticsViewModel, ReportViewModel
Controls/     HistogramControl, QrCodeControl, ChartMath
Views/        SettingsWindow, StatisticsWindow, ReportWindow
Tests/        WorkflowChecks (console, WPF STA)
```

Tiêu đề được căn giữa; góc trên trái là logo rồi tên đơn vị, ngày/giờ ở phía trên phải.
Tên đơn vị chỉnh tại Cài đặt → Thông tin & dữ liệu → Nhãn đơn vị / công ty; logo được nhúng từ `Assets/CompanyLogo.jpg`.
Nhóm Tổng/OK/NG nằm cạnh bộ lọc của bảng kết quả.
Thanh trạng thái phía dưới gồm PLC (trạng thái và Kết nối/Ngắt), Loadcell, máy và thông báo;
ba nút Cài đặt / Thống kê / Báo cáo nằm ở góc dưới phải.
Rê chuột vào nhóm PLC để xem giao thức, IP/port và phiên bản. MSNV được nhập/quét tại ô MSNV trên màn hình đo;
không có chức năng đăng nhập riêng.
