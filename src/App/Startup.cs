// 开机自启（HKCU\...\Run）与单实例信号。都不需要管理员权限。
using System;
using System.Threading;
using Microsoft.Win32;

namespace RazerMouse.App
{
    internal static class Startup
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RazerMouseController";

        public static string ExecutablePath
        {
            get
            {
                string path = System.Windows.Forms.Application.ExecutablePath;

                // 自检用的 RazerMouseTest.exe 和主程序放在一起，别把自检程序写进开机启动项。
                string name = System.IO.Path.GetFileName(path);
                if (string.Equals(name, "RazerMouseTest.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string main = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "RazerMouse.exe");
                    if (System.IO.File.Exists(main)) return main;
                }
                return path;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        /// <summary>最近一次 SetEnabled 失败的原因，供界面/命令行显示。</summary>
        public static string LastError = "";

        public static bool SetEnabled(bool enabled)
        {
            LastError = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        LastError = "打不开 HKCU\\" + RunKeyPath + "（键不存在或无写权限）";
                        return false;
                    }
                    if (enabled)
                        key.SetValue(ValueName, "\"" + ExecutablePath + "\" --minimized");
                    else
                        key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + "：" + ex.Message;
                return false;
            }
        }
    }

    /// <summary>第二个实例启动时用它唤醒已有实例（已有实例监听后弹出面板）。</summary>
    internal static class SingleInstance
    {
        public const string MutexName = "RazerMouseController.SingleInstance";
        public const string ShowEventName = "RazerMouseController.ShowPanel";

        public static EventWaitHandle CreateShowEvent()
        {
            try
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch { return null; }
        }

        public static void SignalExisting()
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName))
                {
                    handle.Set();
                }
            }
            catch
            {
                // 老实例已经退出或还没建好事件，忽略
            }
        }
    }
}
