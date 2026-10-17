"""phase0 只读探测：Razer DeathAdder V3 Pro (1532:00B7) HID 厂商协议。

红线：本脚本只构造 command_id >= 0x80 的读命令；对发送前的报文做断言，
任何 command_id < 0x80 的请求会直接抛异常，绝不下发。

用法：python probe_phase0.py [输出 json 路径]
"""

import ctypes
import json
import os
import sys
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
REPORT_LEN = 91
DEFAULT_TXN = 0x1F
REPLY_WAIT_S = 0.06
MAX_RETRY = 5

STATUS = {0x00: "new", 0x01: "busy", 0x02: "ok", 0x03: "failure",
          0x04: "timeout/no-response", 0x05: "not-supported"}


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
    g = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(g))
    hset = setupapi.SetupDiGetClassDevsW(ctypes.byref(g), None, None, 0x12)
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


def crc_of(buf):
    c = 0
    for i in range(3, 89):
        c ^= buf[i]
    return c


def build_report(command_class, command_id, data_size, args=b"", txn=DEFAULT_TXN):
    if command_id < 0x80:
        raise ValueError("红线：禁止下发非读命令 id=0x%02X" % command_id)
    r = bytearray(REPORT_LEN)
    r[2] = txn
    r[6] = data_size
    r[7] = command_class
    r[8] = command_id
    r[9:9 + len(args)] = args
    r[89] = crc_of(r)
    return bytes(r)


def hx(b, n=None):
    if n is not None:
        b = b[:n]
    return " ".join("%02X" % x for x in b)


class Probe:
    def __init__(self, handle, path):
        self.h = handle
        self.path = path
        self.records = []

    def ask(self, label, cls, cid, ds, args=b"", txn=DEFAULT_TXN, note=""):
        req = build_report(cls, cid, ds, args, txn)
        t0 = time.perf_counter()
        ok = hid.HidD_SetFeature(self.h, ctypes.create_string_buffer(req, REPORT_LEN), REPORT_LEN)
        err = ctypes.get_last_error() if not ok else 0
        reply = None
        attempts = 0
        statuses = []
        if ok:
            buf = ctypes.create_string_buffer(REPORT_LEN)
            for _ in range(MAX_RETRY):
                attempts += 1
                time.sleep(REPLY_WAIT_S)
                if not hid.HidD_GetFeature(self.h, buf, REPORT_LEN):
                    err = ctypes.get_last_error()
                    break
                data = buf.raw
                statuses.append(data[1])
                if data[1] != 0x01:
                    reply = data
                    break
            if reply is None:
                reply = buf.raw
        ms = (time.perf_counter() - t0) * 1000.0
        st = reply[1] if reply else None
        rec = {
            "label": label,
            "request": {"class": cls, "id": cid, "id_hex": "0x%02X" % cid,
                        "data_size": ds, "args_hex": hx(args), "txn": txn,
                        "bytes_91_hex": hx(req)},
            "set_feature_ok": bool(ok),
            "get_feature_error": err,
            "status": st,
            "status_name": STATUS.get(st, "?"),
            "statuses_seen": statuses,
            "attempts": attempts,
            "elapsed_ms": round(ms, 2),
            "reply_91_hex": hx(reply) if reply else "",
            "reply_first40_hex": hx(reply, 40) if reply else "",
            "reply_args_first40_hex": hx(reply[9:49]) if reply else "",
            "reply_data_size_echo": reply[6] if reply else None,
            "reply_txn_echo": reply[2] if reply else None,
            "reply_crc_ok": (reply[89] == crc_of(reply)) if reply else None,
            "note": note,
        }
        self.records.append(rec)
        print("[%s] cls=%02X id=%02X ds=%02X args=%-12s txn=%02X -> status=%s(%s) ms=%.1f  reply[:20]=%s"
              % (label, cls, cid, ds, hx(args) or "-", txn,
                 st, STATUS.get(st, "?"), ms, hx(reply, 20) if reply else "-"))
        return rec


