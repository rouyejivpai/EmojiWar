//------------------------------------------------------------
// EmojiWar GameMain - 逻辑帧耗时与消费节奏统计（W-01）
//
// 目的（报告 H1/E1/H2）：让"逻辑帧耗时"第一次变成可读数字。
// 此前全项目零 ProfilerMarker、零耗时统计，导致：
//   · H1「200 实体单帧耗时」无法判断；
//   · E1「每渲染帧执行了几个逻辑帧」无法判断（抖动验收缺少指标）；
//   · H2「0 B/帧」无法回归（没有 gen0 增量可看）。
//
// 设计：
//   · ProfilerMarker 供 Unity Profiler 做归因（注意：W-04 拆 asmdef 时，
//     本文件应随表现层移出 Sim 程序集，或整体用 #if 包起来——Sim 层不允许引用引擎）；
//   · 环形统计（300 帧）供**构建版 exe** 在没有 Profiler 的情况下也能读出 avg/p50/p99/max；
//   · 顺带统计"每渲染帧执行的 tick 数"分布与 gen0 GC 增量。
//
// 无分配：double[] 复用，只在取样时排序。
//------------------------------------------------------------

using System;
// [W-04] 原来这里 `using Unity.Profiling;` 并持有 ProfilerMarker —— 模拟层不许引用 UnityEngine，
// 因此 ProfilerMarker 搬到表现层（`SimBridge/SimPerfMarkers.cs`），Sim 只保留纯 C# 统计。

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>逻辑帧耗时与消费节奏统计（无分配）。</summary>
    public static class SimPerf
    {
        // [W-04] Profiler 标记原本在这里（`MarkerTick/MarkerCast/MarkerView`）—— 已搬到表现层的
        // `SimBridge/SimPerfMarkers.cs`（模拟层不引用 UnityEngine）。调用点已改为 `SimPerfMarkers.X`。

        // ---- 环形统计（最近 Capacity 个逻辑帧）----
        private const int Capacity = 300;
        private static readonly double[] s_Samples = new double[Capacity];
        private static readonly double[] s_Scratch = new double[Capacity];
        private static int s_Count;
        private static int s_Head;
        private static long s_TotalTicks;
        private static double s_TotalMs;
        private static double s_TickStartMs;

        // ---- 施法段耗时（H4 热点归因）----
        private static long s_CastCalls;
        private static double s_CastTotalMs;
        private static double s_CastStartMs;

        // ---- 每渲染帧 tick 数（E1 抖动验收指标）----
        private static int s_TicksThisFrame;
        private static int s_MaxTicksPerFrame;
        private static long s_FramesObserved;
        private static long s_TicksObserved;
        private static long s_CatchUpFrames;   // 单帧执行 >=2 个 tick 的帧数

        // ---- gen0 GC 增量（H2「0 B/帧」的代理指标）----
        private static int s_Gen0AtLastReport = GC.CollectionCount(0);
        private static int s_Gen0Delta;
        // [W-13/W-18] 顺带记录 gen1/gen2：队列深度出现大尖峰（>12 帧）时，需要能分辨
        //   "只是 gen0 的小停顿" 还是 "一次 gen1/gen2 的长时间停顿"（后者会让客户端明显落后）。
        private static int s_Gen1AtLastReport = GC.CollectionCount(1);
        private static int s_Gen2AtLastReport = GC.CollectionCount(2);
        private static int s_Gen1Delta;
        private static int s_Gen2Delta;

        // ---- [W-18] 逻辑帧分配字节（"0 B/帧"的**精确**指标）----
        private static long s_AllocAtTickStart;      // 本 tick 起点
        private static long s_AllocLastTick;         // 上一个 tick 的分配量
        private static long s_AllocWindow;           // 本报告区间内的分配总量
        private static long s_AllocMaxTick;          // 单 tick 分配峰值
        private static long s_AllocTicksWithGarbage; // 有分配的 tick 数
        private static long s_AllocTicksTotal;       // 统计的 tick 数

        // ---- [W-04] 分配字节读取器（**由表现层注入**，模拟层不引用 UnityEngine）----
        /// <summary>
        /// 读"已分配字节数"的实现。表现层启动时注入：
        /// Mono = `GC.GetAllocatedBytesForCurrentThread`（精确）；IL2CPP = `Profiler.GetTotalAllocatedMemoryLong`（粗代理）。
        /// **未注入时返回 0**：探针会显示 `alloc/tick=0B`，但门禁的"有分配的 tick 数"会异常 —— 这是刻意的
        /// （宁可让门禁看起来不对，也不要静默地假装"零分配达标"）。
        /// </summary>
        public static Func<long> AllocBytesProvider;
        /// <summary>注入的实现是否**精确**（false = 粗代理，探针会标注）。由注入方设置。</summary>
        public static bool AllocExact = true;
        // 背景（W-11 IL2CPP 验收实验）：`GC.GetAllocatedBytesForCurrentThread()` 在 IL2CPP
        // 独立播放器里会 **native crash** —— 崩溃栈为
        //   SimPerf_BeginTick → LockstepSimulation_Tick → ReplayPlayer_Run
        // （IL2CPP 生成的 C++ 里那一行正是 `GC_GetAllocatedBytesForCurrentThread` 的赋值），
        // 表现为"进了第一帧就闪退、但探针停在 Tick 之前"，极难归因。
        // 因此 IL2CPP 下**不能**调用该 API，要改用 `Profiler.GetTotalAllocatedMemoryLong()` 做**粗代理**
        // （单位仍是字节，但含其他线程与底层分配，**不能**用来断言"单 tick 0 分配"；
        //  那条断言由 Mono 构建版门禁继续保证，见 tools/verify_determinism.ps1 -ExpectZeroAlloc）。
        // [W-04] 而 `Profiler` 是 UnityEngine 类型 → 具体实现搬到表现层注入（`SimBridge/SimPerfBootstrap.cs`）。

        private static double NowMs()
        {
            return (double)System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>[W-04] 读"已分配字节数"（实现由表现层注入；未注入时返回 0）。</summary>
        private static long ReadAllocBytes()
        {
            Func<long> p = AllocBytesProvider;
            return p != null ? p() : 0L;
        }

        /// <summary>一个逻辑帧开始（在 FrameIndex++ 之后调用）。</summary>
        public static void BeginTick()
        {
            s_TickStartMs = NowMs();
            // [W-18] 记录 tick 起点的已分配字节数：与 EndTick 的差值 = **这一 tick 分配了多少字节**。
            // 这是"逻辑 Tick 的 GC Alloc = 0 B/帧"这条验收指标的**精确**形式 ——
            // 比"看 gen0 在 2 秒窗口内触发几次"精确得多（后者粗到看不出单帧分配）。
            s_AllocAtTickStart = ReadAllocBytes();
            s_TicksThisFrame++;
            s_TicksObserved++;
        }

        /// <summary>一个逻辑帧结束。</summary>
        public static void EndTick()
        {
            double ms = NowMs() - s_TickStartMs;
            s_Samples[s_Head] = ms;
            s_Head = (s_Head + 1) % Capacity;
            if (s_Count < Capacity) { s_Count++; }
            s_TotalTicks++;
            s_TotalMs += ms;

            // [W-18] 本 tick 的分配量
            long alloc = ReadAllocBytes() - s_AllocAtTickStart;
            s_AllocLastTick = alloc;
            s_AllocWindow += alloc;
            if (alloc > s_AllocMaxTick) { s_AllocMaxTick = alloc; }
            if (alloc > 0) { s_AllocTicksWithGarbage++; }
            s_AllocTicksTotal++;
        }

        /// <summary>一次施法解释器调用开始（CastResolver.Tick 入口）。</summary>
        public static void BeginCast()
        {
            s_CastStartMs = NowMs();
        }

        /// <summary>一次施法解释器调用结束。</summary>
        public static void EndCast()
        {
            s_CastTotalMs += NowMs() - s_CastStartMs;
            s_CastCalls++;
        }

        /// <summary>表现层每渲染帧调用一次：结算上一帧的「tick 数/帧」分布与 gen0 增量。</summary>
        public static void EndViewFrame()
        {
            if (s_TicksThisFrame > s_MaxTicksPerFrame) { s_MaxTicksPerFrame = s_TicksThisFrame; }
            if (s_TicksThisFrame >= 2) { s_CatchUpFrames++; }
            s_FramesObserved++;
            s_TicksThisFrame = 0;
            s_Gen0Delta = GC.CollectionCount(0) - s_Gen0AtLastReport;
            s_Gen1Delta = GC.CollectionCount(1) - s_Gen1AtLastReport;
            s_Gen2Delta = GC.CollectionCount(2) - s_Gen2AtLastReport;
        }

        /// <summary>取一次区间报告并重置"区间"计数（环形样本与累计量保留）。探针每 2 秒调一次。</summary>
        public static string Describe()
        {
            if (s_Count == 0)
            {
                return "tick n=0 (尚无逻辑帧)";
            }

            Array.Copy(s_Samples, s_Scratch, s_Count);
            Array.Sort(s_Scratch, 0, s_Count);
            double p50 = s_Scratch[s_Count / 2];
            double p99 = s_Scratch[Math.Min(s_Count - 1, (int)(s_Count * 0.99))];
            double max = s_Scratch[s_Count - 1];
            double avg = s_TotalMs / Math.Max(1, s_TotalTicks);
            double castAvg = s_CastCalls > 0 ? s_CastTotalMs / s_CastCalls : 0.0;

            double ticksPerFrame = s_FramesObserved > 0 ? (double)s_TicksObserved / s_FramesObserved : 0.0;
            double catchUpPct = s_FramesObserved > 0 ? 100.0 * s_CatchUpFrames / s_FramesObserved : 0.0;

            string line = string.Format(
                "tick n={0} avg={1:F3}ms p50={2:F3}ms p99={3:F3}ms max={4:F3}ms | castAvg={5:F4}ms | ticks/frame={6:F2} maxInFrame={7} catchUp={8:F1}% | gen0+{9} gen1+{15} gen2+{16} | alloc/tick={10}B (last={11}B max={12}B，有分配的 tick {13}/{14})",
                s_Count, avg, p50, p99, max, castAvg, ticksPerFrame, s_MaxTicksPerFrame, catchUpPct, s_Gen0Delta,
                s_AllocTicksTotal > 0 ? (s_AllocWindow / s_AllocTicksTotal) : 0L,
                s_AllocLastTick, s_AllocMaxTick, s_AllocTicksWithGarbage, s_AllocTicksTotal,
                s_Gen1Delta, s_Gen2Delta);

            // 区间重置
            s_FramesObserved = 0;
            s_TicksObserved = 0;
            s_CatchUpFrames = 0;
            s_MaxTicksPerFrame = 0;
            s_Gen0AtLastReport = GC.CollectionCount(0);
            s_Gen0Delta = 0;
            s_Gen1AtLastReport = GC.CollectionCount(1);
            s_Gen2AtLastReport = GC.CollectionCount(2);
            s_Gen1Delta = 0;
            s_Gen2Delta = 0;
            s_AllocWindow = 0;
            s_AllocMaxTick = 0;
            s_AllocTicksWithGarbage = 0;
            s_AllocTicksTotal = 0;

            // [W-11] IL2CPP 下 alloc 是粗代理 —— 必须在**同一条探针行**上标出来，
            // 否则读日志的人会把"总分配量差"当成"单 tick 分配"，得出错误结论。
            if (!AllocExact)
            {
                line += " ⚠alloc=IL2CPP粗代理(Profiler 总分配量差)";
            }
            return line;
        }

        /// <summary>[W-18] 上一个逻辑帧分配了多少字节（0 = 达标）。</summary>
        public static long LastTickAllocBytes { get { return s_AllocLastTick; } }
        /// <summary>[W-18] 本区间内"有分配"的 tick 占比。</summary>
        public static double AllocTickRatio
        {
            get { return s_AllocTicksTotal > 0 ? (double)s_AllocTicksWithGarbage / s_AllocTicksTotal : 0.0; }
        }

        /// <summary>换局/重建模拟时清空全部样本。</summary>
        public static void Reset()
        {
            s_Count = 0;
            s_Head = 0;
            s_TotalTicks = 0;
            s_TotalMs = 0;
            s_CastCalls = 0;
            s_CastTotalMs = 0;
            s_TicksThisFrame = 0;
            s_MaxTicksPerFrame = 0;
            s_FramesObserved = 0;
            s_TicksObserved = 0;
            s_CatchUpFrames = 0;
            s_Gen0AtLastReport = GC.CollectionCount(0);
            s_Gen0Delta = 0;
            s_Gen1AtLastReport = GC.CollectionCount(1);
            s_Gen2AtLastReport = GC.CollectionCount(2);
            s_Gen1Delta = 0;
            s_Gen2Delta = 0;
            s_AllocWindow = 0;
            s_AllocMaxTick = 0;
            s_AllocTicksWithGarbage = 0;
            s_AllocTicksTotal = 0;
        }
    }
}
