# Hệ thống đo lực căng belt – PLC & Loadcell

Ứng dụng WPF (.NET 10) theo luồng trong **GIAO DIỆN.pdf**: chọn quy cách, chọn model thuộc quy cách,
scan serial, nhấn START, đo một lần rồi hiển thị kết quả và mã QR. Lượt tiếp theo scan lại và nhấn START.

## Chạy ứng dụng

```powershell
dotnet build
dotnet run --project HeThongDoKhoangCach.csproj
```

Bộ cài Windows x64 và hướng dẫn PDF/Word: `artifacts/release/1.2.1`.
Quy trình tạo lại bộ cài và kiểm tra cài đặt xem [Packaging/README.md](Packaging/README.md).

Mặc định chạy PLC mô phỏng. Chọn `3.5 ~ 6 mm` → `CPX` → scan/gõ serial và Enter → START.
Mỗi START trong mô phỏng đo một lần, trả kết quả sau khoảng 1.5 giây rồi dừng.
Phần mềm đọc lực căng/khoảng cách liên tục khi đã kết nối PLC.

## Trình tự vận hành

1. **Quy cách**: chọn từ database SQLite.
2. **Chủng loại**: chỉ hiển thị model thuộc quy cách đã chọn. Đổi quy cách sẽ xóa model và serial cũ.
3. **Số serial**: scan mã vạch cho lượt đo, kết thúc bằng Enter. Có thể gõ serial rồi bấm START trực tiếp.
   Mã quét được coi là toàn bộ serial. Model và quy cách do người vận hành chọn, không cần file master hoặc thông tin đơn hàng/line.
   Thiết bị khách hàng xác nhận: **Zebra DS8178**, đế thu kết nối máy tính qua **USB**.
   Cấu hình **USB Keyboard HID** và gửi **Enter** sau mã. Khi màn hình đo đang hoạt động và đã chọn quy cách/model,
   có thể bấm cò quét ngay sau khi chọn model hoặc bấm vào bảng/nút, không cần click ô Serial.
   Trong lúc đo, mã quét mới được bỏ qua để giữ đúng serial của lượt đang chạy. Ô lọc ngày vẫn cho nhập ngày bằng bàn phím;
   hộp thoại Cài đặt/Đăng nhập/Báo cáo nhận bàn phím riêng. Chức năng này không bắt mã khi đang dùng ứng dụng khác.
4. **START**: gửi bit model, LSL/USL nếu cấu hình, rồi bit START xuống PLC để đo một lần.
   Trong khi đo, khóa lựa chọn và scan để kết quả dùng đúng serial/model/quy cách.
5. Khi PLC báo đo xong, chốt giá trị, lực căng, thời gian và serial. Nếu chưa có OK/NG thì chờ phân định;
   giá trị đã chốt không bị thay bằng giá trị ở chu kỳ đọc sau. Không cấu hình OK/NG thì so với LSL/USL.
6. Hiển thị **giá trị + PASS/NG + QR**, tự lưu đúng một kết quả cho lượt START này.
   Các tín hiệu đo xong tiếp theo không tạo thêm bản ghi hoặc thay đổi kết quả/QR đã chốt.
   Tắt tự lưu thì bấm **LƯU DỮ LIỆU** trước lượt tiếp theo. Khi ghi file lỗi, kết quả chưa ghi được giữ để thử lại.
   **RESET** bỏ dữ liệu chưa lưu và xóa serial.
7. Để đo lượt tiếp theo, scan lại serial rồi nhấn **START**. Có thể scan lại cùng serial nếu cần đo lại.

**STOP** dùng để hủy lượt đo đang chạy, giữ serial để có thể nhấn START đo lại. Lực căng/khoảng cách vẫn cập nhật
khi PLC còn kết nối. Khi mất kết nối, hủy lượt chưa hoàn tất; nhấn START đo lại sau khi kết nối phục hồi.
Lần đọc đầu tiên sau kết nối chỉ tạo mốc phát hiện, không lấy kết quả cũ của PLC.
Nếu PLC báo máy vẫn đang chạy, chờ máy dừng hoặc bấm STOP trước khi đổi serial/model.

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
- Các trường khác: `{Spec}`, `{Lsl}`, `{Usl}`, `{Force}`, `{Date}`, `{Time}`, `{DateTime}`, `{Inspector}`.

Mã QR vẽ đen/trắng, có vùng trắng bao quanh, căn theo pixel. Chỉ sinh QR khi có kết quả mới hoàn chỉnh;
scan serial khác hoặc RESET sẽ xóa QR cũ. Nội dung quá dài sẽ báo lỗi thay vì hiển thị mã không đọc được.
Nội dung QR được lưu cùng từng kết quả đo. Trong **BÁO CÁO**, cột **QR** hiển thị ảnh mã và nội dung;
rê chuột để xem mã phóng to. Có thể tìm kiếm theo nội dung QR. Đổi mẫu QR trong Cài đặt không thay đổi mã đã lưu.
Mẫu QR cũ có `{OrderNo}` hoặc `{Line}` được trả về `{Value}` khi nạp cài đặt. QR đã lưu trong lịch sử vẫn giữ nguyên.
File Excel cũng có cột **QR**, gồm ảnh mã và nội dung dạng chữ. Các bản ghi cũ chưa lưu QR để trống cột này.
Cần thử quét trên màn hình thực tế bằng Zebra DS8178 và xác nhận định dạng mà hệ thống Brother nhận.

## Lịch sử, biểu đồ và xuất Excel

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
| PC → PLC | Lệnh | START * | Bit | M0 | C0 | Máy chạy. Phần mềm ghi mức 1, PLC tự xóa |
| PC → PLC | Lệnh | STOP | Bit | M15 | C15 | Máy tạm dừng |
| PC → PLC | Lệnh | RESET | Bit | M16 | C16 | Xóa giá trị / bộ đếm |
| PC → PLC | Chủng loại | Bit theo chủng loại | Bit | M40..M50 | C40..C50 | Mức 1 cho model đang chọn, các bit khác mức 0 (DANH MỤC trong CÀI ĐẶT) |
| PC → PLC | Quy cách | LSL / USL ghi xuống | Float32 | (trống) | (trống) | Tùy chọn, ghi khi chọn model và trước START |
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
  Hạng mục kiểm tra, Quy cách, Lực căng (gf), Giá trị (mm), Kết quả, Ngày kiểm tra, Người kiểm tra,
  Máy tính, Ghi chú, QR (ảnh mã và nội dung đã lưu).
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
Views/        SettingsWindow, StatisticsWindow, ReportWindow, LoginWindow
Tests/        WorkflowChecks (console, WPF STA)
```
