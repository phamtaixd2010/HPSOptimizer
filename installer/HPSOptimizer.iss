; ============================================================================
;  HPS Optimizer — script cài đặt Inno Setup 6
;
;  Không gọi trực tiếp file này. Dùng build.ps1 để nó truyền đúng tham số:
;      .\build.ps1 -Installer                    (self-contained, không cần .NET)
;      .\build.ps1 -Installer -FrameworkDependent (nhỏ hơn, máy đích cần .NET 8)
;
;  Nếu vẫn muốn gọi tay:
;      ISCC.exe /DSourceDir="..\publish" /DSelfContained installer\HPSOptimizer.iss
; ============================================================================

#define AppName        "HPS Optimizer"
#define AppVersion     "1.0.0"
#define AppPublisher   "HP Steel"
#define AppExeName     "HPSOptimizer.exe"
#define AppUrl         "https://hpsteel.vn"

#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; AppId cố định — đổi giá trị này là Windows coi như một sản phẩm khác và sẽ cài song song.
AppId={{7C4E1A93-52B6-4F0D-9E31-2A6D8B4C0F17}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} — Tối ưu Windows 11 & Quản lý ổ đĩa

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=license.txt
InfoBeforeFile=truoc-khi-cai.txt

; App khai báo requireAdministrator trong manifest, nên trình cài cũng phải chạy quyền admin.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Chỉ x64 — project build cho win-x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

OutputDir={#OutputDir}
OutputBaseFilename=HPSOptimizer-{#AppVersion}-Setup
SetupIconFile=..\src\HPSOptimizer\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; Logo HP Steel trong wizard. Hai file BMP sinh từ logo gốc bằng cách resize + đặt lên nền trắng.
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
WizardImageStretch=no

Compression=lzma2/max
SolidCompression=yes
; Máy cấu hình thấp giải nén lzma2/max hơi lâu nhưng file tải về nhỏ hơn đáng kể.

[Languages]
Name: "vi"; MessagesFile: "compiler:Default.isl"

[Messages]
vi.WelcomeLabel1=Chào mừng đến với trình cài đặt [name]
vi.WelcomeLabel2=Trình cài đặt sẽ cài [name/ver] vào máy của bạn.%n%nHãy đóng các ứng dụng khác trước khi tiếp tục.
vi.FinishedHeadingLabel=Đã cài xong [name]
vi.ClickFinish=Bấm Finish để đóng trình cài đặt.

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt ngoài Desktop"; GroupDescription: "Lối tắt:"
Name: "startmenuicon"; Description: "Tạo lối tắt trong Start Menu"; GroupDescription: "Lối tắt:"; Flags: checkedonce

[Files]
; Toàn bộ nội dung thư mục publish. Với bản self-contained đây là exe + runtime;
; với bản framework-dependent chỉ là exe + vài dll.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{group}\Gỡ cài đặt {#AppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Mở {#AppName} ngay bây giờ"; \
  Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; CỐ TÌNH KHÔNG xoá %ProgramData%\HPSOptimizer khi gỡ cài đặt.
; Thư mục đó chứa undo.json — nhật ký hoàn tác với giá trị registry cũ, StartMode cũ của service…
; Xoá nó đi là người dùng vĩnh viễn mất khả năng trả hệ thống về nguyên trạng.
; Muốn dọn sạch thì xoá tay: C:\ProgramData\HPSOptimizer
Type: dirifempty; Name: "{app}"

[Code]
#ifndef SelfContained
{ ---- Bản framework-dependent: phải có .NET 8 Desktop Runtime trên máy đích ---- }

const
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime';

{ Pascal Script không có forward declaration — hàm được gọi phải khai báo trước. }
function HasDotNet8Folder(const Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Dir) then
    Exit;

  if FindFirst(Dir + '\*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
         (FindRec.Name <> '.') and (FindRec.Name <> '..') and
         (Copy(FindRec.Name, 1, 2) = '8.') then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function IsDotNet8DesktopInstalled(): Boolean;
begin
  { Runtime không có khoá registry ổn định để tra. Cách đáng tin nhất là
    tìm thư mục phiên bản 8.x trong shared\Microsoft.WindowsDesktop.App. }
  Result := HasDotNet8Folder(
    ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App'));
end;

function InitializeSetup(): Boolean;
var
  Answer: Integer;
  ErrCode: Integer;
begin
  Result := True;
  if IsDotNet8DesktopInstalled() then
    Exit;

  Answer := MsgBox(
    'Máy này chưa có .NET 8 Desktop Runtime.' + #13#10#13#10 +
    'HPS Optimizer bản gọn cần thành phần đó mới chạy được.' + #13#10#13#10 +
    'Bấm Yes để mở trang tải về (chọn "Desktop Runtime x64"), cài xong rồi chạy lại trình cài đặt này.' + #13#10 +
    'Bấm No để cài tiếp — nhưng app sẽ không mở được cho tới khi bạn cài runtime.',
    mbConfirmation, MB_YESNOCANCEL);

  if Answer = IDYES then
  begin
    ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOW, ewNoWait, ErrCode);
    Result := False;
  end
  else if Answer = IDCANCEL then
    Result := False;
end;
#endif
