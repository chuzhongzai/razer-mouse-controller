# 诊断脚本：启动/唤醒常驻程序，报告每个窗口是否响应（用于定位 UI 线程卡死）
# 用法: pwsh -File tools\probe-ui.ps1 [-Exe <path>] [-SignalTimes <n>] [-Capture <path>]
param(
    [string]$Exe = "$env:LOCALAPPDATA\Programs\RazerMouse\RazerMouse.exe",
    [int]$SignalTimes = 1,
    [string]$Capture = ""
)

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Probe {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h, int index);
  public struct RECT { public int Left, Top, Right, Bottom; }

  public static string List(uint target) {
    var sb = new StringBuilder();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != target) return true;
      var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
      var c = new StringBuilder(256); GetClassNameW(h, c, 256);
      IntPtr res;
      bool ok = SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 2, 1200, out res) != IntPtr.Zero;
      bool vis = IsWindowVisible(h);
      string cls = c.ToString();
      if (cls.StartsWith("IME") || cls.StartsWith("MSCTF")) return true;   // 噪音
      RECT rc; GetWindowRect(h, out rc);
      int ex = GetWindowLongW(h, -20);
      string extra = string.Format("rect=({0},{1})-({2},{3}) {4}x{5} topmost={6}",
          rc.Left, rc.Top, rc.Right, rc.Bottom, rc.Right - rc.Left, rc.Bottom - rc.Top, (ex & 0x8) != 0);
      sb.AppendLine(string.Format("  {0} vis={1} {2} | {3} | {4} | {5}", h, vis, ok ? "OK  " : "HUNG", cls, t, extra));
      return true;
    }, IntPtr.Zero);
    return sb.ToString();
  }
}
'@

function Show-Process([int]$Id, [string]$Label) {
    "--- $Label (pid=$Id) ---"
    $out = [Probe]::List([uint32]$Id)
    if ($out.Trim().Length -eq 0) { "  (无窗口)" } else { $out.TrimEnd() }
}

Get-Process RazerMouse -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700

$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$Exe`" --minimized" }
$primary = [int]$r.ProcessId
Start-Sleep -Seconds 3
Show-Process $primary "启动后"

for ($i = 1; $i -le $SignalTimes; $i++) {
    Start-Process -FilePath $Exe | Out-Null
    Start-Sleep -Seconds 2
    Show-Process $primary "第 $i 次唤醒后"
}

if ($Capture) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 1920, 1080
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size 1920, 1080))
    $bmp.Save($Capture, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "captured -> $Capture"
}
