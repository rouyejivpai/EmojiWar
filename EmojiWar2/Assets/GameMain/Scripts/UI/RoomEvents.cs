//------------------------------------------------------------
// EmojiWar GameMain - 房间事件中心
// 供 NetClientLogic（网络层）与 RoomForm/ProcedureRoom（流程/UI 层）解耦通信。
//------------------------------------------------------------

using System;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 房间相关事件（网络层触发，流程/UI 订阅）。
    /// </summary>
    public static class RoomEvents
    {
        /// <summary>房间玩家列表更新（参数："名字:准备;名字:准备;..."）。</summary>
        public static event Action<string> OnPlayerListUpdated;

        /// <summary>战斗开始（全部准备后由 Host 广播）。</summary>
        public static event Action OnBattleStart;

        /// <summary>房间解散/连接断开（房主退出），客户端应返回大厅。</summary>
        public static event Action OnRoomClosed;

        /// <summary>本机准备状态变化（UI 按钮状态用）。</summary>
        public static event Action<bool> OnLocalReadyChanged;

        public static void PlayerListUpdated(string players) { OnPlayerListUpdated?.Invoke(players); }
        public static void BattleStart() { OnBattleStart?.Invoke(); }
        public static void RoomClosed() { OnRoomClosed?.Invoke(); }
        public static void LocalReadyChanged(bool ready) { OnLocalReadyChanged?.Invoke(ready); }
    }
}
