// 系统空闲时间：只用来判断「用户现在是不是正在操作」。
// 后台轮询在有人用鼠标的时候让路，停手之后立刻补上。
using System;
using System.Runtime.InteropServices;

namespace RazerMouse.Native
{
    internal static class IdleTime
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        /// <summary>
        /// 距上一次键鼠输入的毫秒数。取不到时返回 <see cref="int.MaxValue"/>（当作空闲，不阻断轮询）。
        /// </summary>
        public static int Milliseconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info)) return int.MaxValue;

            // TickCount 与 dwTime 都是 32 位回绕计数，无符号相减在回绕处仍然正确。
            uint now = unchecked((uint)Environment.TickCount);
            uint idle = unchecked(now - info.dwTime);
            return idle > int.MaxValue ? int.MaxValue : (int)idle;
        }
    }
}
