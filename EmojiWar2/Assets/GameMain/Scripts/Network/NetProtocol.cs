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
        ReadyChange = 1103,     // 准备/取消准备 { ready }
        ChangeCharacterReq = 1105, // C2S: 请求切换角色 { characterId }
        ShopContinueReq = 1206, // C2S: 商店阶段点"继续"，请求开始下一波（无负载）
        // 注意：C2S 与 S2C 不得共用同一 MsgId！NetCodec 工厂是"按 ID 单字典"注册，
        // 若 C2SChangeCharacter 与 S2CChangeCharacter 同为 ChangeCharacter(=1104)，
        // 后注册的 S2C 会覆盖 C2S 工厂 → Host 收到 C2S 帧时用 S2C 类型反序列化，
        // payload 长度不符 → BinaryReader 越界 → session Poll 崩溃 → 客户端被踢回大厅。
        // （历史 bug：Client 点角色卡片即断线回大厅；Host 本地切换不走网络故正常。）
        ChangeCharacter = 1104, // S2C: 角色变更广播 { entityId, characterId }

        // ---- S2C ----
        RoomState = 2001,       // 房间状态 { roomId, players[] }
        PlayerJoined = 2002,    // 玩家加入 { playerId, playerName }
        PlayerLeft = 2003,      // 玩家离开 { playerId }
        BattleStart = 2101,     // 战斗开始 { seed, characterId }
        SpawnEntity = 2102,     // 生成实体 { entityId, type, team, x, y }
        EntityState = 2103,     // 实体状态同步 { entityId, x, y, hp, state }
        RemoveEntity = 2104,    // 移除实体 { entityId }
        WaveState = 2201,       // 波次状态 { waveIndex, aliveCount, waveActive }
        ShopOffer = 2202,       // 商店商品 { items[] }
        GameOver = 2999,        // 游戏结束 { win, waveIndex }
        RunRestart = 2105,      // S2C: host restarts the run for a new round
        MyEntity = 2106,        // S2C: host tells a joiner its own entity id { entityId }
        InputFrame = 2107,      // S2C: 输入帧广播（帧同步；各端推进确定性模拟）
        WeaponUpdate = 2108,    // S2C: 武器更新（购买武器后同步模拟参数）{ entityId, weaponId, ... }
        StateCheck = 2109,      // S2C: 定期状态对账（Host 每 N 帧下发帧号+确定性状态哈希；客户端同帧对比）
        JoinRejected = 2110,    // S2C: 加入被拒绝（例如对局已开始，无法中途加入）
        PlayerList = 2203,      // S2C: 房间玩家列表（名字+准备状态）{ players }
        RoomClosed = 2204,      // S2C: 房间解散（房主退出）
        ShopContinue = 2205,    // S2C: 商店继续（开始下一波；无负载，各端 RequestNextWave）

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
