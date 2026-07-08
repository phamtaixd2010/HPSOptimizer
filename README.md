# HPS Optimizer

Ứng dụng Windows 11 để **tối ưu hệ thống cho máy cấu hình thấp** và **quản lý ổ đĩa**.
C# + WPF, .NET 8, x64, chạy với quyền Administrator.

> Triết lý: mọi thay đổi hệ thống đều tạo **System Restore Point** trước, ghi vào **nhật ký hoàn tác**,
> và trả về nguyên trạng được bằng một cú click. Các thao tác không hoàn tác được (xoá file, format)
> đều bị chặn sau một hộp thoại bắt gõ tay chuỗi xác nhận.

## Tính năng

| Tab | Nội dung |
|---|---|
| **Tổng quan** | Cấu hình máy, cảnh báo RAM/ổ C sắp đầy, nút *Tối ưu nhanh* (restore point → dọn temp → tinh chỉnh an toàn) |
| **Dọn rác** | Temp, Windows Update cache, Delivery Optimization, Prefetch, thumbnail, crash dump, cache trình duyệt, thùng rác, `Windows.old`. Quét trước, hiển thị dung lượng, rồi mới dọn |
| **Khởi động** | App chạy cùng Windows (registry Run + thư mục Startup). Tắt bằng cách xoá value (đã sao lưu) hoặc đổi tên `.lnk` → hoàn tác 100% |
| **Dịch vụ** | 16 service đã kiểm chứng là tắt/Manual an toàn (DiagTrack, SysMain, WSearch, Xbox…). Không đụng Windows Update, Defender hay mạng |
| **Tinh chỉnh** | Hiệu ứng trực quan, transparency, MenuShowDelay, Game DVR, startup delay, telemetry, Power Plan, hibernate |
| **Giám sát** | CPU/RAM/Disk/nhiệt độ realtime + biểu đồ 60 giây + top 25 tiến trình, kill process |
| **Ổ đĩa** | Liệt kê HDD/SSD/NVMe/USB: model, bus, dung lượng, **S.M.A.R.T.** (nhiệt độ, giờ chạy, hao mòn, lỗi tích luỹ, dự báo hỏng) |
| **Dung lượng** | Quét cây thư mục theo dung lượng (kiểu WizTree) + tìm file trùng lặp bằng SHA-256 ba tầng |
| **Bảo trì & Phân vùng** | TRIM cho SSD / Defrag cho HDD (tự nhận diện, **không bao giờ defrag SSD**), quét lỗi online; tạo/xoá/resize/format/gán ký tự phân vùng |
| **Nhật ký** | Toàn bộ thay đổi kèm giá trị cũ. Hoàn tác từng mục hoặc tất cả |

## Build & đóng gói

