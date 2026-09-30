//------------------------------------------------------------
// EmojiWar Sim - 引擎侧回调的注入口（W-04）
//
// 模拟层不许引用 UnityEngine / Data，但有两件事只有引擎侧能做：
//   · 按 Id 编译玩家配置（`SimConfigFactory` 要查物品数据表）；
//   · 应用帧事件（`SimFrameEvents.Apply` 要用 `ConfigService.GetWeapon`）。
// 于是把它们做成**委托注入**：表现层在 `SimBridge.Install()` 里接上。
// 未注入时返回 null / false，调用方必须自己兜底（宁可失败不要静默成功）。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-04] 引擎侧能力的注入口（由 `SimBridge.Install()` 设置）。</summary>
    public static class SimHooks
    {
        /// <summary>按 Id 编译玩家配置：`(sessionId, entityId, characterId, loadoutIds) → SimPlayerConfig`。</summary>
        public static System.Func<int, int, int, PlayerLoadoutIds, SimPlayerConfig> BuildConfigFromIds;

        /// <summary>按会话编译玩家配置（房间/测试用）：`(sessionId, entityId, characterId) → SimPlayerConfig`。</summary>
        public static System.Func<int, int, int, SimPlayerConfig> BuildConfig;

        /// <summary>应用一条帧事件（返回是否生效，note 给探针用）。</summary>
        public delegate bool FrameEventListener(LockstepSimulation sim, in FrameEvent e, out string note);

        /// <summary>帧事件的应用实现（唯一一份，网络层与回放器共用）。</summary>
        public static FrameEventListener ApplyFrameEvent;
    }
}