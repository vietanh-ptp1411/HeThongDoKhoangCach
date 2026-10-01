# Chương trình PLC test – Q03UDV

Chương trình ladder nhỏ cho **Mitsubishi Q03UDV**: PLC tự sinh dữ liệu đo theo đúng luồng của phần mềm. Dùng để
thử kết nối MC Protocol và kiểm tra luồng START/STOP/RESET, bit chủng loại, Đo xong, OK/NG, bộ đếm khi chưa có máy
thật (không cần loadcell, cảm biến hay I/O).

| File | Nội dung |
|---|---|
| `TEST_Q03UDV_MAIN.csv` | Chương trình MAIN dạng list (định dạng "Write to CSV File" của GX Works2: UTF-16, phân cách TAB) |
| `TEST_Q03UDV_COMMENT.csv` | Chú thích thiết bị (tùy chọn) |
| `Gen_PLC_Test_CSV.py` | Bộ sinh 2 file trên – sửa chương trình ở đây rồi chạy `python Gen_PLC_Test_CSV.py` |

## 1. Nạp chương trình bằng GX Works2

1. **Project → New**: Series `QCPU (Q mode)`, Type `Q03UDV`, Project Type `Simple Project`, Language `Ladder`.
2. Mở ladder **MAIN** → **Edit → Read from CSV File** (hoặc chuột phải trên ladder) → chọn `TEST_Q03UDV_MAIN.csv`.
3. **Convert (F4)**.
4. (Tùy chọn) Mở **Global Device Comment → Edit → Read from CSV File** → chọn `TEST_Q03UDV_COMMENT.csv`.

> Nếu GX Works2 báo lỗi định dạng khi đọc: trên ladder bất kỳ, vẽ 1 rung (vd `LD M0` / `OUT M1`),
> **Write to CSV File** rồi gửi file đó để chỉnh `Gen_PLC_Test_CSV.py` cho khớp (bản tiếng Nhật/Anh có tiêu đề cột khác nhau).

## 2. Tham số Ethernet (bắt buộc – không nằm trong CSV)

**Parameter → PLC Parameter → Built-in Ethernet Port Setting**

| Mục | Giá trị |
|---|---|
| IP Address | `192.168.1.10` – Subnet `255.255.255.0` |
| Communication Data Code | **Binary Code** |
| Enable online change (FTP, MC Protocol) | **Tích chọn** (không có thì PLC từ chối ghi M0/M40… khi đang RUN) |
| Open Setting – dòng 1 | Protocol `TCP`, Open System `MC Protocol`, Host Station Port No. **`1388`** (hex = 5000) |

**Online → Write to PLC**: chọn Parameter + Program (MAIN) → ghi → **RESET hoặc tắt/bật nguồn CPU** (tham số IP chỉ
có hiệu lực sau khi reset) → gạt **RUN**.

Máy tính đặt IP cùng dải, ví dụ `192.168.1.100`. Thử `ping 192.168.1.10` trước.

## 3. Cài đặt phần mềm

**CÀI ĐẶT → Kết nối PLC**: Giao thức `MC Protocol – 3E frame, Binary`, IP `192.168.1.10`, Port `5000` → bấm
**Mặc định MC** ở mục Địa chỉ PLC → **Kiểm tra kết nối** → Lưu. Bit chủng loại giữ mặc định (GSM M40, PP1 M41,
M1/M2 M42, NF M43, CPX M44, LCP M45).

## 4. Bảng địa chỉ trong chương trình test

