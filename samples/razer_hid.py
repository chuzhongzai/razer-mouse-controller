"""razer_hid — 零依赖读取/控制 Razer 鼠标（Windows，ctypes 直调 HID API）。

在 Razer DeathAdder V3 Pro（无线 dongle，1532:00B7）上逐项实测通过：
  serial / firmware / battery / charging / dpi / polling-rate。
所有命令都走 91 字节 Feature Report（report id 0 + 90 字节 razer_report），
不需要驱动、不需要管理员权限、不需要任何第三方库。

用法：
    python razer_hid.py              # 读一遍全部信息
    python razer_hid.py battery      # 只读电量
    python razer_hid.py dpi          # 只读 DPI
    python razer_hid.py poll         # 只读轮询率

作为库：
    from razer_hid import Device
    with Device() as d:
        print(d.battery())     # (百分比 0-255, 充电状态 0/1)

协议细节见同目录 ../protocol.md。
"""

import ctypes
import time
from ctypes import wintypes as wt

hid = ctypes.WinDLL("hid")
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
setupapi = ctypes.WinDLL("setupapi", use_last_error=True)

GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
FILE_SHARE_RW = 0x3
INVALID_HANDLE = ctypes.c_void_p(-1).value

RAZER_VID = 0x1532
# DeathAdder V3 Pro：00B6 有线 / 00B7 无线 / 00C2、00C3 另外两档同型号 PID
PID_DA_V3_PRO = (0x00B6, 0x00B7, 0x00C2, 0x00C3)

REPORT_LEN = 91          # 1 字节 report id + 90 字节 razer_report
DEFAULT_TXN = 0x1F
REPLY_WAIT_S = 0.06      # 发完 SetFeature 必须等一会儿再 GetFeature
MAX_RETRY = 5

STATUS = {0x00: "new", 0x01: "busy", 0x02: "ok", 0x03: "failure",
          0x04: "timeout/no-response", 0x05: "not-supported"}

# 轮询率编码（标准 0x00/0x05 与 HyperPolling 0x00/0x40 两套）
POLL_CODE_STD = {1000: 0x01, 500: 0x02, 125: 0x08}
POLL_CODE_HYPER = {8000: 0x01, 4000: 0x02, 2000: 0x04, 1000: 0x08,
                   500: 0x10, 250: 0x20, 125: 0x40}


class GUID(ctypes.Structure):
    _fields_ = [("Data1", wt.DWORD), ("Data2", wt.WORD), ("Data3", wt.WORD),
                ("Data4", ctypes.c_ubyte * 8)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", wt.ULONG), ("VendorID", wt.USHORT),
                ("ProductID", wt.USHORT), ("VersionNumber", wt.USHORT)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", wt.DWORD), ("Reserved", ctypes.c_size_t)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [
        ("Usage", wt.USHORT), ("UsagePage", wt.USHORT),
        ("InputReportByteLength", wt.USHORT),
        ("OutputReportByteLength", wt.USHORT),
        ("FeatureReportByteLength", wt.USHORT),
        ("Reserved", wt.USHORT * 17),
        ("NumberLinkCollectionNodes", wt.USHORT),
        ("NumberInputButtonCaps", wt.USHORT), ("NumberInputValueCaps", wt.USHORT),
        ("NumberInputDataIndices", wt.USHORT),
        ("NumberOutputButtonCaps", wt.USHORT), ("NumberOutputValueCaps", wt.USHORT),
        ("NumberOutputDataIndices", wt.USHORT),
        ("NumberFeatureButtonCaps", wt.USHORT), ("NumberFeatureValueCaps", wt.USHORT),
        ("NumberFeatureDataIndices", wt.USHORT),
    ]


hid.HidD_GetHidGuid.argtypes = [ctypes.POINTER(GUID)]
hid.HidD_GetAttributes.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetAttributes.restype = wt.BOOL
hid.HidD_GetPreparsedData.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_GetPreparsedData.restype = wt.BOOL
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidP_GetCaps.restype = wt.LONG
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidD_SetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wt.DWORD]
hid.HidD_SetFeature.restype = wt.BOOL
hid.HidD_GetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wt.DWORD]
hid.HidD_GetFeature.restype = wt.BOOL

setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), ctypes.c_void_p,
                                          ctypes.c_void_p, wt.DWORD]
