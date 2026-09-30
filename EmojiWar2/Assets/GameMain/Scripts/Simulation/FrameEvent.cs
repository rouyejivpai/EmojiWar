//------------------------------------------------------------
// EmojiWar Sim - 帧事件（W-16 的**纯数据部分**）
//
// [W-04] 本文件只放"模拟层自己能懂的东西"：事件结构 + 种类常量。
// 应用逻辑（`SimFrameEvents.Apply`）要用 `Data.ConfigService` 把 Id 查成武器配置，
// 那是引擎侧的数据服务 → 它在 `Scripts/SimBridge/FrameEvent.cs`。
//
// 拆分意义：`ReplayRecorder`/`ReplayPlayer` 在模拟层里要记录/读回帧事件，只需要这个结构体；
// 不该因为"应用时要查表"就把整个模拟层绑到 Data 上。
//
// 为什么事件必须随帧携带：任何"改变模拟"的指令（开下一波/换角色/买到新武器）如果由带外消息
// 直接改模拟，其生效帧号就取决于"消息什么时候被处理"——两端并不一致（实测踩过两次）。
// 参数一律用 **Id**（换武器只传 weaponItemId，两端各自查同一张表构造配置）。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>随输入帧携带的确定性事件。</summary>
    public struct FrameEvent
    {
        public byte Kind;
        public int Arg0;
        public int Arg1;
    }

    /// <summary>帧事件种类（新增种类：这里加常量 + SimBridge 的 `SimFrameEvents.Apply` 加分支）。</summary>
    public static class FrameEventKinds
    {
        /// <summary>开下一波（商店继续）。</summary>
        public const byte ShopContinue = 1;
        /// <summary>换角色：Arg0=entityId, Arg1=characterId。</summary>
        public const byte SetCharacter = 2;
        /// <summary>换武器：Arg0=entityId, Arg1=weaponItemId（两端按 Id 查表）。</summary>
        public const byte WeaponUpdate = 3;
    }
}