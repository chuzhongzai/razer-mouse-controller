// --alerttest：不用真机，用一串模拟电量验证低电量提示的判定与回差。
using System;
using RazerMouse.App;
using RazerMouse.Protocol;

namespace RazerMouse
{
    internal static class AlertTest
    {
        private static int _failed;

        public static int Run(string[] args)
        {
            Console.WriteLine("低电量提示判定测试（阈值 20%，回差 10%）");
            Console.WriteLine();

            DischargePath();
            ChargePath();
            DisconnectedPath();
            ScalePath();

            Console.WriteLine();
            Console.WriteLine(_failed == 0 ? "全部通过 ✅" : "有 " + _failed + " 项不符合预期 ❌");
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>一路放电：只在跨过阈值那一次提示，回升一点不重复提示。</summary>
        private static void DischargePath()
        {
            Console.WriteLine("—— 放电路径 ——");
            var alert = new BatteryAlert(20);
            int count = 0;
            alert.Raised += delegate { count++; };

            Step(alert, 80, false, "80% 正常");
            Step(alert, 25, false, "25% 还高于阈值");
            Step(alert, 19, true, "19% 首次跌破 → 提示");
            Step(alert, 18, false, "18% 不重复提示");
            Step(alert, 19, false, "回到 19% 仍不提示");
            Step(alert, 28, false, "28% 还没回升够，不重新武装");
            Step(alert, 31, false, "31% 超过 30% 重新武装（本身不提示）");
            Step(alert, 19, true, "再跌回 19% → 再次提示");
            Step(alert, 4, false, "4% 严重但已提示过，不连弹");

            Expect(count == 2, "提示总次数应为 2，实际 " + count);
        }

        /// <summary>插上电就不再提示，充满提示一次，重复上报 100% 不重复。</summary>
        private static void ChargePath()
        {
            Console.WriteLine();
            Console.WriteLine("—— 充电路径 ——");
            var alert = new BatteryAlert(20);
            int count = 0;
            string lastTitle = null;
            bool lastLow = false;
            alert.Raised += delegate(object s, BatteryAlertEventArgs e) { count++; lastTitle = e.Title; lastLow = e.Low; };

            Step(alert, 3, true, false, true, "3% 断开充电 → 严重提示");
            Expect(lastLow, "低电量提示带告警标记（Low=true，图标应变色）");
            Step(alert, 50, true, true, false, "充电中 50% 不提示");
            Step(alert, 100, true, true, true, "充到 100% 提示充满");
            Expect(!lastLow, "充满提示不是告警（Low=false，图标不该变红）");
            Step(alert, 100, true, true, false, "再次上报 100% 不重复");
            Step(alert, 100, true, true, false, "第三次也不重复");

            Expect(count == 2, "提示总次数应为 2，实际 " + count);
            Expect(lastTitle == "电量已充满", "最后一次标题应为「电量已充满」，实际 " + (lastTitle ?? "(无)"));
        }

        /// <summary>断连或没有电量信息时，不应该弹任何提示。</summary>
        private static void DisconnectedPath()
        {
            Console.WriteLine();
            Console.WriteLine("—— 断连路径 ——");
            var alert = new BatteryAlert(20);
            int count = 0;
            alert.Raised += delegate { count++; };

            var status = new MouseStatus();
            status.Connected = false;
            status.BatteryPercent = 2;
            alert.Update(status);
            Expect(count == 0, "断连时不应提示");

            status.Connected = true;
            status.HasBattery = false;
            alert.Update(status);
            Expect(count == 0, "没有电量信息时不应提示");
        }

        /// <summary>电量刻度换算：DeathAdder V3 Pro 回的是 0–255，不能把小于 100 的原值当成百分数。</summary>
        private static void ScalePath()
        {
            Console.WriteLine();
            Console.WriteLine("—— 电量刻度换算 ——");

            const bool noGuess = false;   // 刻度确定的型号
            const bool guess = true;      // 老设备，≤100 当百分数

            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(255, noGuess) == 100, "255 → 100%");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(189, noGuess) == 74, "189 → 74%");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(187, noGuess) == 73, "187 → 73%");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(101, noGuess) == 40, "101 → 40%");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(100, noGuess) == 39,
                "100 → 39%（旧逻辑会当成 100%，这就是「电量反弹」的根源）");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(57, noGuess) == 22, "57 → 22%");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(1, noGuess) == 0, "1 → 0%");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(0, noGuess) == 0, "0 → 0%");

            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(57, guess) == 57, "老设备 57 → 57%（当百分数）");
            Expect(RazerMouse.Protocol.MouseCommands.ToPercent(255, guess) == 100, "老设备 255 → 100%");

            ushort daPid = MouseDevice.DeathAdderV3ProPids[1];
            Expect(!RazerMouse.Protocol.MouseCommands.AllowsPercentScale(daPid),
                "DeathAdder V3 Pro 不走「≤100 当百分数」的猜测");
            Expect(RazerMouse.Protocol.MouseCommands.AllowsPercentScale(0x007A),
                "未知型号（Viper 等）仍走猜测");

            SmoothPath();
        }

        /// <summary>抗反弹：放电过程中电量不该往上跳，但插上电或大幅跳变要如实显示。</summary>
        private static void SmoothPath()
        {
            Console.WriteLine();
            Console.WriteLine("—— 抗反弹平滑 ——");

            Expect(BatterySmoother.Adjust(-1, false, 57, false) == 57, "第一次读到 57% 直接用");
            Expect(BatterySmoother.Adjust(57, false, 55, false) == 55, "掉电 57 → 55 正常显示");
            Expect(BatterySmoother.Adjust(57, false, 60, false) == 57, "放电中反弹 +3 被压住");
            Expect(BatterySmoother.Adjust(57, false, 62, false) == 57, "放电中反弹 +5（临界）也被压住");
            Expect(BatterySmoother.Adjust(57, false, 63, false) == 63, "回升 +6 超出容差，如实显示");
            Expect(BatterySmoother.Adjust(57, false, 90, false) == 90, "大幅跳变（重新校准）如实显示");
            Expect(BatterySmoother.Adjust(57, false, 58, true) == 58, "插上电后回升正常显示");
            Expect(BatterySmoother.Adjust(57, true, 56, false) == 56, "刚拔线那一刻的新值直接用");
        }

        private static void Step(BatteryAlert alert, int percent, bool expectRaised, string note)
        {
            Step(alert, percent, true, false, expectRaised, note);
        }

        private static void Step(BatteryAlert alert, int percent, bool connected, bool charging,
            bool expectRaised, string note)
        {
            bool raised = false;
            EventHandler<BatteryAlertEventArgs> probe = delegate { raised = true; };
            alert.Raised += probe;

            var status = new MouseStatus();
            status.Connected = connected;
            status.HasBattery = true;
            status.BatteryPercent = percent;
            status.Charging = charging;
            alert.Update(status);

            alert.Raised -= probe;
            Console.WriteLine(string.Format("  {0,-46} {1}", note, raised == expectRaised ? "✅" : "❌"));
            if (raised != expectRaised) _failed++;
        }

        private static void Expect(bool condition, string message)
        {
            Console.WriteLine("  " + (condition ? "✅ " : "❌ ") + message);
            if (!condition) _failed++;
        }
    }
}