| Địa chỉ | Chiều | Ý nghĩa |
|---|---|---|
| M0 / M15 / M16 | PC → PLC | START / STOP / RESET – PLC tự xóa về 0 sau khi nhận |
| M40..M51 | PC → PLC | Bit chủng loại (giữ). M40/M44: 3.5~6.0 mm; M41/M42/M43: 4.0~5.0 mm; M45 và các bit khác: 3.0~4.0 mm |
| D100 (float) | PLC → PC | Lực căng: 2.0 gf ± 0.3, thay đổi liên tục mỗi 0,1 s |
| D102 (float) | PLC → PC | Khoảng cách: giá trị đo ± 0.05 mm, thay đổi liên tục |
| D104 (float) | PLC → PC | Kết quả đo chốt của lần đo gần nhất |
| D110 / D112 / D114 (32 bit) | PLC → PC | TOTAL / OK / NG |
| M100 | PLC → PC | Loadcell ổn định (luôn ON) |
| M101 | PLC → PC | Đo xong – ON 1 giây rồi tự tắt |
| M102 / M103 | PLC → PC | OK / NG – giữ đến lần START kế tiếp |
| M104 | PLC → PC | Đang chạy (ON 1,5 giây sau mỗi START) |
| M111 | nội bộ | START bị từ chối do chưa có bit chủng loại |
| M130 + D140 (float) | nhập tay | Bật M130 thì lần đo dùng giá trị D140 thay cho bảng test |
| D120 / D122 (float) | nội bộ | LSL / USL đang áp dụng – xem để kiểm tra bit chủng loại |

## 5. Kịch bản kiểm tra

Mỗi lần START, PLC "đo" 1,5 giây rồi trả kết quả lần lượt theo bảng dưới (sau 10 lần hoặc sau RESET thì quay lại
lần 1). Kết quả mong đợi trên phần mềm:

| Lần | Kết quả (mm) | GSM / CPX (3.5~6.0) | PP1 / M1-M2 / NF83 (4.0~5.0) | LCP (3.0~4.0) |
|---|---|---|---|---|
| 1 | 3.70 | OK | NG | OK |
| 2 | 4.50 | OK | OK | NG |
| 3 | 3.20 | NG | NG | OK |
| 4 | 5.80 | OK | NG | NG |
| 5 | 4.10 | OK | OK | NG |
| 6 | 6.40 | NG | NG | NG |
| 7 | 2.90 | NG | NG | NG |
| 8 | 5.00 | OK (biên trên) | OK (biên trên) | NG |
| 9 | 3.55 | OK | NG | OK |
| 10 | 5.20 | OK | NG | NG |

Các bước:

1. **Kết nối**: mở phần mềm → trạng thái PLC xanh; ô Loadcell (~2.0 gf) và Khoảng cách (~3.70 mm) nhảy số liên tục
   dù chưa bấm START → đọc liên tục đạt.
2. **Bit chủng loại**: scan QR hoặc chọn một model (vd M1-M2) → trên GX Works2 monitor thấy M42 = 1, các bit M40..M45 khác = 0,
   D120/D122 = 4.0/5.0. Đổi sang model khác → bit cũ tắt, bit mới bật.
3. **START**: bấm START → M0 bật rồi về 0, M104 ON khoảng 1,5 giây → phần mềm nhận kết quả, PASS/NG đúng bảng trên,
   tự lưu một dòng vào bảng kết quả, TOTAL/OK/NG tăng, histogram cập nhật.
4. Lặp START đủ 10 lần, đối chiếu từng lần với bảng.
5. **STOP**: bấm START rồi bấm STOP trước 1,5 giây → M104 tắt, không có kết quả mới, TOTAL không tăng.
6. **RESET**: bấm RESET → TOTAL/OK/NG trên PLC về 0, lần START tiếp theo trả lại 3.70.
7. **START khi chưa có chủng loại** (tắt hết M40..M51 bằng GX Works2): M111 bật, PLC không chạy.
8. **Mất kết nối**: rút cáp LAN → phần mềm báo mất kết nối; cắm lại → tự kết nối lại và tiếp tục hiển thị.
9. **Giá trị tùy ý**: bật M130, ghi số thực vào D140 (Device/Buffer Memory Batch Monitor, kiểu Real) – vd 4.00 để
   thử biên dưới của M1-M2 – rồi START.

## 6. Lỗi thường gặp

| Hiện tượng trên phần mềm | Nguyên nhân |
|---|---|
| Không kết nối / timeout | Sai IP hoặc dải mạng, chưa reset CPU sau khi ghi tham số, Open Setting chưa có dòng MC Protocol, port không phải `1388` (hex) |
| `PLC trả lỗi MC 0x0055` | Chưa tích **Enable online change (FTP, MC Protocol)** |
| `0xC050` hoặc số đọc vô lý | Communication Data Code đang là ASCII – đổi sang Binary |
| Đọc được nhưng START không chạy | Chưa có bit chủng loại (xem M111), hoặc CPU đang STOP |
