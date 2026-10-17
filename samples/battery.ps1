# battery.ps1 — 读出 Razer 鼠标电量（零依赖，PowerShell 5.1+/7 均可）
#
# 在 DeathAdder V3 Pro 无线（1532:00B7）实测通过。
# 用法： pwsh -File battery.ps1
#
# 原理：枚举 HID 接口 → 找到 FeatureReportByteLength >= 91 的 Razer 控制接口
#       → SetFeature 发 0x07/0x80 → 等 60ms → GetFeature 读回 arguments[1]。

$ErrorActionPreference = 'Stop'

$src = @'
using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class RzBattery {
    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
    [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, uint f);
    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint needed, IntPtr d);
    [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS {
        public ushort Usage; public ushort UsagePage;
        public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
    }

    static string _path;

    public static string Find() {
        if (_path != null) return _path;
        Guid hidGuid; HidD_GetHidGuid(out hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12);
        var did = new SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
        for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did); i++) {
            uint needed = 0;
            SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out needed, IntPtr.Zero);
            IntPtr buf = Marshal.AllocHGlobal((int)needed);
            try {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buf, needed, out needed, IntPtr.Zero)) continue;
                string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
                    if (h.IsInvalid) continue;
                    var attr = new HIDD_ATTRIBUTES(); attr.Size = Marshal.SizeOf(attr);
                    if (!HidD_GetAttributes(h, ref attr) || attr.VendorID != 0x1532) continue;
                    var caps = new HIDP_CAPS(); IntPtr pd;
                    if (HidD_GetPreparsedData(h, out pd)) { HidP_GetCaps(pd, ref caps); HidD_FreePreparsedData(pd); }
                    if (caps.FeatureReportByteLength >= 91) { _path = path; return path; }
                }
            } finally { Marshal.FreeHGlobal(buf); }
        }
        SetupDiDestroyDeviceInfoList(set);
        throw new Exception("no Razer control interface found");
    }

    // 返回 "raw|charging"（raw 0-255）
    public static string Read() {
        string path = Find();
        using (var h = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (h.IsInvalid) throw new Exception("CreateFile failed err=" + Marshal.GetLastWin32Error());

            byte[] req = new byte[91];
            req[2] = 0x1F; req[6] = 0x02; req[7] = 0x07; req[8] = 0x80;
            byte crc = 0; for (int i = 3; i <= 88; i++) crc ^= req[i];
            req[89] = crc;
            if (!HidD_SetFeature(h, req, 91)) throw new Exception("SetFeature failed err=" + Marshal.GetLastWin32Error());

            byte[] resp = new byte[91];
            for (int t = 0; t < 5; t++) {
                Thread.Sleep(60);
                if (!HidD_GetFeature(h, resp, 91)) throw new Exception("GetFeature failed err=" + Marshal.GetLastWin32Error());
                if (resp[1] != 0x01) break;   // 0x01 = busy
            }
            if (resp[1] != 0x02) throw new Exception("device status 0x" + resp[1].ToString("X2"));

            byte charging = 0;
            byte[] creq = (byte[])req.Clone();
            creq[8] = 0x84;                       // 0x07/0x84 = 充电状态
            byte ccrc = 0; for (int i = 3; i <= 88; i++) ccrc ^= creq[i];
            creq[89] = ccrc;
            byte[] cresp = new byte[91];
            if (HidD_SetFeature(h, creq, 91)) {
                for (int t = 0; t < 5; t++) {
                    Thread.Sleep(60);
                    if (!HidD_GetFeature(h, cresp, 91)) break;
                    if (cresp[1] != 0x01) break;
                }
                if (cresp[1] == 0x02) charging = cresp[10];   // arguments[1]
            }
            return resp[10] + "|" + charging;    // resp[10] = arguments[1]
        }
    }
}
'@

Add-Type -TypeDefinition $src -Language CSharp
$raw, $charging = [RzBattery]::Read().Split('|')
$pct = if ([int]$raw -gt 100) { [math]::Floor([int]$raw * 100 / 255) } else { [int]$raw }
$state = if ($charging -eq '1') { 'charging' } else { 'on battery' }
"battery: {0}%  (raw {1}/255, {2})" -f $pct, $raw, $state