setupapi.SetupDiGetClassDevsW.restype = ctypes.c_void_p
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                                 ctypes.POINTER(GUID), wt.DWORD,
                                                 ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiEnumDeviceInterfaces.restype = wt.BOOL
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [ctypes.c_void_p,
                                                      ctypes.POINTER(SP_DEVICE_INTERFACE_DATA),
                                                      ctypes.c_void_p, wt.DWORD,
                                                      ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
setupapi.SetupDiGetDeviceInterfaceDetailW.restype = wt.BOOL
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.restype = wt.BOOL

k32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p,
                            wt.DWORD, wt.DWORD, ctypes.c_void_p]
k32.CreateFileW.restype = ctypes.c_void_p
k32.CloseHandle.argtypes = [ctypes.c_void_p]
k32.CloseHandle.restype = wt.BOOL


def enumerate_interfaces(vid=RAZER_VID):
    """枚举系统里所有 HID 接口，返回 [{path, vid, pid, usage, usage_page, feature_len}]。"""
    g = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(g))
    hset = setupapi.SetupDiGetClassDevsW(ctypes.byref(g), None, None, 0x12)  # PRESENT | DEVICEINTERFACE
    if not hset:
        raise OSError("SetupDiGetClassDevs failed")
    out = []
    try:
        did = SP_DEVICE_INTERFACE_DATA()
        did.cbSize = ctypes.sizeof(did)
        i = 0
        while setupapi.SetupDiEnumDeviceInterfaces(hset, None, ctypes.byref(g), i, ctypes.byref(did)):
            i += 1
            needed = wt.DWORD(0)
            setupapi.SetupDiGetDeviceInterfaceDetailW(hset, ctypes.byref(did), None, 0,
                                                      ctypes.byref(needed), None)
            if not needed.value:
                continue
            buf = ctypes.create_string_buffer(needed.value)
            # cbSize：x64 = 8、x86 = 6（MSVC 对含变长数组结构体的打包差异）；路径都从偏移 4 起
            ctypes.memset(buf, 0, needed.value)
            ctypes.c_uint32.from_buffer(buf).value = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6
            if not setupapi.SetupDiGetDeviceInterfaceDetailW(hset, ctypes.byref(did), buf,
                                                             needed.value, ctypes.byref(needed), None):
                continue
            path = ctypes.wstring_at(ctypes.addressof(buf) + 4)
            h = k32.CreateFileW(path, 0, FILE_SHARE_RW, None, OPEN_EXISTING, 0, None)
            if h == INVALID_HANDLE:
                h = k32.CreateFileW(path, GENERIC_WRITE, FILE_SHARE_RW, None, OPEN_EXISTING, 0, None)
            if h == INVALID_HANDLE:
                continue
            try:
                attr = HIDD_ATTRIBUTES()
                attr.Size = ctypes.sizeof(attr)
                if not hid.HidD_GetAttributes(h, ctypes.byref(attr)):
                    continue
                if attr.VendorID != vid:
                    continue
                caps = HIDP_CAPS()
                pp = ctypes.c_void_p()
                if hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
                    hid.HidP_GetCaps(pp, ctypes.byref(caps))
                    hid.HidD_FreePreparsedData(pp)
                out.append({
                    "path": path,
                    "vid": attr.VendorID, "pid": attr.ProductID,
                    "version": attr.VersionNumber,
                    "usage": caps.Usage, "usage_page": caps.UsagePage,
                    "input_len": caps.InputReportByteLength,
                    "output_len": caps.OutputReportByteLength,
                    "feature_len": caps.FeatureReportByteLength,
                })
            finally:
                k32.CloseHandle(h)
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(hset)
    return out


def build_report(command_class, command_id, data_size, args=b"", txn=DEFAULT_TXN):
    """构造 91 字节 Feature Report：b[0]=report id(0)，其后是 90 字节 razer_report。"""
    r = bytearray(REPORT_LEN)
    r[2] = txn                      # transaction id
    r[6] = data_size                # arguments 有效长度
    r[7] = command_class
    r[8] = command_id
    r[9:9 + len(args)] = args
    crc = 0
    for i in range(3, 89):          # XOR b[3..88]，写在 b[89]
        crc ^= r[i]
    r[89] = crc
    return bytes(r)


def parse_status(reply):
    return reply[1]


def parse_args(reply, n=None):
    n = reply[6] if n is None else n
    return bytes(reply[9:9 + n])


