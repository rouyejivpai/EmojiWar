//------------------------------------------------------------
// EmojiWar GameMain - 网络损伤模拟（W-26.3 / 报告 J1）
//
// 目的：报告 J1 指出"从未在 150ms RTT / 30ms 抖动 / 5% 丢包下测试过"，
//       而 E 组（帧消费与抖动缓冲）的**全部问题只在有延迟/抖动时才暴露**——
//       本机回环 RTT≈0 恰好把它们全部绕过。本文件就是让 M2 的验收成为可能的前提。
//
// 用法（命令行，构建版）：
//   -netdelay <ms>     单向基础延迟（默认 0）
//   -netjitter <ms>    每条消息在基础延迟上加 ±jitter 的随机抖动（默认 0）
//   -netloss <pct>     每条消息按百分比丢弃（默认 0）
//   -netseed <n>       注入用的随机种子（默认 12345，保证同参数可复现）
//
// 关键设计：**保持 FIFO 顺序**
//   当前协议仍走 TCP（可靠有序）。若按"到期时间"排序投递，就会把消息顺序打乱，
//   制造出当前架构下**不可能发生**的故障（例如 StateCheck 先于同帧的 InputFrame 到达），
//   让门禁报出假的不同步。
//   因此这里按"**每会话一条 FIFO 队列，只有队头到期才能释放**"实现：
//     · 顺序与 TCP 一致（先入先出）；
//     · 队头被推迟时，后续消息也必须等 → **天然复现 TCP 的队头阻塞**（报告 C4 点名的现象）；
//     · -netloss 按"未来的不可靠通道"语义做**消息级丢弃**（C4 的改造方向）。
//
// 与确定性的关系：注入只影响**消息到达时机**，不改变消息内容，也不参与模拟计算。
//   它的随机源是独立的 System.Random（不是 SimRandom），因此不会污染帧同步。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>网络损伤注入门（延迟/抖动/丢包）。未启用时所有方法零开销直通。</summary>
    public static class NetSim
    {
        private struct Pending
        {
            public float DueTime;
            public int SessionId;
            public NetMessage Message;
        }

        /// <summary>是否启用注入（任一参数非 0）。</summary>
        public static bool Enabled { get; private set; }

        public static float DelayMs { get; private set; }
        public static float JitterMs { get; private set; }
        public static float LossPct { get; private set; }
        public static int Seed { get; private set; }

        private static bool s_Parsed;
        private static System.Random s_Rng;

        // 每会话一条 FIFO 队列（sessionId = -1 表示客户端侧）
        private static readonly Dictionary<int, Queue<Pending>> s_Queues = new Dictionary<int, Queue<Pending>>();
        private static readonly List<int> s_SessionKeys = new List<int>();   // 复用：避免每帧分配

        // 统计（探针用）
        public static long InterceptedCount { get; private set; }
        public static long DeliveredCount { get; private set; }
        public static long DroppedCount { get; private set; }
        public static long DelayedCount { get; private set; }
        public static double DelaySumMs { get; private set; }
        public static long PassThroughCount { get; private set; }
        public static int QueueDepth { get; private set; }

        /// <summary>解析命令行（幂等；由 NetworkService.Awake 调用一次）。</summary>
        public static void EnsureParsed()
        {
            if (s_Parsed) { return; }
            s_Parsed = true;

            Seed = 12345;
            DelayMs = 0f;
            JitterMs = 0f;
            LossPct = 0f;

            try
            {
                string[] args = Environment.GetCommandLineArgs();
                // 循环上界用 args.Length（否则最后一个参数永不被检查 —— 本项目已踩过一次同类坑）
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-netdelay" && i + 1 < args.Length)
                    {
                        float v; if (float.TryParse(args[i + 1], out v)) { DelayMs = Mathf.Max(0f, v); }
                    }
                    else if (args[i] == "-netjitter" && i + 1 < args.Length)
                    {
                        float v; if (float.TryParse(args[i + 1], out v)) { JitterMs = Mathf.Max(0f, v); }
                    }
                    else if (args[i] == "-netloss" && i + 1 < args.Length)
                    {
                        float v; if (float.TryParse(args[i + 1], out v)) { LossPct = Mathf.Clamp(v, 0f, 100f); }
                    }
                    else if (args[i] == "-netseed" && i + 1 < args.Length)
                    {
                        int v; if (int.TryParse(args[i + 1], out v)) { Seed = v; }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NetSim] 解析命令行失败（注入未启用）: " + e.Message);
                DelayMs = JitterMs = LossPct = 0f;
            }

            Enabled = DelayMs > 0f || JitterMs > 0f || LossPct > 0f;
            s_Rng = new System.Random(Seed);

            if (Enabled)
            {
                Debug.Log(string.Format("[NetSim] 损伤注入启用 delay={0}ms jitter=±{1}ms loss={2}% seed={3}",
                    DelayMs, JitterMs, LossPct, Seed));
            }
        }

        /// <summary>
        /// 收到一条消息时调用。
        /// 返回 true = **已被本门接管**（调用方不要再直接派发）；false = 让调用方按原路径直接派发。
        /// </summary>
        public static bool Intercept(int sessionId, NetMessage msg, float now)
        {
            if (!Enabled || msg == null) { return false; }

            InterceptedCount++;

            // 丢包（"未来的不可靠通道"语义：消息级丢弃）
            if (LossPct > 0f && s_Rng.NextDouble() * 100.0 < LossPct)
            {
                DroppedCount++;
                return true;
            }

            float delay = DelayMs;
            if (JitterMs > 0f)
            {
                delay += (float)((s_Rng.NextDouble() * 2.0 - 1.0) * JitterMs);
            }
            if (delay < 0f) { delay = 0f; }

            // 无需延迟 → 走零开销直通路径（但仍保持 FIFO：若该会话已有排队消息，必须排到队尾）
            Queue<Pending> q;
            if (!s_Queues.TryGetValue(sessionId, out q))
            {
                if (delay <= 0.0001f)
                {
                    PassThroughCount++;
                    return false;   // 无队列、无延迟 → 直接派发
                }
                q = new Queue<Pending>();
                s_Queues[sessionId] = q;
            }

            q.Enqueue(new Pending
            {
                DueTime = now + delay * 0.001f,
                SessionId = sessionId,
                Message = msg,
            });
            DelayedCount++;
            DelaySumMs += delay;
            QueueDepth = q.Count;
            return true;
        }

        /// <summary>
        /// 每帧调用：把**队头已到期**的消息按 FIFO 投递出去。
        /// 队头未到期则整条队列都不动 —— 这就是队头阻塞。
        /// </summary>
        public static void Pump(float now, Action<int, NetMessage> dispatch)
        {
            if (!Enabled || s_Queues.Count == 0 || dispatch == null) { return; }

            s_SessionKeys.Clear();
            foreach (var kv in s_Queues) { s_SessionKeys.Add(kv.Key); }

            int depth = 0;
            for (int k = 0; k < s_SessionKeys.Count; k++)
            {
                Queue<Pending> q;
                if (!s_Queues.TryGetValue(s_SessionKeys[k], out q)) { continue; }
                while (q.Count > 0 && q.Peek().DueTime <= now)
                {
                    Pending p = q.Dequeue();
                    DeliveredCount++;
                    dispatch(p.SessionId, p.Message);
                }
                depth += q.Count;
            }
            QueueDepth = depth;
        }

        /// <summary>统计摘要（探针用）。</summary>
        public static string Describe()
        {
            return string.Format(
                "enabled={0} delay={1:F0}ms jitter=±{2:F0}ms loss={3:F0}% seed={4} | 拦截={5} 直通={6} 已投递={7} 丢弃={8} 排队中={9} 平均增加延迟={10:F1}ms",
                Enabled, DelayMs, JitterMs, LossPct, Seed,
                InterceptedCount, PassThroughCount, DeliveredCount, DroppedCount, QueueDepth,
                DelayedCount > 0 ? DelaySumMs / DelayedCount : 0.0);
        }

        /// <summary>清空队列与统计（换局/换模式时）。</summary>
        public static void Reset()
        {
            s_Queues.Clear();
            InterceptedCount = 0;
            DeliveredCount = 0;
            DroppedCount = 0;
            DelayedCount = 0;
            DelaySumMs = 0.0;
            PassThroughCount = 0;
            QueueDepth = 0;
        }
    }
}
