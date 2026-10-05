# Đóng gói bản Windows 1.2.1

Bản giao khách nằm trong `artifacts/release/1.2.1`. Bộ cài NSIS chứa ứng dụng self-contained `win-x64`,
runtime .NET và hướng dẫn PDF/Word. Cài theo tài khoản Windows tại
`%LOCALAPPDATA%\Programs\BeltTensionMeasurement`, không yêu cầu quyền quản trị.

Project và file chạy hiện mang tên `BeltTensionMeasurement`. Bộ cài giữ khóa đăng ký `MVALab\BeltMeasurement`
để nhận diện bản đã cài: khi nâng cấp sẽ dùng lại thư mục của bản cũ, kể cả thư mục mang tên
`HeThongDoKhoangCach`, nhằm giữ cấu hình và dữ liệu. Sau khi kiểm tra bản cũ đã đóng, bộ cài gỡ các binary
mang tên cũ và thay bằng tên mới; không xóa `appsettings.json` hay `Data`.

## Công cụ

- .NET SDK 10.
- Python 3.12 và các gói trong `guide-requirements.txt` (chỉ để tạo tài liệu).
- NSIS 3.13, bản ZIP từ trang tải chính thức: <https://nsis.sourceforge.io/Download>.
  Giải nén vào `artifacts/tools/nsis-3.13`, hoặc truyền `-CompilerPath` cho script đóng gói.

## Tạo ảnh và tài liệu

```powershell
dotnet run --project Tests/WorkflowChecks.csproj -c Release -- --manual-assets
python -m venv artifacts/tools/docs-env
artifacts/tools/docs-env/Scripts/python.exe -m pip install -r Packaging/guide-requirements.txt
artifacts/tools/docs-env/Scripts/python.exe Packaging/Build-UserGuide.py --screenshots "DUONG_DAN_ANH_VUA_IN_RA" --output artifacts/release/1.2.1
```

Ảnh minh họa được tạo từ một phiên PLC mô phỏng riêng, không lấy dữ liệu vận hành thật.

## Tạo bộ cài

```powershell
./Packaging/Publish-Installer.ps1
```

Script publish không trim WPF, không ghi đè đầu ra Debug đang chạy. Không đóng gói file master; database và lịch sử được tạo lúc sử dụng. Các thông báo thành phần phụ thuộc
được thu thập vào `Licenses/` và `THIRD-PARTY-NOTICES.txt` bên trong thư mục ứng dụng.
Mã SHA256 của EXE/PDF/DOCX được ghi vào `SHA256SUMS.txt`.

## Kiểm tra

```powershell
dotnet run --project Tests/WorkflowChecks.csproj -c Release
./Packaging/Verify-Installer.ps1
python Packaging/Finalize-Release.py
```

Lệnh cuối tạo ZIP bàn giao và kiểm tra lại SHA256 của từng tệp bên trong gói.

Kiểm tra bộ cài sử dụng thư mục riêng dưới `artifacts/qa`, tạo rồi gỡ đăng ký ứng dụng và shortcut.
Script từ chối chạy nếu đã có một bản được đăng ký hoặc shortcut cùng tên, để tránh ghi đè bản đang dùng.
Nó kiểm tra cài mới, mở ứng dụng, runtime đi kèm, nâng cấp giữ nguyên dữ liệu (kể cả tệp cũ không còn dùng) và gỡ cài đặt giữ dữ liệu.

`appsettings.json`, `Data/HeThongDo.db`, `Data/results.jsonl` không được phân phối trong bộ cài.
File master không còn được đọc hay phân phối. Tệp master có từ bản cũ được giữ trên đĩa như dữ liệu khác của người dùng.
Gỡ cài đặt chỉ xóa danh sách tệp chương trình đã đóng gói.

Phiên bản hiện được đặt trong csproj, MainViewModel, script NSIS, script publish và tài liệu;
cần cập nhật đồng bộ khi phát hành phiên bản mới. Bộ cài hiện chưa có chữ ký Authenticode của đơn vị phát hành.
