// 入口：默认启动托盘程序；--selftest 走命令行自检；第二个实例唤醒第一个。
using System;
using System.Threading;
using System.Windows.Forms;
using RazerMouse.App;

namespace RazerMouse
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch { }

            if (HasArg(args, "--autostart")) return AutostartCommand.Run(GetValue(args, "--autostart"));
            if (HasArg(args, "--make-icon")) return IconMaker.Run(GetValue(args, "--make-icon") ?? @"assets\app.ico");
            if (HasArg(args, "--alerttest")) return AlertTest.Run(args);
            if (HasArg(args, "--selftest")) return SelfTest.Run(args);
            if (HasArg(args, "--shot")) return Shot.Run(GetValue(args, "--shot") ?? "shots");
            if (HasArg(args, "--help") || HasArg(args, "-h") || HasArg(args, "/?")) { PrintHelp(); return 0; }

            bool minimized = HasArg(args, "--minimized");

            bool created;
            using (var mutex = new Mutex(true, SingleInstance.MutexName, out created))
            {
                if (!created)
                {
                    SingleInstance.SignalExisting();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    ShowFatal(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    ShowFatal(e.ExceptionObject as Exception);
                };

                using (var app = new TrayApp(minimized))
                {
                    Application.Run(app);
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>取 "--shot D:\dir" 里的值；值可以跟空格，也可以写成 --shot=D:\dir。</summary>
        private static string GetValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) return args[i + 1];
                    return null;
                }
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(name.Length + 1);
                }
            }
            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("雷蛇鼠标控制器");
            Console.WriteLine("  直接运行：启动托盘程序（--minimized 只驻留托盘）");
            Console.WriteLine("  --selftest [--write-test]：命令行自检，读设备状态");
            Console.WriteLine("  --shot <目录>：把面板与图标渲染成 PNG（诊断用）");
        }

        private static void ShowFatal(Exception ex)
        {
            string text = ex == null ? "未知错误" : ex.Message;
            try
            {
                MessageBox.Show("程序遇到未处理的错误：\n\n" + text + "\n\n程序会继续运行，可右键托盘图标退出。",
                    "雷蛇鼠标控制器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
