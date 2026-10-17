// 91 字节 Feature Report 的构造 / 校验 / 解析（协议见 protocol.md §1–§4）。
using System;

namespace RazerMouse.Protocol
{
    internal static class RazerReport
    {
        /// <summary>1 字节 report id + 90 字节 razer_report。</summary>
        public const int Length = 91;

        /// <summary>本机实测稳定；0xFF 会超时（protocol.md §3）。</summary>
        public const byte DefaultTxn = 0x1F;

        private const int ArgumentsOffset = 9;
        private const int ArgumentsMax = 80;

        public static byte[] Build(byte commandClass, byte commandId, byte dataSize,
            byte[] args, byte txn)
        {
            var report = new byte[Length];
            report[2] = txn;
            report[6] = dataSize;
            report[7] = commandClass;
            report[8] = commandId;

            if (args != null && args.Length > 0)
            {
                if (args.Length > ArgumentsMax)
                    throw new ArgumentException("arguments 超过 80 字节");
                Array.Copy(args, 0, report, ArgumentsOffset, args.Length);
            }

            report[89] = Crc(report);
            return report;
        }

        /// <summary>b[3..88] 逐字节 XOR，写在 b[89]（发送与回包同一算法）。</summary>
        public static byte Crc(byte[] report)
        {
            byte crc = 0;
            for (int i = 3; i < 89; i++) crc ^= report[i];
            return crc;
        }

        public static byte Status(byte[] reply)
        {
            return reply[1];
        }

        /// <summary>按固定长度取 arguments —— 不要用 reply[6]，它只是请求 data_size 的回显。</summary>
        public static byte[] Args(byte[] reply, int count)
        {
            if (count < 0 || count > ArgumentsMax)
                throw new ArgumentOutOfRangeException("count");
            var result = new byte[count];
            Array.Copy(reply, ArgumentsOffset, result, 0, count);
            return result;
        }

        public static bool CrcValid(byte[] reply)
        {
            return reply[89] == Crc(reply);
        }

        public static string StatusText(byte status)
        {
            switch (status)
            {
                case 0x00: return "新命令（未处理）";
                case 0x01: return "设备忙";
                case 0x02: return "成功";
                case 0x03: return "失败（参数被拒）";
                case 0x04: return "超时/无响应";
                case 0x05: return "不支持该命令";
                default: return string.Format("未知状态 0x{0:X2}", status);
            }
        }
    }

    internal sealed class RazerException : Exception
    {
        public byte Status { get; private set; }

        public RazerException(byte status, string message)
            : base(message)
        {
            Status = status;
        }
    }
}