class Device:
    """一台 Razer 鼠标的控制接口。with Device() as d: ..."""

    def __init__(self, handle=None, path=None, txn=DEFAULT_TXN):
        self.path = path
        self.txn = txn
        self._h = handle
        self._owned = handle is None
        if self._h is None:
            self.path, self._h = self._open_control_interface()

    # ---------- 发现与打开 ----------
    @staticmethod
    def _open_control_interface():
        candidates = [i for i in enumerate_interfaces()
                      if i["feature_len"] >= REPORT_LEN
                      and i["pid"] in PID_DA_V3_PRO]
        if not candidates:
            raise RuntimeError("没找到 Razer 鼠标控制接口（设备没插？PID 不在已知列表？）")
        # 优先选 usage page 1 / usage 2（鼠标主集合），否则取第一个
        candidates.sort(key=lambda i: (i["usage_page"] != 0x01 or i["usage"] != 0x02,
                                       -i["feature_len"]))
        dev = candidates[0]
        h = k32.CreateFileW(dev["path"], GENERIC_WRITE, FILE_SHARE_RW, None,
                            OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE:
            h = k32.CreateFileW(dev["path"], 0, FILE_SHARE_RW, None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE:
            raise OSError(ctypes.get_last_error(),
                          "CreateFileW 失败：用 GENERIC_WRITE 或 0 打开（GENERIC_READ 会被拒绝）")
        return dev["path"], h

    def close(self):
        if self._owned and self._h:
            k32.CloseHandle(self._h)
            self._h = None

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    # ---------- 收发 ----------
    def ask(self, command_class, command_id, data_size, args=b"", txn=None):
        """发一条命令并等回复。返回 (status, reply_bytes)；status==0x02 才是成功。"""
        if self._h is None:
            raise RuntimeError("device closed")
        buf = build_report(command_class, command_id, data_size, args,
                           self.txn if txn is None else txn)
        send = ctypes.create_string_buffer(buf, REPORT_LEN)
        if not hid.HidD_SetFeature(self._h, send, REPORT_LEN):
            raise OSError(ctypes.get_last_error(), "HidD_SetFeature failed")
        reply = ctypes.create_string_buffer(REPORT_LEN)
        for _ in range(MAX_RETRY):
            time.sleep(REPLY_WAIT_S)
            if not hid.HidD_GetFeature(self._h, reply, REPORT_LEN):
                raise OSError(ctypes.get_last_error(), "HidD_GetFeature failed")
            data = reply.raw
            if data[1] != 0x01:          # 0x01 = busy，等设备准备好再读
                return data[1], data
        return 0x01, reply.raw

    # ---------- 已在本机验证的查询 ----------
    def serial(self):
        st, r = self.ask(0x00, 0x82, 0x16)
        return parse_args(r).split(b"\x00")[0].decode("ascii", "replace")

    def firmware(self):
        st, r = self.ask(0x00, 0x81, 0x02)
        a = parse_args(r)
        return f"{a[0]}.{a[1]}" if len(a) >= 2 else ""

    def battery(self):
        """返回百分比 0-255；解析位置是 arguments[1]（老设备是 [2]，见 protocol.md）。"""
        st, r = self.ask(0x07, 0x80, 0x02)
        a = parse_args(r)
        return a[1]

    def charging(self):
        """0 = 未充电，1 = 充电中。"""
        st, r = self.ask(0x07, 0x84, 0x02)
        return parse_args(r)[1]

    def dpi(self):
        """返回 (x, y)。用 NOSTORE(0x00) 查询当前生效值。"""
        st, r = self.ask(0x04, 0x85, 0x07, b"\x00")
        a = parse_args(r)
        return (a[1] << 8 | a[2], a[3] << 8 | a[4])

    def polling(self):
        """返回轮询率 Hz（先查标准 0x00/0x85 的编码表）。"""
        st, r = self.ask(0x00, 0x85, 0x01)
        code = parse_args(r)[0]
        for hz, c in POLL_CODE_STD.items():
            if c == code:
                return hz
        return None


def main(argv):
    what = argv[1] if len(argv) > 1 else "all"
    with Device() as d:
        if what in ("all", "iface"):
            print("control:", d.path)
        if what in ("all", "serial"):
            print("serial :", d.serial())
        if what in ("all", "firmware"):
            print("firmware:", d.firmware())
        if what in ("all", "battery"):
            pct = d.battery()
            print(f"battery: {pct} ({pct * 100 // 255}%)  charging={d.charging()}")
        if what in ("all", "dpi"):
            x, y = d.dpi()
            print(f"dpi    : {x} x {y}")
        if what in ("all", "poll"):
            print("poll   :", d.polling(), "Hz")


if __name__ == "__main__":
    import sys
    main(sys.argv)
