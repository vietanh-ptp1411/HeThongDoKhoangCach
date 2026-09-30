# Hệ thống đo lực căng belt – PLC & Loadcell

Ứng dụng WPF (.NET 10) theo luồng trong **GIAO DIỆN.pdf**: chọn quy cách, chọn model thuộc quy cách,
scan serial một lần cho cuộn dây, nhấn START, đo liên tục đến khi STOP và hiển thị QR của kết quả mới nhất.

## Chạy ứng dụng

```powershell
dotnet build
dotnet run --project HeThongDoKhoangCach.csproj
```

Mặc định chạy PLC mô phỏng. Chọn `3.5 ~ 6 mm` → `CPX` → scan/gõ serial và Enter → START.
Mô phỏng trả kết quả đầu tiên sau khoảng 1.5 giây, rồi tiếp tục đo khoảng mỗi 4.5 giây đến khi STOP.
Phần mềm đọc lực căng/khoảng cách liên tục khi đã kết nối PLC.

## Trình tự vận hành

1. **Quy cách**: chọn từ database SQLite.
2. **Chủng loại**: chỉ hiển thị model thuộc quy cách đã chọn. Đổi quy cách sẽ xóa model và serial cũ.
3. **Số serial**: scan mã vạch **một lần cho cả cuộn dây**, kết thúc bằng Enter. Có thể gõ serial rồi bấm START trực tiếp.
   Mã quét được coi là toàn bộ serial; file master chỉ bổ sung đơn hàng/line, không đổi model hay quy cách.
4. **START**: gửi bit model, LSL/USL nếu cấu hình, rồi bit START xuống PLC. Máy đo liên tục đến khi STOP.
   Trong khi chạy, khóa lựa chọn và scan để tất cả kết quả của cuộn dây dùng đúng serial/model/quy cách.
5. Khi PLC báo đo xong, chốt giá trị, lực căng, thời gian và serial. Nếu chưa có OK/NG thì chờ phân định;
   giá trị đã chốt không bị thay bằng giá trị ở chu kỳ đọc sau. Không cấu hình OK/NG thì so với LSL/USL.
6. Mỗi kết quả mới cập nhật **giá trị + PASS/NG + QR**, tự lưu một bản ghi riêng cùng serial.
   Hai lần đo có cùng giá trị vẫn lưu thành hai bản ghi khi có hai xung đo xong/bộ đếm tăng.
   Tắt tự lưu thì kết quả được giữ trong bộ nhớ; bấm **LƯU DỮ LIỆU** để lưu toàn bộ các kết quả đang chờ.
   Khi ghi file lỗi, các kết quả chưa ghi vẫn được giữ để thử lại. **RESET** bỏ dữ liệu chưa lưu và xóa serial.
7. Hết cuộn dây thì nhấn **STOP**. START lại có thể dùng ngay serial cũ; chỉ scan lại khi đổi cuộn dây/serial.

**STOP** gửi lệnh dừng, ngừng ghi thêm kết quả và giữ serial cùng kết quả cuối. Lực căng/khoảng cách vẫn cập nhật
khi PLC còn kết nối. Khi mất kết nối, kết thúc lượt chạy; nhấn START sau khi kết nối phục hồi, không cần scan lại.
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

Trong **CÀI ĐẶT → DANH MỤC QUY CÁCH – MODEL**, chọn dòng quy cách rồi thêm/sửa/xóa model ở bảng dưới.
**Lưu & áp dụng** ghi toàn bộ danh mục trong một transaction. **Hủy** không lưu các thay đổi danh mục.
Cho phép M1/M2 dùng chung bit trong cùng quy cách, không cho cùng bit điều khiển hai quy cách khác nhau.
Giới hạn phải hợp lệ, model không trống/trùng trong một quy cách. Quy cách và model được nhớ cho lần mở tiếp theo;
serial chỉ cần scan lại khi mở phần mềm, RESET hoặc đổi cuộn dây. Cấu hình `DatabaseFilePath` trong `appsettings.json`
nếu cần đặt database ở vị trí khác.

## QR kết quả

Mặc định QR chứa **`3.70`** khi giá trị đo là 3.70 mm. PDF chưa quy định chi tiết payload phía Brother,
nên mẫu có thể thay đổi trong **CÀI ĐẶT → MÃ QR KẾT QUẢ ĐO**.

- `{Value}`: giá trị có hai chữ số thập phân, dấu chấm.
- `{Serial};{Model};{Value};{Result}`: ví dụ `985X57200-00123;CPX;3.70;OK`.
- Các trường khác: `{Spec}`, `{Lsl}`, `{Usl}`, `{Force}`, `{Date}`, `{Time}`, `{DateTime}`, `{Inspector}`, `{OrderNo}`, `{Line}`.

Mã QR vẽ đen/trắng, có vùng trắng bao quanh, căn theo pixel. Chỉ sinh QR khi có kết quả mới hoàn chỉnh;
scan serial khác hoặc RESET sẽ xóa QR cũ. Nội dung quá dài sẽ báo lỗi thay vì hiển thị mã không đọc được.
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
| PLC → PC | Giá trị đo | Kết quả đo (chốt) | Float32 | D104 | HR104 | Trống → lấy khoảng cách tại lúc Đo xong. Giá trị 0 = chưa có kết quả |
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
- START cho máy đo liên tục với serial đã scan; STOP dừng máy. Bit máy đang chạy phản ánh trạng thái thực tế.
- PLC tự xóa 3 bit lệnh START/STOP/RESET sau khi xử lý. Bit chủng loại do phần mềm giữ mức (không cần xóa).
- Kết nối Ethernet, IP tĩnh cùng mạng máy tính; MC Protocol cấu hình *3E frame, Binary* (port ví dụ 5000)
  hoặc Modbus TCP (port 502, Unit ID).

## File master (BISG)

Dùng để bổ sung đơn hàng/line từ serial; model và quy cách theo lựa chọn trên màn hình. `Data\master.csv` – CSV UTF-8, cột `Key,OrderNo,Line,Model`.
`Key` là serial đầy đủ hoặc tiền tố serial (ví dụ `985X57200` khớp với `985X57200-00123`). Khớp chính xác được
ưu tiên, sau đó là tiền tố dài nhất. File được nạp lại tự động khi thay đổi, có thể trỏ tới file dùng chung trên mạng.

## Dữ liệu kết quả

- `Data\results.jsonl`: mỗi dòng một kết quả (JSON Lines, chỉ ghi thêm).
- Xuất Excel: sheet `ExportResultData` với các cột No, Dòng hàng, Line, Chủng loại, Mã quét (Serial),
  Hạng mục kiểm tra, Quy cách, Lực căng (gf), Giá trị (mm), Kết quả, Ngày kiểm tra, Người kiểm tra,
  Máy tính, Ghi chú.
- `appsettings.json` cạnh file exe: kết nối, tag PLC, đường dẫn database/master/lịch sử, mẫu QR và lựa chọn gần nhất.
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
Services/     SpecDatabase, ResultQrFormatter, MasterDataService, ResultStore, ExcelExporter,
              SettingsService, PlcMonitor
Services/Plc/ IPlcClient, McProtocolClient, ModbusTcpClient, SimulationPlcClient, TagCodec
ViewModels/   MainViewModel, SettingsViewModel, StatisticsViewModel, ReportViewModel
Controls/     HistogramControl, QrCodeControl, ChartMath
Views/        SettingsWindow, StatisticsWindow, ReportWindow, LoginWindow
Tests/        WorkflowChecks (console, WPF STA)
```
