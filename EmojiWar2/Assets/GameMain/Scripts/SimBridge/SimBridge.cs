//------------------------------------------------------------
// EmojiWar GameMain - 模拟层的"表现层自举"（W-04）
//
// W-04 把模拟层拆成不引用 UnityEngine 的程序集（`EmojiWar.Sim`）。原来模拟层直接用的三样引擎东西
// 必须留在表现层，并在启动时**注入**回模拟层：
//   1. `UnityEngine.Debug`      → `SimLog.Info/Warn`（模拟层的日志出口）
//   2. `Unity.Profiling` 的 ProfilerMarker → 本文件（归因用标记；调用点改成 `SimPerfMarkers.X`）
//   3. `GC.GetAllocatedBytesForCurrentThread` / `Profiler.GetTotalAllocatedMemoryLong`
//      → `SimPerf.AllocBytesProvider`（IL2CPP 下前者会 **native crash**，见 SimPerf.cs 的注释）
//   4. `Application.buildGUID`  → `SimBuildInfo.BuildIdProvider`（仅诊断用，不参与比较）
//
// 入口：`GameEntry` 启动时调用 `SimBridge.Install()`（幂等）。
//------------------------------------------------------------

using Unity.Profiling;
using EmojiWar.GameMain.Simulation;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-04] 模拟层的 Profiler 标记（模拟层不引用 UnityEngine，标记只能放这里）。</summary>
    public static class SimPerfMarkers
    {
        public static readonly ProfilerMarker Tick = new ProfilerMarker("Sim.Tick");
        public static readonly ProfilerMarker Cast = new ProfilerMarker("Sim.Cast");
        public static readonly ProfilerMarker View = new ProfilerMarker("Sim.View");
    }

    /// <summary>[W-04] 把引擎侧的实现注入模拟层（幂等；GameEntry 启动时调用一次）。</summary>
    public static class SimBridge
    {
        private static bool s_Installed;

        public static bool Installed { get { return s_Installed; } }

        public static void Install()
        {
            if (s_Installed) { return; }
            s_Installed = true;

            // 1) 日志出口（模拟层不再直接 Debug.Log）
            SimLog.Info = UnityEngine.Debug.Log;
            SimLog.Warn = UnityEngine.Debug.LogWarning;

            // 2) 分配字节读取器
            //    ★ IL2CPP 下 `GC.GetAllocatedBytesForCurrentThread()` 会 native crash（实测），
            //      所以只有 Mono 用精确实现；IL2CPP 用 Profiler 总分配量做粗代理（探针会标注）。
#if ENABLE_IL2CPP
            SimPerf.AllocBytesProvider = AllocCoarse;
            SimPerf.AllocExact = false;
#else
            SimPerf.AllocBytesProvider = AllocExactBytes;
            SimPerf.AllocExact = true;
#endif

            // 3) 构建标识（诊断用：Mono 走 MVID，IL2CPP 走 buildGUID）
            SimBuildInfo.BuildIdProvider = ReadBuildGuid;

            // 4) [W-04] 配置视图注入：模拟层只认接口（ISimSpellConfig / ISimBattleConfig），
            //    于是它不必引用 Data.ConfigService 与 ScriptableObject。
            SimInjectedConfig.Spell = Data.ConfigService.SpellSystem;
            SimInjectedConfig.Battle = Data.ConfigService.Battle;

            // 5) [W-04] 引擎侧能力注入：模拟层不能引用 SimConfigFactory / SimFrameEvents（它们要用 Data），
            //    于是反过来由这里把实现接上去。
            SimHooks.BuildConfigFromIds = SimConfigFactory.BuildFromIds;
            SimHooks.BuildConfig = SimConfigFactory.Build;
            SimHooks.ApplyFrameEvent = SimFrameEvents.Apply;
        }

        private static long AllocExactBytes()
        {
            return System.GC.GetAllocatedBytesForCurrentThread();
        }

        private static long AllocCoarse()
        {
            return UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        }

        private static string ReadBuildGuid()
        {
            return UnityEngine.Application.buildGUID;
        }
    }
}