def parse_dpi_stages(reply, force_len=0x26):
    """按 protocol.md 0x04/0x06 布局解析：args=[store, current, count, (idx,xHi,xLo,yHi,yLo,0,0)*n]

    注意：0x04/0x86 的回包 b[6] 只是把请求里的 data_size 原样回显，不代表有效长度
    （ds=0x00 时 b[6]=0 但 args 里 38 字节照样齐），所以这里固定按 0x26 取。
    """
    a = reply[9:9 + force_len]
    if len(a) < 3:
        return {"ok": False, "reason": "args too short", "args_hex": hx(a)}
    store, current, count = a[0], a[1], a[2]
    stages = []
    p = 3
    while p + 6 <= len(a) and len(stages) < 8:
        idx = a[p]
        x = (a[p + 1] << 8) | a[p + 2]
        y = (a[p + 3] << 8) | a[p + 4]
        stages.append({"idx": idx, "x": x, "y": y, "raw": hx(a[p:p + 7])})
        p += 7
    return {"ok": True, "store": store, "current": current, "count": count,
            "stages": stages, "parsed_bytes": p, "args_len": len(a),
            "args_hex": hx(a), "leftover_hex": hx(a[p:])}


def main():
    out_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "phase0_raw.json")

    doc = {"generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
           "host_python": sys.version.split()[0],
           "red_line": "只发 command_id >= 0x80 的读命令；脚本对 cid<0x80 断言抛错"}

    ifaces = enumerate_interfaces()
    doc["enumeration"] = {"vid": "0x%04X" % RAZER_VID, "count": len(ifaces),
                          "interfaces": ifaces}
    print("=== 枚举到 %d 个 VID=1532 接口 ===" % len(ifaces))
    for i in ifaces:
        print("  pid=%04X up=%04X usage=%04X feat=%3d in=%3d out=%3d ver=%04X\n     %s"
              % (i["pid"], i["usage_page"], i["usage"], i["feature_len"],
                 i["input_len"], i["output_len"], i["version"], i["path"]))

    # 选控制口：feature_len >= 91 且 usage_page=1 且 pid 为已知 DA V3 Pro 系列
    cand = [i for i in ifaces if i["feature_len"] >= REPORT_LEN and i["usage_page"] == 0x01]
    cand.sort(key=lambda i: (i["pid"] not in (0x00B6, 0x00B7, 0x00C2, 0x00C3), -i["feature_len"]))
    if not cand:
        doc["error"] = "没有 feature_len>=91 且 usage_page=1 的接口"
        json.dump(doc, open(out_path, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
        print("!! 未找到控制口，已写", out_path)
        return 2
    ctl = cand[0]
    doc["control_interface"] = ctl
    print("control:", ctl["path"])

    h = k32.CreateFileW(ctl["path"], GENERIC_WRITE, FILE_SHARE_RW, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE:
        h = k32.CreateFileW(ctl["path"], 0, FILE_SHARE_RW, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE:
        doc["error"] = "CreateFileW failed err=%d" % ctypes.get_last_error()
        json.dump(doc, open(out_path, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
        print("!! 打不开控制口", doc["error"])
        return 3

    p = Probe(h, ctl["path"])
    try:
        # ---- 2. 已 验证的读 ----
        p.ask("serial", 0x00, 0x82, 0x16)
        p.ask("firmware", 0x00, 0x81, 0x02)
        for i in range(3):
            p.ask("battery#%d" % (i + 1), 0x07, 0x80, 0x02)
        p.ask("charging", 0x07, 0x84, 0x02)
        p.ask("polling", 0x00, 0x85, 0x01)
        p.ask("device_mode", 0x00, 0x84, 0x02)
        # ---- 3. DPI ----
        p.ask("dpi_active", 0x04, 0x85, 0x07, b"\x00")
        p.ask("dpi_stored", 0x04, 0x85, 0x07, b"\x01")
        # ---- 4. DPI 档位读：3x3 矩阵 ----
        stage_recs = []
        for ds in (0x26, 0x00, 0x01):
            for args in (b"", b"\x00", b"\x01"):
                rec = p.ask("dpi_stages_ds%02X_a%s" % (ds, (hx(args).replace(" ", "") or "empty")),
                            0x04, 0x86, ds, args)
                stage_recs.append(rec)
        doc["dpi_stage_parse"] = []
        for rec in stage_recs:
            if rec["status"] == 0x02:
                doc["dpi_stage_parse"].append(
                    {"label": rec["label"],
                     "request": {"data_size": rec["request"]["data_size"],
                                 "args_hex": rec["request"]["args_hex"]},
                     "reply_data_size_echo": rec["reply_data_size_echo"],
                     "parsed": parse_dpi_stages(bytes.fromhex(rec["reply_91_hex"]))})
        # ---- 4b. 交叉校验：回包里的当前档 DPI 应等于 0x04/0x85 的当前生效 DPI ----
        try:
            base = [r for r in stage_recs if r["label"] == "dpi_stages_ds26_aempty"][0]
            parsed0 = parse_dpi_stages(bytes.fromhex(base["reply_91_hex"]))
            act = [r for r in p.records if r["label"] == "dpi_active"][0]
            a_act = bytes.fromhex(act["reply_91_hex"])[9:16]
            dx = (a_act[1] << 8) | a_act[2]
            cur = parsed0["stages"][parsed0["current"] - 1] if parsed0.get("ok") else None
            doc["dpi_stage_cross_check"] = {
                "current_index_1based": parsed0.get("current"),
                "current_stage": cur,
                "dpi_active_xy": [dx, (a_act[3] << 8) | a_act[4]],
                "match": bool(cur and cur["x"] == dx and cur["y"] == ((a_act[3] << 8) | a_act[4])),
            }
        except Exception as e:  # noqa: BLE001
            doc["dpi_stage_cross_check"] = {"error": repr(e)}

        # ---- 5. txn 稳定性 ----
        for txn in (0x1F, 0xFF, 0x3F):
            for k in range(2):
                p.ask("txn%02X#%d" % (txn, k + 1), 0x07, 0x80, 0x02, txn=txn)
        # ---- 6. 单次耗时样本：再读 10 次 txn=0x1F 的电量 ----
        for k in range(10):
            p.ask("latency#%d" % (k + 1), 0x07, 0x80, 0x02)
    finally:
        k32.CloseHandle(h)

    doc["probes"] = p.records

    # ---- 汇总 ----
    ok = [r for r in p.records if r["status"] == 0x02]
    lat = sorted(r["elapsed_ms"] for r in ok)
    doc["summary"] = {
        "total_probes": len(p.records),
        "ok": len(ok),
        "status_histogram": {STATUS.get(k, str(k)): sum(1 for r in p.records if r["status"] == k)
                             for k in sorted({r["status"] for r in p.records})},
        "latency_ok_ms": {"n": len(lat), "min": lat[0] if lat else None,
                          "max": lat[-1] if lat else None,
                          "avg": round(sum(lat) / len(lat), 2) if lat else None,
                          "median": lat[len(lat) // 2] if lat else None},
    }

    # 便于人读的结论块（同一条数据，不引入新探测）
    txn_groups = {}
    for r in p.records:
        if r["label"].startswith("txn"):
            key = "0x%02X" % r["request"]["txn"]
            txn_groups.setdefault(key, []).append(
                {"status": r["status"], "ms": r["elapsed_ms"]})
    stage_ok = [x for x in doc.get("dpi_stage_parse", []) if x["parsed"].get("ok")]
    doc["findings"] = {
        "control_interface_unique": len([i for i in ifaces
                                         if i["feature_len"] >= REPORT_LEN and i["usage_page"] == 0x01]) == 1,
        "dpi_stages_readable": len(stage_ok) > 0,
        "dpi_stages_working_combo": {"class": "0x04", "id": "0x86", "data_size": "0x26",
                                     "args": "empty or [0x00]",
                                     "reply_args_len_fixed": 0x26,
                                     "reply_data_size_echo_is_request_echo": True},
        "dpi_stages": (stage_ok[0]["parsed"] if stage_ok else None),
        "dpi_stages_cross_check": doc.get("dpi_stage_cross_check"),
        "txn_results": txn_groups,
        "latency_ok_ms": doc["summary"]["latency_ok_ms"],
        "busy_ever_seen": any(0x01 in r["statuses_seen"] for r in p.records),
        "crc_all_ok": all(r["reply_crc_ok"] for r in p.records if r["status"] is not None),
    }
    json.dump(doc, open(out_path, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
    print("\n=== summary ===")
    print(json.dumps(doc["summary"], indent=2, ensure_ascii=False))
    print("written:", out_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