Cần **.NET 8 SDK**. Muốn ra file cài đặt thì cần thêm **[Inno Setup 6](https://jrsoftware.org/isdl.php)**.

```powershell
cd HPSOptimizer
.\build.ps1                                 # exe độc lập → publish\HPSOptimizer.exe
.\build.ps1 -Installer                      # file cài đặt  → dist\HPSOptimizer-1.0.0-Setup.exe
.\build.ps1 -FrameworkDependent             # exe ~1 MB, máy đích cần .NET 8 Runtime
.\build.ps1 -Installer -FrameworkDependent  # file cài đặt gọn, tự nhắc cài runtime nếu thiếu
```

Mặc định là **self-contained** — exe to (~150 MB) nhưng cắm vào máy Windows x64 nào cũng chạy.
Với đối tượng "máy cấu hình thấp", bắt người dùng tự đi cài .NET trước là một rào cản không đáng có.

Trình cài đặt: chạy quyền admin, chỉ x64, tối thiểu Windows 10 build 19041, tạo lối tắt Desktop
và Start Menu, có màn hình license + cảnh báo trước khi cài.

**Khi gỡ cài đặt, `C:\ProgramData\HPSOptimizer\` được giữ lại có chủ đích** — thư mục đó chứa
`undo.json` với toàn bộ giá trị registry cũ và StartMode cũ của service. Xoá nó đi là người dùng
vĩnh viễn mất khả năng trả hệ thống về nguyên trạng.

File cài đặt **chưa ký số**. SmartScreen sẽ cảnh báo ở vài máy đầu. Muốn hết thì ký bằng
`signtool` với chứng chỉ code-signing của HP Steel.

Hoặc mở `HPSOptimizer.sln` bằng Visual Studio 2022 và bấm F5 (VS phải chạy as administrator).

## Logo HP Steel

Logo gốc `Assets/logohps.png` (1705×637, RGBA) được dùng **nguyên bản, chỉ resize**, không đổi màu,
không cắt xén, không vẽ lại:

| Chỗ dùng | Cách xử lý |
|---|---|
| Thanh tiêu đề trong app | `<Image Height="30" Stretch="Uniform">` trên thẻ nền trắng bo góc — chữ logo màu navy nên không thể đặt thẳng lên header navy |
| Icon exe / cửa sổ / taskbar | `Assets/app.ico` — logo scale về 240px rồi canh giữa trên canvas vuông 256×256 **trong suốt** |
| Wizard trình cài đặt | `installer/wizard-large.bmp` (164×314) và `wizard-small.bmp` (138×140) — logo scale giữ tỷ lệ, canh giữa trên nền trắng (Inno Setup chỉ nhận BMP, không có alpha) |

Đệm thêm nền trong suốt / nền trắng để ra khung vuông không phải là sửa logo — bản thân hình logo
không bị biến dạng ở bất kỳ đâu. Lưu ý: ở cỡ 16×16 px trên taskbar, wordmark dài như logo HP Steel
sẽ nhoè thành một vệt — đó là hệ quả không tránh được khi không cho phép rút gọn logo.

Muốn tạo lại icon sau khi thay logo:

```bash
python3 -c "
from PIL import Image
src = Image.open('src/HPSOptimizer/Assets/logohps.png').convert('RGBA')
w, h = src.size; S, M = 256, 8
tw = S - 2*M; th = round(h * tw / w)
canvas = Image.new('RGBA', (S, S), (0,0,0,0))
canvas.paste(src.resize((tw, th), Image.LANCZOS), (M, (S-th)//2), src.resize((tw, th), Image.LANCZOS))
canvas.save('src/HPSOptimizer/Assets/app.ico',
            sizes=[(256,256),(128,128),(64,64),(48,48),(32,32),(24,24),(20,20),(16,16)])
"
```

## Nguyên tắc an toàn đã cài vào code

1. **Restore point trước mọi thay đổi** — `RestorePointService.EnsureAsync()`. Tự set
   `SystemRestorePointCreationFrequency = 0` để Windows không chặn tạo điểm thứ hai trong 24h.
2. **Nhật ký hoàn tác** — `%ProgramData%\HPSOptimizer\undo.json` lưu giá trị registry cũ,
   StartMode cũ của service, đường dẫn `.lnk` gốc, GUID power plan cũ.
3. **Phân vùng hệ thống bị khoá cứng ở tầng service**, không chỉ ở UI:
   `StorageService.Guard()` từ chối mọi phân vùng có `IsBoot`, `IsSystem`, hoặc type chứa
   `System`/`Recovery`/`Reserved`. `CreatePartitionAsync` cũng kiểm tra lại trong PowerShell.
4. **Thao tác huỷ dữ liệu bắt gõ chuỗi xác nhận** — `Dialogs.ConfirmByTyping()`.
   Xoá `D:` phải gõ đúng `XOA D`, format phải gõ `FORMAT D`, resize phải gõ `TOI DA SAO LUU`.
5. **SSD không bao giờ bị defrag** — `StorageService.OptimizeAsync` đọc `MediaType`/`BusType`
   rồi chọn `-ReTrim` hay `-Defrag`.
6. **File đang khoá thì bỏ qua, không ép xoá** — trình dọn rác đếm và báo lại số file bỏ qua.
7. **Không đụng vào** Windows Update, Defender, driver, `pagefile.sys`, hay blob
   `StartupApproved` không có tài liệu chính thức của Task Manager.

## Kiến trúc

```
src/HPSOptimizer/
├── Core/            Paths, Logger, ObservableObject, Fmt
├── Services/
│   ├── PowerShellRunner    chạy cmdlet Storage/Restore qua -EncodedCommand
│   ├── RegistryHelper      đọc/ghi/serialize registry
│   ├── RestorePointService điểm khôi phục
│   ├── UndoJournal         nhật ký hoàn tác (JSON)
│   ├── CleanerService      dọn rác + Recycle Bin (P/Invoke shell32)
│   ├── StartupService      app khởi động
│   ├── ServiceTweakService dịch vụ Windows (WMI ChangeStartMode)
│   ├── TweakService        registry + powercfg tweaks, apply/revert
│   ├── MonitorService      PerformanceCounter + GlobalMemoryStatusEx + ACPI temp
│   ├── StorageService      physical disk, SMART, partition, TRIM/defrag
│   └── DiskUsageService    quét cây thư mục + tìm file trùng
└── Views/           10 UserControl, code-behind thuần (không MVVM framework)
```

Không dùng thư viện ngoài ngoài 3 gói first-party của Microsoft
(`System.Management`, `System.ServiceProcess.ServiceController`, `System.Diagnostics.PerformanceCounter`).

## Giới hạn đã biết

- **Nhiệt độ CPU** lấy qua `MSAcpi_ThermalZoneTemperature`; rất nhiều mainboard không expose,
  khi đó app hiện `—` thay vì đoán bừa. Muốn chính xác phải đọc trực tiếp Super I/O chip (cần driver kernel).
- **Restore point** không tạo được nếu System Protection đang tắt cho ổ C:. App báo rõ và hỏi có tiếp tục không.
- **Telemetry = 0** chỉ có hiệu lực đầy đủ trên Windows Enterprise/Education; bản Home/Pro sẽ bị nâng lên mức 1.
- **`Resize-Partition` chỉ mở rộng được vào vùng trống liền kề phía sau.** Windows không di chuyển phân vùng.
- App **liệt kê** file trùng lặp nhưng cố tình **không tự xoá** — quyết định đó thuộc về người dùng.
