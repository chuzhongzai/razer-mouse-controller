// 具体命令的编解码。所有布局见 protocol.md §5，标 ✅ 的是本机实测过的。
using System;
using System.Collections.Generic;

namespace RazerMouse.Protocol
{
    internal sealed class BatteryInfo
    {
        public int Raw;        // 设备原始刻度（0–255 或 0–100）
        public int Percent;    // 折算后的百分比
        public bool Charging;  // 是否充电中
    }

    internal sealed class DpiStage
    {
        public int X;
        public int Y;

        public DpiStage(int x, int y) { X = x; Y = y; }
        public override string ToString() { return X == Y ? X.ToString() : X + "x" + Y; }
    }

    internal sealed class DpiStages
    {
        public int Active;                       // 当前档位，1 起
        public List<DpiStage> Stages;            // 最多 5 档

        public DpiStages() { Stages = new List<DpiStage>(); }
    }

    internal static class MouseCommands
    {
        public const int MinDpi = 100;
        public const int MaxDpi = 30000;
        public const int MaxStages = 5;

        // ---------- 电量 / 充电 ----------

        /// <summary>✅ 0x07/0x80：args[1] = 电量刻度（本机 187 → 73%）。</summary>
        public static BatteryInfo ReadBattery(MouseDevice device)
        {
            byte[] reply = device.Ask(0x07, 0x80, 0x02, null);
            byte[] args = RazerReport.Args(reply, 2);

            var info = new BatteryInfo();
            info.Raw = args[1];
            info.Percent = ToPercent(args[1]);
            return info;
        }

        /// <summary>✅ 0x07/0x84：args[1] = 0 未充电 / 1 充电中。</summary>
        public static bool ReadCharging(MouseDevice device)
        {
            byte[] reply = device.Ask(0x07, 0x84, 0x02, null);
            return RazerReport.Args(reply, 2)[1] != 0;
        }

        /// <summary>老设备回 0–100，新设备回 0–255；≤100 视为百分数最稳（README §5）。</summary>
        public static int ToPercent(int raw)
        {
            if (raw <= 0) return 0;
            if (raw <= 100) return raw;
            int pct = (raw * 100 + 127) / 255;
            return pct > 100 ? 100 : pct;
        }

        // ---------- DPI ----------

        /// <summary>✅ 0x04/0x85：args[1..2]=X、args[3..4]=Y（大端）。onboard=true 读板载值。</summary>
        public static DpiStage ReadDpi(MouseDevice device, bool onboard)
        {
            byte[] reply = device.Ask(0x04, 0x85, 0x07, new byte[] { onboard ? (byte)0x01 : (byte)0x00 });
            byte[] args = RazerReport.Args(reply, 7);
            int x = (args[1] << 8) | args[2];
            int y = (args[3] << 8) | args[4];
            return new DpiStage(x, y);
        }

        /// <summary>0x04/0x05：args=[store, xHi, xLo, yHi, yLo, 0, 0]（写命令，写完必须读回）。</summary>
        public static void WriteDpi(MouseDevice device, int x, int y, bool store)
        {
            CheckDpi(x);
            CheckDpi(y);
            var args = new byte[7];
            args[0] = store ? (byte)0x01 : (byte)0x00;
            args[1] = (byte)(x >> 8);
            args[2] = (byte)(x & 0xFF);
            args[3] = (byte)(y >> 8);
            args[4] = (byte)(y & 0xFF);
            device.Ask(0x04, 0x05, 0x07, args);
        }

        // ---------- DPI 档位 ----------

        /// <summary>✅ 0x04/0x86：回包固定 38 字节，不能用 b[6] 当长度（Phase 0 实测坑）。</summary>
        public static DpiStages ReadDpiStages(MouseDevice device)
        {
            byte[] reply = device.Ask(0x04, 0x86, 0x26, new byte[] { 0x00 });
            byte[] args = RazerReport.Args(reply, 38);

            var result = new DpiStages();
            result.Active = args[1];
            int count = args[2];
            if (count < 1 || count > MaxStages) count = MaxStages;

            for (int i = 0; i < count; i++)
            {
                int offset = 3 + i * 7;
                int x = (args[offset + 1] << 8) | args[offset + 2];
                int y = (args[offset + 3] << 8) | args[offset + 4];
                result.Stages.Add(new DpiStage(x, y));
            }
            return result;
        }

