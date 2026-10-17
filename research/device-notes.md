# Razer DeathAdder V3 Pro 设备笔记（实测记录）

> 报文格式、CRC、时序、命令表见 [`../protocol.md`](../protocol.md)；这份笔记留的是**设备身份、实测原始回包、电量刻度坑、能力边界**这些当时踩出来的东西。
> 目标设备：**Razer DeathAdder V3 Pro（无线 dongle，1532:00B7）**。
> 所有事实分两类，明确标注：

- **[实测]** —— 在本机这台鼠标上跑通、抓到了真实回包（2026-10-17，Synapse 已卸载、设备走 Windows 自带 HID 驱动）。
- **[参考]** —— 来自开源项目源码，命令格式可信，但**没在本机验证过**（写入类命令尤其如此）。

## 1. 设备身份

| 项目 | 值 |
|---|---|
| 型号 | Razer DeathAdder V3 Pro |
| 厂商 ID (VID) | `0x1532` |
| 产品 ID (PID) | `0x00B7` 无线 dongle（有线 `0x00B6`；另有 `0x00C2` / `0x00C3` 同型号变体） |
| 设备实例 | `HID\VID_1532&PID_00B7&MI_00\8&1E2B9B13&0&0000` |
| 控制接口路径 | `\\?\hid#vid_1532&pid_00b7&mi_00#8&1e2b9b13&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}` |
| 接口属性 | usagePage `0x0001`、usage `0x0002`、FeatureReportByteLength **91** |
| 序列号 | `PM2635H26790678` [实测] |
| 固件 | `2.2` [实测，0x00/0x81 回包 args[0]=2 args[1]=2] |

一台鼠标会暴露十来个 HID 接口（鼠标/键盘/消费控制/系统控制…），**只有 FeatureReportByteLength ≥ 91 且 usagePage=1 的那一个是厂家控制口**。别按路径硬编码，每次启动枚举一遍即可（见 `samples/razer_hid.py`）。

## 2. 快速开始

```bash
# Python 3.13（零依赖，ctypes 直调 hid.dll）—— 已验证
python samples/razer_hid.py            # 一次读出序列号/固件/电量/DPI/轮询率
python samples/razer_hid.py battery    # 只读电量

# PowerShell（Add-Type 内嵌 C#）—— 已验证
pwsh -File samples/battery.ps1
```

实测输出：

```
control: \\?\hid#vid_1532&pid_00b7&mi_00#8&1e2b9b13&0&0000#{...}
serial : PM2635H26790678
firmware: 2.2
battery: 187 (73%)  charging=0
dpi    : 3200 x 3200
poll   : 1000 Hz
```

**不需要**驱动、管理员权限、Synapse、hidapi 或任何第三方包。调用方式只有三个 Win32 API：

```
CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ|WRITE, ..., OPEN_EXISTING, 0, 0)
HidD_SetFeature(h, buf91, 91)     // 发命令
Sleep(60ms); HidD_GetFeature(h, buf91, 91)   // 收回复，busy 就重试
```

## 3. 已验证能力（本机实测）

| 能力 | 命令 (class/id) | data_size | 回包要点 |
|---|---|---|---|
| 读电量 | `0x07 / 0x80` | `0x02` | args[1]=187（0–255 刻度） |
| 读充电状态 | `0x07 / 0x84` | `0x02` | args[1]=0 未充电 / 1 充电中 |
| 读序列号 | `0x00 / 0x82` | `0x16` | args[0..17] ASCII |
| 读固件版本 | `0x00 / 0x81` | `0x02` | args[0].args[1] = 2.2 |
| 读 DPI | `0x04 / 0x85` | `0x07` | args[1..2]=X、args[3..4]=Y（大端）→ 0x0C80=3200 |
| 读轮询率 | `0x00 / 0x85` | `0x01` | args[0]=0x01 → 1000 Hz |
| 读设备模式 | `0x00 / 0x84` | `0x02` | args[0]=0x00（普通 HID 模式） |

原始回包（每条都是 91 字节的前 14 字节）：

