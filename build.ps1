# 编译脚本：用系统自带的 csc.exe，产物不需要任何运行时安装。
#   pwsh -File build.ps1            # 只编译
#   pwsh -File build.ps1 -Run       # 编译后跑一次只读自检
#   pwsh -File build.ps1 -Launch    # 编译后直接启动托盘程序
param(
    [switch]$Run,
    [switch]$WriteTest,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到系统 csc.exe：$csc" }

$sources = @(Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs |
    Sort-Object FullName | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "src 下没有 .cs 文件" }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$common = @(
    '/nologo',
    '/codepage:65001',      # 源码是 UTF-8 无 BOM，不指定会按 GBK 读，中文全乱
    '/optimize+',
    '/platform:anycpu',
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
)

$icon = Join-Path $root 'assets\app.ico'
if (Test-Path $icon) { $common += "/win32icon:$icon" }

$gui = Join-Path $bin 'RazerMouse.exe'
$console = Join-Path $bin 'RazerMouseTest.exe'

Write-Host "编译 $gui ..."
& $csc @common '/target:winexe' "/out:$gui" $sources
if ($LASTEXITCODE -ne 0) { throw "编译 GUI 版失败（$LASTEXITCODE）" }

Write-Host "编译 $console ..."
& $csc @common '/target:exe' "/out:$console" $sources
if ($LASTEXITCODE -ne 0) { throw "编译控制台版失败（$LASTEXITCODE）" }

Get-ChildItem $gui, $console | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

if ($Run) {
    $argsList = @('--selftest')
    if ($WriteTest) { $argsList += '--write-test' }
    & $console @argsList
    exit $LASTEXITCODE
}

if ($Launch) {
    Start-Process -FilePath $gui
}
