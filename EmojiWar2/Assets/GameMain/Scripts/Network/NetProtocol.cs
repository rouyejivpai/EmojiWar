//------------------------------------------------------------
// EmojiWar GameMain - 网络协议定义
// 自研二进制协议，帧格式（规划 §5）：
//   [消息ID: ushort][长度: ushort][Payload: bytes]
// 传输层：TCP（Host 权威）。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 消息 ID 定义。
    /// 客户端→服务器 (C2S) 与服务器→客户端 (S2C) 共用 ID 空间，按方向区分。
    /// </summary>
    public enum MsgId : ushort
    {
        // ---- C2S ----
        JoinRoom = 1001,        // 加入房间 { playerName }
        LeaveRoom = 1002,       // 离开房间
        PlayerInput = 1101,     // 输入上行 { inputX, inputY, aimX, aimY, firePrimary, fireSecondary }
        BuyItem = 1201,         // 购买 { shopItemIndex }
        EquipMod = 1202,        // 装备 Mod { modId }

        // ---- S2C ----
        RoomState = 2001,       // 房间状态 { roomId, players[] }
        PlayerJoined = 2002,    // 玩家加入 { playerId, playerName }
        PlayerLeft = 2003,      // 玩家离开 { playerId }
        BattleStart = 2101,     // 战斗开始 { seed, characterId }
        SpawnEntity = 2102,     // 生成实体 { entityId, type, team, x, y }
        EntityState = 2103,     // 实体状态同步 { entityId, x, y, hp, state }
        RemoveEntity = 2104,    // 移除实体 { entityId }
        WaveState = 2201,       // 波次状态 { waveIndex, aliveCount }
        ShopOffer = 2202,       // 商店商品 { items[] }
        GameOver = 2999,        // 游戏结束 { win, waveIndex }

        // ---- 心跳 ----
        Heartbeat = 9001,
    }

    /// <summary>
    /// 网络消息基类（所有消息继承）。
    /// </summary>
    public abstract class NetMessage
    {
        public abstract MsgId Id { get; }

        /// <summary>
        /// 序列化到写入器。
        /// </summary>
        public abstract void Serialize(System.IO.BinaryWriter writer);

        /// <summary>
        /// 从读取器反序列化。
        /// </summary>
        public abstract void Deserialize(System.IO.BinaryReader reader);
    }
}
