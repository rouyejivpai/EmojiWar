//------------------------------------------------------------
// EmojiWar Sim - 模拟层日志出口（W-04）
//
// 为什么需要它：模拟层不许引用 UnityEngine（G1），也就不能直接 `Debug.Log`。
// 但模拟层**确实需要**在异常路径上留下痕迹（录像写失败、帧事件应用失败…），
// 静默失败是本项目反复踩过的坑（W-00「对账不再沉默」就是同一类问题）。
//
// 做法：模拟层只调用 `SimLog.Log/LogWarning`，**由表现层在启动时注入真正的写日志实现**
// （`GameEntry` 里接 `UnityEngine.Debug.Log`）。没注入时退化为"不输出"，
// 但接口上依然是显式调用 —— 读代码的人能看到"这里会报告"，而不是"这里什么都没做"。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-04] 模拟层的日志出口；实现由表现层注入（默认无输出）。</summary>
    public static class SimLog
    {
        /// <summary>普通日志实现（表现层注入，例如 UnityEngine.Debug.Log）。</summary>
        public static System.Action<string> Info;

        /// <summary>警告实现（表现层注入，例如 UnityEngine.Debug.LogWarning）。</summary>
        public static System.Action<string> Warn;

        public static bool HasSink { get { return Info != null || Warn != null; } }

        public static void Log(string message)
        {
            System.Action<string> h = Info;
            if (h != null) { h(message); }
        }

        public static void LogWarning(string message)
        {
            System.Action<string> h = Warn;
            if (h != null) { h(message); }
            else
            {
                h = Info;
                if (h != null) { h("[WARN] " + message); }
            }
        }
    }
}
