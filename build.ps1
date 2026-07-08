<#
    Build HPS Optimizer.

    Chạy trên Windows 11, PowerShell 5.1 hoặc 7+:

        .\build.ps1                                 # chỉ build exe độc lập vào .\publish
        .\build.ps1 -Installer                      # build + đóng gói file cài đặt vào .\dist
        .\build.ps1 -FrameworkDependent             # exe ~1 MB, máy đích cần .NET 8 Desktop Runtime
        .\build.ps1 -Installer -FrameworkDependent  # file cài đặt gọn, có kiểm tra runtime khi cài

    Mặc định là self-contained: exe to hơn nhưng cắm vào máy nào cũng chạy,
    không bắt người dùng đi cài .NET trước — đúng tinh thần "máy cấu hình thấp".

    Cần:
      - .NET 8 SDK        https://dotnet.microsoft.com/download/dotnet/8.0
      - Inno Setup 6      https://jrsoftware.org/isdl.php   (chỉ khi dùng -Installer)
#>
[CmdletBinding()]
param(
    [switch]$Installer,
    [switch]$FrameworkDependent,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj      = Join-Path $root 'src\HPSOptimizer\HPSOptimizer.csproj'
$publish   = Join-Path $root 'publish'
$dist      = Join-Path $root 'dist'
$issScript = Join-Path $root 'installer\HPSOptimizer.iss'

$selfContained = -not $FrameworkDependent

# ---------------------------------------------------------------- kiểm tra công cụ

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Không tìm thấy 'dotnet'. Cài .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

function Find-ISCC {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

if ($Installer) {
    $iscc = Find-ISCC
    if (-not $iscc) {
        throw @"
Không tìm thấy ISCC.exe (trình biên dịch Inno Setup).
Cài Inno Setup 6 rồi chạy lại: https://jrsoftware.org/isdl.php
Hoặc bỏ tham số -Installer để chỉ build exe.
"@
    }
    Write-Host "    Inno Setup: $iscc" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- publish

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "==> Khôi phục gói NuGet" -ForegroundColor Cyan
dotnet restore $proj
if ($LASTEXITCODE -ne 0) { throw "restore thất bại (exit $LASTEXITCODE)." }

Write-Host "==> Build $Configuration | self-contained: $selfContained" -ForegroundColor Cyan

# Không dùng tên biến $args — đó là biến tự động của PowerShell.
$publishArgs = @(
    'publish', $proj,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', $publish,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '--self-contained', $selfContained.ToString().ToLower()
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Build thất bại (exit $LASTEXITCODE)." }

$exe = Join-Path $publish 'HPSOptimizer.exe'
if (-not (Test-Path $exe)) { throw "Build xong nhưng không thấy $exe." }

$exeSize = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "==> exe: $exe ($exeSize MB)" -ForegroundColor Green

if (-not $Installer) {
    Write-Host ""
    Write-Host "Xong. App yêu cầu quyền Administrator, Windows sẽ tự hỏi UAC khi mở." -ForegroundColor Yellow
    if ($FrameworkDependent) {
        Write-Host "Máy đích cần .NET 8 Desktop Runtime:" -ForegroundColor Yellow
        Write-Host "https://dotnet.microsoft.com/download/dotnet/8.0/runtime" -ForegroundColor Yellow
    }
    Write-Host "Thêm -Installer để đóng gói thành file cài đặt." -ForegroundColor DarkGray
    return
}

# ---------------------------------------------------------------- đóng gói

New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "==> Đóng gói file cài đặt" -ForegroundColor Cyan

$isccArgs = @(
    "/DSourceDir=$publish",
    "/DOutputDir=$dist"
)
# Biến SelfContained bật/tắt đoạn kiểm tra .NET runtime trong script Inno.
if ($selfContained) { $isccArgs += '/DSelfContained' }
$isccArgs += $issScript

& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "Đóng gói thất bại (exit $LASTEXITCODE)." }

$setup = Get-ChildItem $dist -Filter 'HPSOptimizer-*-Setup.exe' |
         Sort-Object LastWriteTime -Descending |
         Select-Object -First 1

if (-not $setup) { throw "Không tìm thấy file cài đặt trong $dist." }

$setupSize = [math]::Round($setup.Length / 1MB, 1)
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash

Write-Host ""
Write-Host "==> File cài đặt: $($setup.FullName)" -ForegroundColor Green
Write-Host "    Dung lượng:   $setupSize MB"
Write-Host "    SHA-256:      $hash"
Write-Host ""
if ($selfContained) {
    Write-Host "Bản self-contained: cắm vào máy Windows x64 nào cũng chạy, không cần cài .NET." -ForegroundColor Yellow
} else {
    Write-Host "Bản framework-dependent: trình cài đặt sẽ kiểm tra và nhắc cài .NET 8 Desktop Runtime nếu thiếu." -ForegroundColor Yellow
}
Write-Host "File cài đặt CHƯA ký số — SmartScreen sẽ cảnh báo ở vài máy đầu tiên." -ForegroundColor DarkYellow
Write-Host "Muốn hết cảnh báo thì ký bằng signtool với chứng chỉ code-signing của HP Steel." -ForegroundColor DarkYellow
