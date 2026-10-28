// --selftest：不开界面，把全部读写结果打到控制台，用于排错与回归。
using System;
using System.Diagnostics;
using RazerMouse.Protocol;

namespace RazerMouse
{
    internal static class SelfTest
    {
        public static int Run(string[] args)
        {
            bool writeTest = Array.IndexOf(args, "--write-test") >= 0;

            if (Array.IndexOf(args, "--batprobe") >= 0) return RunBatteryProbe(args);

            try
            {
                using (MouseDevice device = MouseDevice.Open())
                {
                    Console.WriteLine("控制口 : " + device.Path);
                    Console.WriteLine("PID    : 0x" + device.Pid.ToString("X4") +
                                      "    版本: " + device.Version +
                                      "    feature: " + device.FeatureLength + " 字节");
                    Console.WriteLine();

                    Stopwatch watch = Stopwatch.StartNew();
                    string serial = MouseCommands.ReadSerial(device);
                    watch.Stop();
                    Console.WriteLine(string.Format("序列号 : {0}   ({1} ms)", serial, watch.ElapsedMilliseconds));

                    watch.Restart();
                    string firmware = MouseCommands.ReadFirmware(device);
                    watch.Stop();
                    Console.WriteLine(string.Format("固件   : {0}   ({1} ms)", firmware, watch.ElapsedMilliseconds));

                    watch.Restart();
                    BatteryInfo battery = MouseCommands.ReadBattery(device);
                    bool charging = MouseCommands.ReadCharging(device);
                    battery.Charging = charging;
                    watch.Stop();
                    Console.WriteLine(string.Format("电量   : {0} ({1}%)  充电={2}   ({3} ms)",
                        battery.Raw, battery.Percent, charging ? "是" : "否", watch.ElapsedMilliseconds));

                    watch.Restart();
                    DpiStage dpi = MouseCommands.ReadDpi(device, false);
                    watch.Stop();
                    Console.WriteLine(string.Format("DPI    : {0} x {1}   ({2} ms)", dpi.X, dpi.Y, watch.ElapsedMilliseconds));

                    int code = MouseCommands.ReadPollingCode(device);
                    int hz = MouseCommands.StandardCodeToHz(code);
                    Console.WriteLine(string.Format("回报率 : {0} Hz (编码 0x{1:X2})", hz > 0 ? hz.ToString() : "未知", code));

                    DpiStages stages = MouseCommands.ReadDpiStages(device);
                    Console.WriteLine(string.Format("档位   : 共 {0} 档，当前第 {1} 档", stages.Stages.Count, stages.Active));
                    for (int i = 0; i < stages.Stages.Count; i++)
                    {
                        Console.WriteLine(string.Format("         [{0}] {1}{2}", i + 1,
                            stages.Stages[i], i + 1 == stages.Active ? "   ← 当前" : ""));
                    }

                    if (writeTest) RunWriteTest(device, dpi, stages, hz);
                    return 0;
                }
            }
            catch (RazerException ex)
            {
                Console.WriteLine("协议错误: " + ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("错误: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// --batprobe [次数] [间隔毫秒]：连续采样电量，把原始回包整个打出来。
        /// 用来判断"电量读数抖动"到底是设备回的值在变，还是我们解析错了。
        /// </summary>
        private static int RunBatteryProbe(string[] args)
        {
            int count = ReadIntArg(args, "--batprobe", 60);
            int intervalMs = ReadIntArg(args, "--interval", 2000);

            try
            {
                using (MouseDevice device = MouseDevice.Open())
                {
                    Console.WriteLine("采样 " + count + " 次，间隔 " + intervalMs + " ms");
                    Console.WriteLine("序号  时间        raw(0x07/0x80)  args0 args1  percent  充电args  charging");

                    for (int i = 0; i < count; i++)
                    {
                        string time = DateTime.Now.ToString("HH:mm:ss.fff");
                        try
                        {
                            byte[] batteryReply = device.Ask(0x07, 0x80, 0x02, null);
                            byte[] batteryArgs = RazerReport.Args(batteryReply, 2);
                            byte[] chargingReply = device.Ask(0x07, 0x84, 0x02, null);
                            byte[] chargingArgs = RazerReport.Args(chargingReply, 2);

                            Console.WriteLine(string.Format("{0,4}  {1}  {2,3}            {3,3}  {4,3}  {5,3}%     {6,3}  {7,3}  {8}",
                                i, time,
                                batteryArgs[1], batteryArgs[0], batteryArgs[1],
                                MouseCommands.ToPercent(batteryArgs[1],
                                    MouseCommands.AllowsPercentScale(device.Pid)),
                                chargingArgs[0], chargingArgs[1],
                                chargingArgs[1] != 0 ? "是" : "否"));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(string.Format("{0,4}  {1}  ❌ {2}", i, time, ex.Message));
                        }

                        if (i + 1 < count) System.Threading.Thread.Sleep(intervalMs);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("错误: " + ex.Message);
                return 1;
            }
        }

        private static int ReadIntArg(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                int value;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out value)) return value;
                return fallback;
            }
            return fallback;
        }

        /// <summary>写"当前值"（等效空操作），再读回校验写入通道是否可用。</summary>
        private static void RunWriteTest(MouseDevice device, DpiStage dpi, DpiStages stages, int hz)
        {
            Console.WriteLine();
            Console.WriteLine("—— 写入通道验证（把当前值写回去，等效空操作）——");

            // 1. DPI
            try
            {
                MouseCommands.WriteDpi(device, dpi.X, dpi.Y, false);
                DpiStage back = MouseCommands.ReadDpi(device, false);
                Console.WriteLine(string.Format("写 DPI   : {0}x{1} → 读回 {2}x{3}  {4}",
                    dpi.X, dpi.Y, back.X, back.Y, back.X == dpi.X && back.Y == dpi.Y ? "✅" : "❌ 未生效"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("写 DPI   : ❌ " + ex.Message);
            }

            // 2. 回报率
            if (hz > 0)
            {
                try
                {
                    MouseCommands.WritePolling(device, hz);
                    int backCode = MouseCommands.ReadPollingCode(device);
                    int backHz = MouseCommands.StandardCodeToHz(backCode);
                    Console.WriteLine(string.Format("写回报率 : {0}Hz → 读回 {1}Hz (0x{2:X2})  {3}",
                        hz, backHz, backCode, backHz == hz ? "✅" : "❌ 未生效"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("写回报率 : ❌ " + ex.Message);
                }
            }

            // 3. 档位表
            try
            {
                MouseCommands.WriteDpiStages(device, stages, false);
                DpiStages back = MouseCommands.ReadDpiStages(device);
                bool same = back.Stages.Count == stages.Stages.Count && back.Active == stages.Active;
                if (same)
                {
                    for (int i = 0; i < stages.Stages.Count; i++)
                    {
                        if (back.Stages[i].X != stages.Stages[i].X || back.Stages[i].Y != stages.Stages[i].Y)
                            same = false;
                    }
                }
                Console.WriteLine(string.Format("写档位表 : {0} 档 → 读回 {1} 档  {2}",
                    stages.Stages.Count, back.Stages.Count, same ? "✅" : "❌ 未生效"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("写档位表 : ❌ " + ex.Message);
            }
        }
    }
}