        /// <summary>0x04/0x06：args=[store, 当前档(1起), 档数, (序号, xHi,xLo,yHi,yLo,0,0)×5]，共 38 字节。</summary>
        public static void WriteDpiStages(MouseDevice device, DpiStages stages, bool store)
        {
            if (stages == null || stages.Stages.Count == 0)
                throw new ArgumentException("档位表为空");
            if (stages.Stages.Count > MaxStages)
                throw new ArgumentException("最多 5 档");

            var args = new byte[38];
            args[0] = store ? (byte)0x01 : (byte)0x00;
            args[1] = (byte)Clamp(stages.Active, 1, stages.Stages.Count);
            args[2] = (byte)stages.Stages.Count;

            for (int i = 0; i < MaxStages; i++)
            {
                int offset = 3 + i * 7;
                if (i >= stages.Stages.Count) continue;   // 多余槽位保持 0
                DpiStage stage = stages.Stages[i];
                CheckDpi(stage.X);
                CheckDpi(stage.Y);
                args[offset] = (byte)(i + 1);
                args[offset + 1] = (byte)(stage.X >> 8);
                args[offset + 2] = (byte)(stage.X & 0xFF);
                args[offset + 3] = (byte)(stage.Y >> 8);
                args[offset + 4] = (byte)(stage.Y & 0xFF);
            }
            device.Ask(0x04, 0x06, 0x26, args);
        }

        // ---------- 回报率 ----------

        /// <summary>标准 encoding：1000→0x01 / 500→0x02 / 125→0x08。</summary>
        public static readonly int[] StandardPollingRates = { 125, 500, 1000 };

        /// <summary>HyperPolling encoding（0x00/0x40）：8K→0x01 / 4K→0x02 / 2K→0x04 / 1K→0x08 / 500→0x10 / 250→0x20 / 125→0x40。</summary>
        public static readonly int[] HyperPollingRates = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        /// <summary>✅ 0x00/0x85：args[0] = 编码。</summary>
        public static int ReadPollingCode(MouseDevice device)
        {
            byte[] reply = device.Ask(0x00, 0x85, 0x01, null);
            return RazerReport.Args(reply, 1)[0];
        }

        /// <summary>把标准编码翻成 Hz；不认识返回 0。</summary>
        public static int StandardCodeToHz(int code)
        {
            switch (code)
            {
                case 0x01: return 1000;
                case 0x02: return 500;
                case 0x08: return 125;
                default: return 0;
            }
        }

        public static int HzToStandardCode(int hz)
        {
            switch (hz)
            {
                case 1000: return 0x01;
                case 500: return 0x02;
                case 125: return 0x08;
                default: return -1;
            }
        }

        /// <summary>0x00/0x05（标准）：args=[code]；失败再试 0x00/0x40（HyperPolling）：args=[0x00, code]。</summary>
        public static void WritePolling(MouseDevice device, int hz)
        {
            int code = HzToStandardCode(hz);
            if (code < 0) throw new ArgumentException("不支持的回报率：" + hz + " Hz");

            try
            {
                device.Ask(0x00, 0x05, 0x01, new byte[] { (byte)code });
            }
            catch (RazerException ex)
            {
                if (ex.Status != 0x05 && ex.Status != 0x03) throw;
                device.Ask(0x00, 0x40, 0x02, new byte[] { 0x00, (byte)code });
            }
        }

        // ---------- 设备信息 ----------

        /// <summary>✅ 0x00/0x82：args[0..17] ASCII 序列号。</summary>
        public static string ReadSerial(MouseDevice device)
        {
            byte[] reply = device.Ask(0x00, 0x82, 0x16, null);
            byte[] args = RazerReport.Args(reply, 22);
            int length = 0;
            while (length < args.Length && args[length] != 0) length++;
            return System.Text.Encoding.ASCII.GetString(args, 0, length);
        }

        /// <summary>✅ 0x00/0x81：args[0].args[1] = 主.次。</summary>
        public static string ReadFirmware(MouseDevice device)
        {
            byte[] reply = device.Ask(0x00, 0x81, 0x02, null);
            byte[] args = RazerReport.Args(reply, 2);
            return args[0] + "." + args[1];
        }

        // ---------- 工具 ----------

        private static void CheckDpi(int value)
        {
            if (value < MinDpi || value > MaxDpi)
                throw new ArgumentOutOfRangeException("dpi",
                    string.Format("DPI 必须在 {0}–{1} 之间", MinDpi, MaxDpi));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