```
电量   00-02-1F-00-00-00-02-07-80-00-BB-00-00-00
充电   00-02-1F-00-00-00-02-07-84-00-00-00-00-00
序列号 00-02-1F-00-00-00-16-00-82-50-4D-32-33-36  ← "PM2635H..."
固件   00-02-1F-00-00-00-02-00-81-02-02-00-00-00
DPI    00-02-1F-00-00-00-07-04-85-00-0C-80-0C-80
轮询   00-02-1F-00-00-00-01-00-85-01-00-00-00-00
```

## 4. 能力边界（先想清楚再写）

**能做（走 HID 协议）**：电量/充电、序列号/固件、DPI 读写、轮询率读写（含 HyperPolling 8K 的 0x00/0x40 变体，待验证）、灯效（DA V3 Pro 只有 logo 一个灯区，见 §6）、设备模式切换。

**不能做，别再花时间**：

- **按键重映射**：协议里没有"改按键功能"的通用命令。要么写板载 profile 的私有格式（未解码），要么在软件层做鼠标钩子 / 低级鼠标钩子把按键转成别的动作 —— 那是另一条技术路线。
- **宏录制**：同上，属于软件层。
- **固件升级**：Razer 只对自家签名的升级包开这条路，第三方无法实现。
- **多套板载配置**：DA V3 Pro 只有 **1 组**板载内存，当前设置即唯一配置（要"多套"只能存在你的程序里，切换时重新下发给鼠标）。

## 5. 电量刻度的一个坑

回包给的是 **187**，不是 100 以内的百分数。按 `x/255` 折算是 **73%**。老设备（如 Viper 系列）有时直接回报 0–100，读数时按"**≤100 视为百分数，>100 视为 0–255 刻度**"处理最稳。要彻底确认刻度，可以在电量 100% 时读一次：回 255 → 0–255 刻度；回 100 → 百分数。

## 6. 灯效（参考实现，未在本机验证）

DA V3 Pro 只有一个灯区（logo，LED id `0x04`），`open-razerkit` 给它的灯效方法标记为 `logo` + `txn 0xFF`：

```
静态色  0x03/0x01  args=[store, led, r, g, b]         // store: 0x01 存板载, 0x00 临时
效果    0x03/0x02  args=[store, led, effect]          // 0x00 静态 0x01 闪烁 0x02 呼吸 0x04 光谱
开关    0x03/0x00  args=[store, led, on/off]
亮度    0x03/0x03  args=[store, led, 0..255]
```

多灯区设备才有 `0x0F/0x02` 扩展灯效（波浪/反应/星光）和逐键矩阵 —— 单灯区设备调这些会返回 not-supported。

## 7. 目录

```
README.md          本文件：设备身份、已验证清单、边界、路线建议
protocol.md        报文格式、CRC、时序、状态码、完整命令表（含未验证项）
samples/razer_hid.py   零依赖 Python 客户端（枚举 + 7 个查询，实测通过）
samples/battery.ps1    零依赖 PowerShell 读电量（实测通过）
```

## 8. 下一步建议

1. 先只读跑通（本目录两个 sample 已经是了），再逐条验证写入类命令：**每次写完立刻读回**，值不对就说明参数布局错了。
2. 写操作一律先 `store=0x00`（临时）试，确认效果对了再 `store=0x01` 存板载 —— 板载只有一格，写坏了只能重设。
3. 轮询率：先验证 `0x00/0x85` 的读回编码，再试 `0x00/0x05` 写；8K 需要兼容的 dongle，走 `0x00/0x40` 那套编码（本机未验证）。
4. 做 GUI 时把 HID 调用放到后台线程：一次查询约 60–70ms（固定等待设备响应），同步调用会卡界面。
5. 程序启动时枚举设备、退出时还原自己改过的项（尤其是 DPI/轮询率这类影响手感的）。

## 9. 参考资料

- OpenRazer（Linux 驱动，协议事实上的标准文档）：https://github.com/openrazer/openrazer
  - `driver/razercommon.h`（`razer_report` 结构、状态码、LED id）、`driver/razercommon.c`（CRC 算法）
- open-razerkit（Windows/Linux 纯 Python 实现，本目录协议细节主要据此交叉验证）：https://github.com/HamzaYslmn/open-razerkit
- Non-Synapse-Mouse（Windows 单 exe 的 HID 客户端）：https://github.com/Damnedapa/Non-Synapse-Mouse
