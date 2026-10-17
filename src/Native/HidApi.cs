// HID 底层：SetupAPI 枚举 + CreateFileW 打开 + HidD_Set/GetFeature 收发。
// 仅 P/Invoke，无任何第三方依赖；对应 samples/razer_hid.py 的 C# 版。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RazerMouse.Native
{
    internal sealed class HidInterface
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public ushort Version;
        public ushort UsagePage;
        public ushort Usage;
        public int FeatureLength;
        public int InputLength;
        public int OutputLength;

        public override string ToString()
        {
            return string.Format("pid=0x{0:X4} usagePage=0x{1:X4} usage={2} feature={3}",
                Pid, UsagePage, Usage, FeatureLength);
        }
    }

    internal static class HidApi
    {
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ_WRITE = 0x3;
        private const uint OPEN_EXISTING = 3;
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;

        internal static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr handle, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsed);

        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsed, ref HIDP_CAPS caps);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_SetFeature(IntPtr handle, byte[] buffer, uint length);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, uint length);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator,
            IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo,
            ref Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,
            ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint detailSize,
            out uint required, IntPtr deviceInfo);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string path, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        /// <summary>枚举系统里所有 HID 接口；vidFilter 为 null 时返回全部。</summary>
        internal static List<HidInterface> Enumerate(int? vidFilter)
        {
            var result = new List<HidInterface>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr set = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == InvalidHandle) return result;

            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                for (uint index = 0; ; index++)
                {
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data))
                        break;

                    uint required;
                    SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0) continue;

                    IntPtr detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        // cbSize：x64 = 8，x86 = 6；路径字符串都从偏移 4 开始
                        Marshal.WriteInt32(detail, Environment.Is64BitProcess ? 8 : 6);
                        uint written;
                        if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required,
                                out written, IntPtr.Zero))
                            continue;

                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        HidInterface info = Probe(path, vidFilter);
                        if (info != null) result.Add(info);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        private static HidInterface Probe(string path, int? vidFilter)
        {
            IntPtr handle = OpenDevice(path);
            if (handle == InvalidHandle) return null;
            try
            {
                var attributes = new HIDD_ATTRIBUTES();
                attributes.Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (!HidD_GetAttributes(handle, ref attributes)) return null;
                if (vidFilter.HasValue && attributes.VendorID != vidFilter.Value) return null;

                var info = new HidInterface();
                info.Path = path;
                info.Vid = attributes.VendorID;
                info.Pid = attributes.ProductID;
                info.Version = attributes.VersionNumber;

                IntPtr preparsed;
                if (HidD_GetPreparsedData(handle, out preparsed))
                {
                    try
                    {
                        var caps = new HIDP_CAPS();
                        if (HidP_GetCaps(preparsed, ref caps) >= 0)
                        {
                            info.UsagePage = caps.UsagePage;
                            info.Usage = caps.Usage;
                            info.InputLength = caps.InputReportByteLength;
                            info.OutputLength = caps.OutputReportByteLength;
                            info.FeatureLength = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        HidD_FreePreparsedData(preparsed);
                    }
                }
                return info;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>打开设备；先试 GENERIC_WRITE（GENERIC_READ 会被系统拒绝），失败再试 access=0。</summary>
        internal static IntPtr OpenDevice(string path)
        {
            IntPtr handle = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == InvalidHandle)
            {
                handle = CreateFileW(path, 0, FILE_SHARE_READ_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            }
            return handle;
        }
    }
}
