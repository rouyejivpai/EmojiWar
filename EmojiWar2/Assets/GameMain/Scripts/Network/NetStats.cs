//------------------------------------------------------------
// EmojiWar GameMain - 网络字节计数（W-19）
//
// 为什么需要它：W-19 的验收之一是"确认单帧字节数与每会话序列化次数"。
//   · `NetStats.RecordSent/RecordReceived` 在每个 socket 读写处累加**真实字节数**；
//   · 每 2 秒由 `NetworkService` 打一行 `[netstat]` 探针 → 于是"带宽到底多少"变成可观测量，
//     而不是靠估算协议头长度。
//
// 这个数字直接决定"输入量化"值不值得做：量化能把 `S2CInputFrame` 从 ~104 B（4 人）压到 ~40 B，
//   但如果实测带宽本来就只有几 KB/s，那么它节省的是**几 KB/s**，
//   却要付"改变输入精度、作废既有录像/基线"的代价 —— 那就该等真正的瓶颈出现（例如换 Steam 传输、
//   或玩家数/频率继续上调）再做。**先量化问题，再决定要不要优化。**
//------------------------------------------------------------

namespace EmojiWar.GameMain.Network
{
    /// <summary>网络收发字节统计（只计数，不做判断）。</summary>
    public static class NetStats
    {
        private static long s_SentBytes;
        private static long s_RecvBytes;
        private static long s_SentMessages;
        private static long s_RecvMessages;

        /// <summary>累计发送字节（含 4 字节帧头）。</summary>
        public static long SentBytes { get { return s_SentBytes; } }
        /// <summary>累计接收字节。</summary>
        public static long RecvBytes { get { return s_RecvBytes; } }
        public static long SentMessages { get { return s_SentMessages; } }
        public static long RecvMessages { get { return s_RecvMessages; } }

        public static void RecordSent(int bytes) { if (bytes > 0) { s_SentBytes += bytes; s_SentMessages++; } }
        public static void RecordReceived(int bytes) { if (bytes > 0) { s_RecvBytes += bytes; s_RecvMessages++; } }

        /// <summary>取一次区间报告并重置（探针每 2 秒调一次）。</summary>
        public static string DescribeAndReset(double seconds)
        {
            double sec = seconds > 0.01 ? seconds : 0.01;
            string line = string.Format(
                "sent={0}B ({1:F1} KB/s, {2} 条) recv={3}B ({4:F1} KB/s, {5} 条)",
                s_SentBytes, s_SentBytes / 1024.0 / sec, s_SentMessages,
                s_RecvBytes, s_RecvBytes / 1024.0 / sec, s_RecvMessages);
            s_SentBytes = 0; s_RecvBytes = 0; s_SentMessages = 0; s_RecvMessages = 0;
            return line;
        }

        public static void Reset()
        {
            s_SentBytes = 0; s_RecvBytes = 0; s_SentMessages = 0; s_RecvMessages = 0;
        }
    }
}
