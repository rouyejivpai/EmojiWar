//------------------------------------------------------------
// EmojiWar GameMain - 流程状态机定义（GF 规范单一来源）
//
// GF（GameFramework / StarForce 范式）中流程状态机的注册方式是：
//   ProcedureComponent.m_AvailableProcedureTypeNames（类型名数组）
//     + m_EntranceProcedureTypeName（入口流程）
//   在 Menu 场景的 GameFramework 对象上配置，运行时反射创建。
//
// 本类把"流程状态机有哪些状态、入口是谁、注册顺序"收敛为唯一常量来源：
//   - Builder（MenuSceneBuilder / RoomSetupBuilder）写场景 YAML 时引用本类，
//     避免多处硬编码漂移（曾出现 Builder 5 个 vs 场景 7 个的不一致）；
//   - 文档（doc/流程状态机.md）与本类一一对应，改流程先改这里。
//
// 状态机（GF 规范）：
//   Launch ──▶ Menu ──▶ CharacterSelect ──▶ Lobby ──▶ Room ──▶ Battle ──▶ GameOver
//     ▲                                                        │              │
//     └──────────────────────────────────────────────────────────┴── Menu ◀──┘
//   （Battle ─(玩家死亡)─▶ GameOver ─(返回房间)─▶ Room ─(全部准备)─▶ Battle 循环）
//------------------------------------------------------------

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 流程状态机定义（GF 规范）：类型名 + 注册顺序 + 入口。
    /// 仅供场景配置（ProcedureComponent）与编辑器 Builder 引用。
    /// </summary>
    public static class ProcedureTypes
    {
        /// <summary>入口流程：启动后第一个进入的状态。</summary>
        public const string Entrance = "EmojiWar.GameMain.Procedure.ProcedureLaunch";

        /// <summary>
        /// 可用流程注册顺序（与 Menu 场景 ProcedureComponent.m_AvailableProcedureTypeNames 一致）。
        /// 数组下标 = 注册顺序（GF 反射创建顺序），入口必须位于其中。
        /// 注意：独立选角色流程已移除（角色在房间内选择，见 CharacterDockForm）。
        /// 2026-09-04：新增多人游戏中转（ProcedureMultiplayer：创建/加入）与
        /// 加入列表（ProcedureJoinRoom：局域网房间发现列表）。
        /// </summary>
        public static readonly string[] Available =
        {
            "EmojiWar.GameMain.Procedure.ProcedureLaunch",
            "EmojiWar.GameMain.Procedure.ProcedureMenu",
            "EmojiWar.GameMain.Procedure.ProcedureMultiplayer",
            "EmojiWar.GameMain.Procedure.ProcedureJoinRoom",
            "EmojiWar.GameMain.Procedure.ProcedureLobby",
            "EmojiWar.GameMain.Procedure.ProcedureRoom",
            "EmojiWar.GameMain.Procedure.ProcedureBattle",
            "EmojiWar.GameMain.Procedure.ProcedureGameOver",
        };
    }
}
