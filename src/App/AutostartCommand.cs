// --autostart：命令行开关开机自启，方便排错与脚本化；界面里的菜单项走同一套代码。
using System;
using RazerMouse.App;

namespace RazerMouse
{
    internal static class AutostartCommand
    {
        public static int Run(string action)
        {
            if (string.IsNullOrEmpty(action)) action = "status";
            action = action.Trim().ToLowerInvariant();

            if (action == "on" || action == "off")
            {
                bool enable = action == "on";
                if (!Startup.SetEnabled(enable))
                {
                    Console.WriteLine("写入注册表失败：" + Startup.LastError);
                    return 1;
                }
            }
            else if (action != "status")
            {
                Console.WriteLine("用法：--autostart on|off|status");
                return 1;
            }

            bool enabled = Startup.IsEnabled();
            Console.WriteLine("开机自启：" + (enabled ? "已开启" : "已关闭"));
            Console.WriteLine("命令     ：\"" + Startup.ExecutablePath + "\" --minimized");
            Console.WriteLine("注册表   ：HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\RazerMouseController");
            return action == "status" ? 0 : 0;
        }
    }
}
