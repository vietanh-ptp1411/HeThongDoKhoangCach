from pathlib import Path
import hashlib
import zipfile

root=Path(__file__).resolve().parent.parent
release=root/'artifacts/release/1.2.1'
notes='''BÀN GIAO PHẦN MỀM ĐO LỰC CĂNG BELT - PHIÊN BẢN 1.2.1
Ngày đóng gói: 30/09/2026

Điểm mới: bỏ file master, Đơn hàng và Line; giữ cột QR trong báo cáo và Excel.
Máy đọc serial: Zebra DS8178 qua đế USB, chế độ USB Keyboard HID + Enter.

1. Chạy HeThongDoLucCangBelt-Setup-1.2.1-win-x64.exe để cài đặt.
   Dành cho Windows 64-bit, khuyến nghị Windows 11. Đã kèm .NET Desktop Runtime.
   Dùng tài khoản Windows của người vận hành; không cần chạy bằng quyền quản trị.

2. HuongDanSuDung.pdf: hướng dẫn 10 trang, có ảnh minh họa và bảng địa chỉ PLC.
   HuongDanSuDung.docx: bản Word có thể chỉnh sửa.
   Hai tài liệu cũng được cài vào thư mục HuongDan bên cạnh ứng dụng.

3. Lần đầu mở mặc định ở chế độ MÔ PHỎNG (DEMO).
   Chọn quy cách -> model -> scan serial -> START -> đo một lần -> kết quả và QR.
   Muốn đo tiếp: scan lại serial rồi nhấn START.
   Kỹ thuật viên cần cấu hình và đối chiếu PLC thật trước khi vận hành máy.

4. Vị trí cài mặc định:
   %LOCALAPPDATA%\\Programs\\HeThongDoKhoangCach
   Cài đè/nâng cấp và gỡ cài đặt giữ lại cấu hình, database và lịch sử.
   Nên đóng ứng dụng và sao lưu appsettings.json cùng thư mục Data trước khi nâng cấp.

5. Bản cài chưa có chứng thư số của đơn vị phát hành (Authenticode).
   SHA256SUMS.txt chứa mã kiểm tra EXE/PDF/DOCX và tệp này.

Đã kiểm tra cài mới, mở ứng dụng với runtime đi kèm, nâng cấp giữ dữ liệu và gỡ cài đặt giữ dữ liệu.
Kiểm tra nghiệp vụ thực hiện trên PLC mô phỏng; thiết bị PLC và máy quét thật cần nghiệm thu tại máy.
'''
(release/'DOC_TRUOC_KHI_CAI.txt').write_text(notes,encoding='utf-8-sig')
names=['HeThongDoLucCangBelt-Setup-1.2.1-win-x64.exe','HuongDanSuDung.pdf','HuongDanSuDung.docx','DOC_TRUOC_KHI_CAI.txt']
manifest=[]
for name in names:
    p=release/name
    if not p.is_file():raise FileNotFoundError(p)
    digest=hashlib.sha256(p.read_bytes()).hexdigest().upper()
    manifest.append(f'{digest}  {name}')
(release/'SHA256SUMS.txt').write_text('\n'.join(manifest)+'\n',encoding='ascii')
archive=release/'BanGiao-HeThongDoLucCangBelt-1.2.1-win-x64.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for name in names+['SHA256SUMS.txt']:z.write(release/name,name)
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for line in manifest:
        expected,name=line.split('  ',1)
        assert hashlib.sha256(z.read(name)).hexdigest().upper()==expected
qa=root/'artifacts/qa'
qa.mkdir(exist_ok=True)
for preview in release.glob('guide-preview-*.png'):
    target=qa/preview.name
    assert preview.resolve().is_relative_to(root/'artifacts') and target.resolve().is_relative_to(root/'artifacts')
    preview.replace(target)
for p in sorted(release.iterdir()):
    if p.is_file():print(f'{p.name}: {p.stat().st_size:,} bytes')
