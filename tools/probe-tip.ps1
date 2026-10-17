# 探针：把光标移到托盘图标上，检查"悬停文字气泡"是否出现/移开后是否立刻消失。
# 用法：pwsh -File tools\probe-tip.ps1 [-Capture 输出png]
param(
    [string]$Capture = "",
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'

Add-Type -Namespace Win -Name Dpi -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
'@
[void][Win.Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4))   # per-monitor v2，否则拿到的是虚拟化后的坐标

Add-Type -Namespace Win -Name Native -MemberDefinition @'
public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern IntPtr FindWindowW(string cls, string title);
[DllImport("user32.dll")] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
'@

function Get-AppWindows([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [Win.Native+EnumProc]{
        param($h, $l)
        $p = 0
        [void][Win.Native]::GetWindowThreadProcessId($h, [ref]$p)
        if ($p -eq $targetPid -and [Win.Native]::IsWindowVisible($h)) {
            $cls = New-Object System.Text.StringBuilder 256
            [void][Win.Native]::GetClassNameW($h, $cls, 256)
            $title = New-Object System.Text.StringBuilder 256
            [void][Win.Native]::GetWindowTextW($h, $title, 256)
            $r = New-Object Win.Native+RECT
            [void][Win.Native]::GetWindowRect($h, [ref]$r)
            [void]$found.Add([pscustomobject]@{
                Handle = $h; Class = $cls.ToString(); Title = $title.ToString()
                X = $r.Left; Y = $r.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top
            })
        }
        return $true
    }
    [void][Win.Native]::EnumWindows($cb, [IntPtr]::Zero)
    return $found
}

function Find-TipWindow([int]$targetPid) {
    # 气泡是 WindowsForms 窗口，宽 200-900、高 60-300，且不是托盘的主窗口（360x352 是面板，1400 高是别的）
    Get-AppWindows $targetPid | Where-Object {
        $_.Class -like 'WindowsForms10.Window*' -and $_.W -ge 200 -and $_.W -le 900 -and $_.H -ge 50 -and $_.H -le 300
    }
}

if ($ProcessId -eq 0) {
    $ProcessId = (Get-Process RazerMouse -ErrorAction SilentlyContinue | Select-Object -First 1).Id
}
if (!$ProcessId) { throw '没找到正在运行的 RazerMouse.exe' }
"目标进程 : $ProcessId"

# Win11 的任务栏是 XAML 的，没有 TrayNotifyWnd；直接按 Shell_TrayWnd 的矩形整条扫。
$trayHwnd = [IntPtr]::Zero
$cbTray = [Win.Native+EnumProc]{
    param($h, $l)
    $c = New-Object System.Text.StringBuilder 256
    [void][Win.Native]::GetClassNameW($h, $c, 256)
    if ($c.ToString() -eq 'Shell_TrayWnd') { $script:trayHwnd = $h; return $false }
    return $true
}
[void][Win.Native]::EnumWindows($cbTray, [IntPtr]::Zero)
if ($trayHwnd -eq [IntPtr]::Zero) { throw '找不到 Shell_TrayWnd' }
$nr = New-Object Win.Native+RECT
[void][Win.Native]::GetWindowRect($trayHwnd, [ref]$nr)
"任务栏   : ($($nr.Left),$($nr.Top))-($($nr.Right),$($nr.Bottom))  宽 $($nr.Right - $nr.Left) 高 $($nr.Bottom - $nr.Top)"

$saved = New-Object Win.Native+POINT
[void][Win.Native]::GetCursorPos([ref]$saved)

$hit = $null
# 从任务栏右端往左扫（图标区在右半边），找一个会弹出气泡的图标位置
$y = [int](($nr.Top + $nr.Bottom) / 2)
$startX = $nr.Right - 10
$endX = $nr.Right - 1500
$step = 6
for ($x = $startX; $x -ge $endX; $x -= $step) {
    [void][Win.Native]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 90
    $tip = Find-TipWindow $ProcessId
    if ($tip) {
        $hit = [pscustomobject]@{ X = $x; Y = $y; Tip = $tip }
        break
    }
}
[void][Win.Native]::SetCursorPos($saved.X, $saved.Y)

if (!$hit) {
    "结果     : 扫完通知区域没看到气泡窗口（外壳可能把 WM_MOUSEMOVE 转给了别的图标，或图标在溢出区里）"
    exit 2
}

"命中     : 光标 ($($hit.X),$($hit.Y)) 处出现气泡"
$hit.Tip | ForEach-Object { "  窗口 $($_.X),$($_.Y) $($_.W)x$($_.H) class=$($_.Class)" }

if ($Capture) {
    Add-Type -AssemblyName System.Drawing
    $left = [Math]::Max(0, $hit.X - 520)
    $top = [Math]::Max(0, $hit.Y - 260)
    $w = [Math]::Min(1040, $hit.Tip[0].W + 560)
    $h = 420
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $bmp.Save($Capture, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "截图     : $Capture"
}

# 移开是否立刻消失
[void][Win.Native]::SetCursorPos([int](($saved.X)), [int](($saved.Y)))
Start-Sleep -Milliseconds 250
$still = Find-TipWindow $ProcessId
if ($still) { "移开后   : 气泡还在（$($still.Count) 个），不符合预期" ; exit 3 }
"移开后   : 气泡已消失"
exit 0
