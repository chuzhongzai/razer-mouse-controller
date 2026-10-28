# Razer 鼠标 HID 协议

面向 Windows：用 **Feature Report**（`HidD_SetFeature` / `HidD_GetFeature`）和设备对话。Linux 上 OpenRazer 用的是等价的 USB control transfer（`HID_REQ_SET_REPORT`，report index `0x02`），两边的 payload 完全一样，所以 OpenRazer 的源码就是本协议的权威文档。

## 1. 报文结构：91 字节

Windows 的 feature report 带 1 字节 report id，所以缓冲区是 **1 + 90 = 91 字节**；OpenRazer 的 `struct razer_report` 正好 90 字节（`static_assert(sizeof(struct razer_report) == 90)`）。

| 偏移（91 字节缓冲区） | 长度 | 字段 | 说明 |
|---|---|---|---|
| `b[0]` | 1 | report id | 固定 `0x00`（设备用编号 0 的 feature report） |
| `b[1]` | 1 | status | 0x00 新命令 / 0x01 busy / 0x02 成功 / 0x03 失败 / 0x04 超时 / 0x05 不支持 |
| `b[2]` | 1 | transaction_id | 请求-回复配对用；本机 `0x1F` 可用，`0xFF` 会超时（见 §3） |
| `b[3..4]` | 2 | remaining_packets | 大端，多包序列才用，一般 `0x0000` |
| `b[5]` | 1 | protocol_type | 固定 `0x00` |
| `b[6]` | 1 | data_size | arguments 的有效字节数（≤ 80） |
| `b[7]` | 1 | command_class | 命令类别，见 §5 |
| `b[8]` | 1 | command_id | 命令号，最高位是方向位，见 §5 |
| `b[9..88]` | 80 | arguments | 参数/数据区 |
| `b[89]` | 1 | crc | 校验和 |
| `b[90]` | 1 | reserved | `0x00` |

对照 OpenRazer 结构体：`status`=b[1]、`transaction_id`=b[2]、`remaining_packets`=b[3..4]、`protocol_type`=b[5]、`data_size`=b[6]、`command_class`=b[7]、`command_id`=b[8]、`arguments[0]`=b[9]、`crc`=b[89]、`reserved`=b[90]。

## 2. 校验和

```python
crc = 0
for i in range(3, 89):   # 即 OpenRazer 里的 for (i = 2; i < 88; i++)
    crc ^= buf[i]
buf[89] = crc
```

就是 b[3] 到 b[88] 的逐字节 XOR（含 remaining_packets / protocol_type / data_size / class / id / arguments，**不含** report id、status、transaction id 和最后的 reserved）。发送端和回包用同一算法，回包 CRC 校验不过说明设备没正确接收。

## 3. 时序：这是最容易踩的地方

```
SetFeature(请求)  →  等 ~60ms  →  GetFeature(回复)
```

- **不能** SetFeature 完立刻 GetFeature：拿回来的是请求回显，status = `0x01`（busy）。
- 正确做法：固定等 60ms，然后读；若 status 仍是 `0x01`，继续等/重试（本机实测 1～2 次即可拿到 0x02），上限 5 次算失败。
- transaction id 用 `0x1F` 实测稳定；**换成 `0xFF` 时本机返回 `0x04`（超时/无响应）**。OpenRazer 对 `txn==0` 的请求会强改成 `0xFF`（老设备习惯），但灯效类命令在 open-razerkit 里对 DA V3 Pro 用的是 `0xFF` —— 说明 txn 因命令类别而异，写的时候允许按命令覆盖。

## 4. 状态码

| 值 | 含义 | 处理 |
|---|---|---|
| `0x00` | 新命令（请求里用） | — |
| `0x01` | busy | 等一会儿再 GetFeature |
| `0x02` | 成功 | 解析 arguments |
| `0x03` | 失败 | 参数不对/设备拒绝 |
| `0x04` | 超时/无响应 | txn 或命令不支持；换 txn 再试 |
| `0x05` | 不支持 | 该型号没有这条命令 |

## 5. 命令类别（[实测] / [参考]）

`command_id` 最高位是方向位：`0x80` 起为读（host→device 请求、device→host 回复），低位为写。所以同一功能通常成对出现：读 `0x85` / 写 `0x05`、读 `0x84` / 写 `0x04`。

### 5.1 已在本机验证（DA V3 Pro 无线，1532:00B7）

| 功能 | class/id | data_size | 请求 args | 回包解析 | 实测回包 |
|---|---|---|---|---|---|
| 电量 | `0x07/0x80` | `0x02` | — | args[1] = 0–255 | `...-80-00-BB-00` → 187 |
| 充电状态 | `0x07/0x84` | `0x02` | — | args[1] = 0/1 | `...-84-00-00-00` → 0 |
| 序列号 | `0x00/0x82` | `0x16` | — | args[0..17] ASCII | `PM2635H26790678` |
| 固件 | `0x00/0x81` | `0x02` | — | args[0].args[1] | `02 02` → 2.2 |
| DPI | `0x04/0x85` | `0x07` | `[0x00]`=当前生效 / `[0x01]`=板载 | args[1..2]=X、args[3..4]=Y（大端） | `0C 80 0C 80` → 3200×3200 |
| 轮询率 | `0x00/0x85` | `0x01` | — | args[0] = 编码 | `01` → 1000 Hz |
| 设备模式 | `0x00/0x84` | `0x02` | `[0x00,0x00]` | args[0] | `00` = 普通 HID 模式 |

### 5.2 参考实现里的写命令（[参考]，本机未验证）

来自 open-razerkit `src/drivers/protocol.py`，格式可信但请逐条试、写完读回。

| 功能 | class/id | data_size | args |
|---|---|---|---|
| 设 DPI | `0x04/0x05` | `0x07` | `[0x01, xHi, xLo, yHi, yLo, 0, 0]`，100–30000 |
| 设 DPI 档位 | `0x04/0x06` | `0x26` | `[store, 当前档(1 起), 档数, (序号, xHi,xLo,yHi,yLo, 0,0)×n]`，≤5 档 |
| 设轮询率（标准） | `0x00/0x05` | `0x01` | `[code]`：1000→`0x01`，500→`0x02`，125→`0x08` |
| 设轮询率（HyperPolling） | `0x00/0x40` | `0x02` | `[0x00, code]`：8K→`0x01`，4K→`0x02`，2K→`0x04`，1K→`0x08`，500→`0x10`，250→`0x20`，125→`0x40` |
| 设备模式 | `0x00/0x04` | `0x02` | `[0x03, 0x00]` = 驱动模式，`[0x00, 0x00]` = 普通 |
| 滚轮模式/加速 | `0x02/0x14` `0x02/0x16` | `0x02` | `[store, 值]` |
| logo 静态色 | `0x03/0x01` | `0x05` | `[store, 0x04, r, g, b]` |
| logo 效果 | `0x03/0x02` | `0x03` | `[store, 0x04, effect]`：0 静态 / 1 闪烁 / 2 呼吸 / 4 光谱 |
| logo 开关 | `0x03/0x00` | `0x03` | `[store, 0x04, 0/1]` |
| logo 亮度 | `0x03/0x03` | `0x03` | `[store, 0x04, 0..255]` |

`store`：`0x00` 临时（掉电丢）、`0x01` 写进板载内存（DA V3 Pro 只有 1 格）。

## 6. 坑清单（都是踩过的）

1. **读权限会被拒**：`CreateFileW(..., GENERIC_READ, ...)` 一律 `ERROR_ACCESS_DENIED`（5）。必须用 `GENERIC_WRITE`（0x40000000）或 `0` 打开。这跟 Synapse 在不在跑无关。
2. 共享模式给 `FILE_SHARE_READ|FILE_SHARE_WRITE`（3）；共享模式 0 会 `ERROR_SHARING_VIOLATION`（32）。
3. 缓冲区必须 **91 字节**（带 report id）。给 90 字节成功不了。
4. 发完立刻读是错的，见 §3 的 60ms。
5. `txn` 不是随便填的，见 §3 末尾。
6. 路径会变：设备重新插拔/换 USB 口/休眠唤醒后接口路径可能不同 —— 每次启动重新枚举，别硬编码。
7. 只有 `usagePage=1, usage=2, FeatureReportByteLength≥91` 的那个接口能收发厂家命令，别的 HID 口都不理。
8. 一个设备同一时刻最好只由你的程序操作；多个程序同时发命令会互相抢回包（表现为状态乱跳）。
9. 写入后**必须读回确认**：状态 `0x02` 只代表设备收了命令，不代表值生效。
10. 电量刻度可能是 0–255 也可能是 0–100（老设备）。DA V3 Pro 已确认是 **0–255**（充满读到 255），必须一律 `x/255`；"≤100 当百分数"的猜测只适用于刻度未知的型号，否则电量低于 40% 后会读出假的高值并出现"反弹"。

## 7. 调用骨架（Python，实测通过的写法）

```python
import ctypes, time
k32 = ctypes.WinDLL("kernel32"); hid = ctypes.WinDLL("hid")

h = k32.CreateFileW(path, 0x40000000, 3, None, 3, 0, None)      # GENERIC_WRITE
req = bytearray(91)
req[2] = 0x1F; req[6] = 0x02; req[7] = 0x07; req[8] = 0x80      # txn / size / class / id
crc = 0
for i in range(3, 89): crc ^= req[i]
req[89] = crc
hid.HidD_SetFeature(h, bytes(req), 91)

resp = bytearray(91)
for _ in range(5):
    time.sleep(0.06)
    hid.HidD_GetFeature(h, resp, 91)
    if resp[1] != 0x01: break
print(resp[1], resp[10])   # status, arguments[1] = 电量
```

完整可运行版本见 `samples/razer_hid.py`（枚举 + 解析 + 异常处理）。

## 8. 参考

- OpenRazer `driver/razercommon.h` / `razercommon.c`：结构体、状态码、CRC 的权威定义
  https://github.com/openrazer/openrazer/tree/master/driver
- open-razerkit `src/drivers/protocol.py`：Windows/Linux 两用的报文构造与命令表
  https://github.com/HamzaYslmn/open-razerkit/blob/main/src/drivers/protocol.py
- open-razerkit `src/drivers/mouse.py`：型号表（PID → 灯效方法 / LED id / txn）
  https://github.com/HamzaYslmn/open-razerkit/blob/main/src/drivers/mouse.py
