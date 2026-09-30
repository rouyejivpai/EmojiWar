//------------------------------------------------------------
// EmojiWar GameMain - 网络消息实现
// 具体消息类：序列化/反序列化。
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;

namespace EmojiWar.GameMain.Network
{
    // ==================== C2S ====================

    /// <summary>加入房间。</summary>
    public sealed class C2SJoinRoom : NetMessage
    {
        public string PlayerName;
        public int CharacterId = 1;

        /// <summary>[W-07] 本端配置版本哈希（`ConfigService.VersionHash`）。</summary>
        public ulong ConfigHash;

        /// <summary>
        /// [W-07] 本端逻辑层**代码**哈希（`SimBuildInfo.CodeHash`）—— 配置哈希发现不了"代码不一致"。
        /// [W-11b] 它只哈希**跨后端一致**的比较用指纹（语义版本 + 逻辑帧率），
        /// 不含 MVID/buildGUID 这类"构建标识" —— 否则"编辑器(Mono) 与 IL2CPP 包同源"会被误判成版本不一致。
        /// </summary>
        public ulong CodeHash;

        public override MsgId Id { get { return MsgId.JoinRoom; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(PlayerName ?? string.Empty);
            writer.Write(CharacterId);
            writer.Write(ConfigHash);
            writer.Write(CodeHash);
        }

        public override void Deserialize(BinaryReader reader)
        {
            PlayerName = reader.ReadString();
            CharacterId = reader.ReadInt32();
            ConfigHash = reader.ReadUInt64();
            CodeHash = reader.ReadUInt64();
        }
    }

    /// <summary>玩家输入（帧同步意图上行：只传意图，不传位置结果）。</summary>
    public sealed class C2SPlayerInput : NetMessage
    {
        public float InputX;
        public float InputY;
        public float AimX;
        public float AimY;
        public bool FirePrimary;
        public bool FireSecondary;
        public bool Reload;

        /// <summary>
        /// [W-12] 采样该输入时**客户端的本地模拟帧号**（仅用于日志/排查，**不参与新鲜度判定**）。
        /// 它会在开战（重建战斗模拟）时归零 —— 拿它判新鲜度会造成"开战后永远判成过期 → 永久托管"。
        /// </summary>
        public int FrameIndex;

        /// <summary>
        /// [W-12] **单调递增的发送序号**（房主判"输入源是否还在更新"只用它）。
        /// 之所以不用帧号：客户端在开战/回房间时会**重建模拟**、帧号归零，而房主记的"上次消费值"
        /// 还是旧的大值 → 之后每条输入都被判成"过期" → 玩家被永久托管（实测：角色整场停在原点）。
        /// 发送序号没有归零问题。
        /// </summary>
        public int SendSeq;

        /// <summary>[W-12] 本采样窗口内的**边沿**位：bit0=FirePrimary 按下、bit1=松开、bit2=FireSecondary 按下、bit3=松开。</summary>
        public byte EdgeFlags;

        public override MsgId Id { get { return MsgId.PlayerInput; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(InputX);
            writer.Write(InputY);
            writer.Write(AimX);
            writer.Write(AimY);
            writer.Write(FirePrimary);
            writer.Write(FireSecondary);
            writer.Write(Reload);
            writer.Write(FrameIndex);
            writer.Write(SendSeq);
            writer.Write(EdgeFlags);
        }

        public override void Deserialize(BinaryReader reader)
        {
            InputX = reader.ReadSingle();
            InputY = reader.ReadSingle();
            AimX = reader.ReadSingle();
            AimY = reader.ReadSingle();
            FirePrimary = reader.ReadBoolean();
            FireSecondary = reader.ReadBoolean();
            Reload = reader.ReadBoolean();
            FrameIndex = reader.ReadInt32();
            SendSeq = reader.ReadInt32();
            EdgeFlags = reader.ReadByte();
        }
    }

    /// <summary>输入帧广播（Host 每 tick 收齐所有玩家意图后广播，各端据此推进确定性模拟）。</summary>
    public sealed class S2CInputFrame : NetMessage
    {
        public int FrameIndex;
        public int Count;
        public int[] EntityIds;
        public float[] InputXs;
        public float[] InputYs;
        public float[] AimXs;
        public float[] AimYs;
        public bool[] FirePrimaries;
        public bool[] FireSecondaries;
        public bool[] Reloads;

        /// <summary>
        /// [W-12] 每名玩家是否处于**托管**（房主判定"该玩家输入源已断 ≥0.5 秒"，用空输入代打）。
        /// 必须随帧广播：托管改变的是**模拟推进**，两端不一致就会分叉（并且它进了状态哈希）。
        /// </summary>
        public bool[] Managed;

        /// <summary>
        /// [W-16] 本帧要执行的确定性事件（帧事件批）。定义与"怎么应用"都在
        /// `EmojiWar.GameMain.Simulation.FrameEvent` / `SimFrameEvents` —— 网络层与回放器共用同一份。
        /// </summary>
        public List<Simulation.FrameEvent> Events;

        public override MsgId Id { get { return MsgId.InputFrame; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(FrameIndex);
            writer.Write(Count);
            int evCount = Events != null ? Events.Count : 0;
            writer.Write(evCount);
            for (int i = 0; i < evCount; i++)
            {
                writer.Write(Events[i].Kind);
                writer.Write(Events[i].Arg0);
                writer.Write(Events[i].Arg1);
            }
            for (int i = 0; i < Count; i++)
            {
                writer.Write(EntityIds[i]);
                writer.Write(InputXs[i]);
                writer.Write(InputYs[i]);
                writer.Write(AimXs[i]);
                writer.Write(AimYs[i]);
                writer.Write(FirePrimaries[i]);
                writer.Write(FireSecondaries[i]);
                writer.Write(Reloads[i]);
                writer.Write(Managed != null && i < Managed.Length && Managed[i]);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            FrameIndex = reader.ReadInt32();
            Count = reader.ReadInt32();
            int evCount = reader.ReadInt32();
            if (evCount > 0)
            {
                if (Events == null) { Events = new List<Simulation.FrameEvent>(evCount); }
                Events.Clear();
                for (int i = 0; i < evCount; i++)
                {
                    Simulation.FrameEvent e;
                    e.Kind = reader.ReadByte();
                    e.Arg0 = reader.ReadInt32();
                    e.Arg1 = reader.ReadInt32();
                    Events.Add(e);
                }
            }
            else if (Events != null) { Events.Clear(); }
            EntityIds = new int[Count];
            InputXs = new float[Count];
            InputYs = new float[Count];
            AimXs = new float[Count];
            AimYs = new float[Count];
            FirePrimaries = new bool[Count];
            FireSecondaries = new bool[Count];
            Reloads = new bool[Count];
            Managed = new bool[Count];
            for (int i = 0; i < Count; i++)
            {
                EntityIds[i] = reader.ReadInt32();
                InputXs[i] = reader.ReadSingle();
                InputYs[i] = reader.ReadSingle();
                AimXs[i] = reader.ReadSingle();
                AimYs[i] = reader.ReadSingle();
                FirePrimaries[i] = reader.ReadBoolean();
                FireSecondaries[i] = reader.ReadBoolean();
                Reloads[i] = reader.ReadBoolean();
                Managed[i] = reader.ReadBoolean();
            }
        }
    }

    /// <summary>购买商品。</summary>
    public sealed class C2SBuyItem : NetMessage
    {
        public int ShopItemIndex;

        public override MsgId Id { get { return MsgId.BuyItem; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(ShopItemIndex);
        }

        public override void Deserialize(BinaryReader reader)
        {
            ShopItemIndex = reader.ReadInt32();
        }
    }

    /// <summary>准备/取消准备。</summary>
    public sealed class C2SReadyChange : NetMessage
    {
        public bool Ready;

        public override MsgId Id { get { return MsgId.ReadyChange; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Ready);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Ready = reader.ReadBoolean();
        }
    }

    /// <summary>房间内切换角色请求（客户端 → Host；Host 广播 S2CChangeCharacter）。
    /// 注意：本消息必须用独立 MsgId（ChangeCharacterReq），不能与 S2CChangeCharacter 共用
    /// ChangeCharacter(=1104)——NetCodec 工厂按单字典注册，同 ID 会被后注册者覆盖，
    /// 导致 Host 收到本消息时按 S2C 类型反序列化越界 → 连接断开（Client 切角色即被踢）。</summary>
    public sealed class C2SChangeCharacter : NetMessage
    {
        public int CharacterId;

        public override MsgId Id { get { return MsgId.ChangeCharacterReq; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(CharacterId);
        }

        public override void Deserialize(BinaryReader reader)
        {
            CharacterId = reader.ReadInt32();
        }
    }

    // ==================== S2C ====================

    /// <summary>商店阶段"继续"（C2S：客户端请求开始下一波；无负载）。</summary>
    public sealed class C2SShopContinue : NetMessage
    {
        public override MsgId Id { get { return MsgId.ShopContinueReq; } }
        public override void Serialize(BinaryWriter writer) { }
        public override void Deserialize(BinaryReader reader) { }
    }

    /// <summary>商店阶段"继续"广播（S2C：Host 权威 → 各端 RequestNextWave 开始下一波；无负载）。</summary>
    public sealed class S2CShopContinue : NetMessage
    {
        public override MsgId Id { get { return MsgId.ShopContinue; } }
        public override void Serialize(BinaryWriter writer) { }
        public override void Deserialize(BinaryReader reader) { }
    }

    /// <summary>房间状态。</summary>
    public sealed class S2CRoomState : NetMessage
    {
        public string RoomId;
        public int PlayerCount;

        public override MsgId Id { get { return MsgId.RoomState; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(RoomId ?? string.Empty);
            writer.Write(PlayerCount);
        }

        public override void Deserialize(BinaryReader reader)
        {
            RoomId = reader.ReadString();
            PlayerCount = reader.ReadInt32();
        }
    }

    /// <summary>玩家加入。</summary>
    public sealed class S2CPlayerJoined : NetMessage
    {
        public int PlayerId;
        public string PlayerName;

        public override MsgId Id { get { return MsgId.PlayerJoined; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(PlayerId);
            writer.Write(PlayerName ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            PlayerId = reader.ReadInt32();
            PlayerName = reader.ReadString();
        }
    }

    /// <summary>玩家离开。</summary>
    public sealed class S2CPlayerLeft : NetMessage
    {
        public int PlayerId;

        public override MsgId Id { get { return MsgId.PlayerLeft; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(PlayerId);
        }

        public override void Deserialize(BinaryReader reader)
        {
            PlayerId = reader.ReadInt32();
        }
    }

    /// <summary>生成实体。</summary>
    public sealed class S2CSpawnEntity : NetMessage
    {
        public int EntityId;
        public int Type;        // 0=Player, 1=Enemy
        public int Team;        // 1=Player, 2=Enemy
        public float X;
        public float Y;
        public int CharacterId; // 玩家角色 ID（美术用；敌人为 0）

        public override MsgId Id { get { return MsgId.SpawnEntity; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
            writer.Write(Type);
            writer.Write(Team);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(CharacterId);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
            Type = reader.ReadInt32();
            Team = reader.ReadInt32();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            CharacterId = reader.ReadInt32();
        }
    }

    /// <summary>实体状态同步（高频）。</summary>
    public sealed class S2CEntityState : NetMessage
    {
        public int EntityId;
        public float X;
        public float Y;
        public float Hp;
        public int State;       // 0=Idle,1=Moving,2=Attacking,4=Dead

        public override MsgId Id { get { return MsgId.EntityState; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Hp);
            writer.Write(State);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            Hp = reader.ReadSingle();
            State = reader.ReadInt32();
        }
    }

    /// <summary>移除实体。</summary>
    public sealed class S2CRemoveEntity : NetMessage
    {
        public int EntityId;

        public override MsgId Id { get { return MsgId.RemoveEntity; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
        }
    }

    /// <summary>波次状态。</summary>
    public sealed class S2CWaveState : NetMessage
    {
        public int WaveIndex;
        public int AliveCount;
        public bool WaveActive;

        public override MsgId Id { get { return MsgId.WaveState; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(WaveIndex);
            writer.Write(AliveCount);
            writer.Write(WaveActive);
        }

        public override void Deserialize(BinaryReader reader)
        {
            WaveIndex = reader.ReadInt32();
            AliveCount = reader.ReadInt32();
            WaveActive = reader.ReadBoolean();
        }
    }

    /// <summary>商店商品。</summary>
    public sealed class S2CShopOffer : NetMessage
    {
        public int Count;
        public string Items;    // 简化为逗号分隔 "type:id:price;..."

        public override MsgId Id { get { return MsgId.ShopOffer; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Count);
            writer.Write(Items ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Count = reader.ReadInt32();
            Items = reader.ReadString();
        }
    }

    /// <summary>游戏结束。</summary>
    public sealed class S2CGameOver : NetMessage
    {
        public bool Win;
        public int WaveIndex;

        public override MsgId Id { get { return MsgId.GameOver; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Win);
            writer.Write(WaveIndex);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Win = reader.ReadBoolean();
            WaveIndex = reader.ReadInt32();
        }
    }

    /// <summary>心跳。</summary>
    /// <summary>S2C: host restarts the run; clients should reset their local battle.</summary>
    public sealed class S2CRunRestart : NetMessage
    {
        public int Seed;

        public override MsgId Id { get { return MsgId.RunRestart; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Seed);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Seed = reader.ReadInt32();
        }
    }

    /// <summary>S2C: 战斗开始（房间内全部准备后广播）。</summary>
    public sealed class S2CBattleStart : NetMessage
    {
        public int Seed;

        public override MsgId Id { get { return MsgId.BattleStart; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Seed);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Seed = reader.ReadInt32();
        }
    }

    /// <summary>S2C: 房间玩家列表（名字+准备状态，";"分隔 "name:ready;name:ready"）。</summary>
    public sealed class S2CPlayerList : NetMessage
    {
        public int Count;
        public string Players;    // "name:ready;name:ready;..."

        public override MsgId Id { get { return MsgId.PlayerList; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Count);
            writer.Write(Players ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Count = reader.ReadInt32();
            Players = reader.ReadString();
        }
    }

    /// <summary>S2C: 房间解散（房主退出/离开）。</summary>
    public sealed class S2CRoomClosed : NetMessage
    {
        public override MsgId Id { get { return MsgId.RoomClosed; } }

        public override void Serialize(BinaryWriter writer)
        {
        }

        public override void Deserialize(BinaryReader reader)
        {
        }
    }

    /// <summary>S2C: 加入被拒绝（例如对局已开始，无法中途加入）。
    /// 修复：以前对局已开始后 HandleJoin 仍会接受新客户端（只查人数上限），
    /// 但它永远收不到 S2CBattleStart → 停在 seed=0 房间模拟、与对局脱节且无告警。</summary>
    public sealed class S2CJoinRejected : NetMessage
    {
        public string Reason;

        public override MsgId Id { get { return MsgId.JoinRejected; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Reason ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Reason = reader.ReadString();
        }
    }

    /// <summary>
    /// C2S: 上报本机装备 Id（W-06）。
    /// 格式见 <c>Simulation/LoadoutWire.cs</c>：`"wandL:sp1,sp2:wandR:sp1,sp2"`（不含 entityId，Host 按 session 归属）。
    /// **只传 Id，不传资产引用或字符串名**（工程约定 §二）。
    /// </summary>
    public sealed class C2SLoadoutSync : NetMessage
    {
        public string HandIds;

        public override MsgId Id { get { return MsgId.LoadoutSync; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(HandIds ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            HandIds = reader.ReadString();
        }
    }

    /// <summary>
    /// S2C: 全员装备 Id 广播（W-06）。格式见 <c>Simulation/LoadoutWire.cs</c>：
    /// `"entityId:wandL:sp1,sp2:wandR:sp1,sp2;entityId:..."`。
    /// 所有端收到后都用同一份 Id 表编译 → 消除"各端按本机背包编译"的分叉源。
    /// </summary>
    public sealed class S2CLoadoutBroadcast : NetMessage
    {
        public string Loadouts;

        public override MsgId Id { get { return MsgId.LoadoutBroadcast; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Loadouts ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Loadouts = reader.ReadString();
        }
    }

    /// <summary>S2C: host tells a joiner its own entity id (client skips rendering itself).</summary>
    public sealed class S2CMyEntity : NetMessage
    {
        public int EntityId;

        public override MsgId Id { get { return MsgId.MyEntity; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
        }
    }

    /// <summary>S2C: 武器更新（购买武器后同步模拟参数，所有端一致）。</summary>
    public sealed class S2CWeaponUpdate : NetMessage
    {
        public int EntityId;        // 持有者实体 ID
        public int WeaponId;        // 武器数据表 ID
        public string WeaponName;   // 武器名
        public float Damage;
        public float FireRate;
        public float BulletSpeed;

        public override MsgId Id { get { return MsgId.WeaponUpdate; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
            writer.Write(WeaponId);
            writer.Write(WeaponName ?? string.Empty);
            writer.Write(Damage);
            writer.Write(FireRate);
            writer.Write(BulletSpeed);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
            WeaponId = reader.ReadInt32();
            WeaponName = reader.ReadString();
            Damage = reader.ReadSingle();
            FireRate = reader.ReadSingle();
            BulletSpeed = reader.ReadSingle();
        }
    }

    /// <summary>角色变更广播（Host → 全端；各端同步更新模拟玩家角色）。</summary>
    public sealed class S2CChangeCharacter : NetMessage
    {
        public int EntityId;
        public int CharacterId;

        public override MsgId Id { get { return MsgId.ChangeCharacter; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
            writer.Write(CharacterId);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
            CharacterId = reader.ReadInt32();
        }
    }

    /// <summary>
    /// 定期状态对账（Host → 客户端）：每 N 帧下发一次"逻辑帧号 + 该帧确定性状态哈希"。
    /// 客户端在同一逻辑帧计算本地哈希并对比；不等即判定不同步（告警/记录，供排查）。
    /// 纯校验不下发状态：帧同步假设下双端哈希恒等；一旦不等说明确定性被破坏，需查输入序列/随机/顺序。
    /// </summary>
    public sealed class S2CStateCheck : NetMessage
    {
        public int FrameIndex;      // Host 计算哈希时的逻辑帧号
        public long StateHash;      // 该帧确定性状态哈希（FNV-1a over 模拟实体）

        public override MsgId Id { get { return MsgId.StateCheck; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(FrameIndex);
            writer.Write(StateHash);
        }

        public override void Deserialize(BinaryReader reader)
        {
            FrameIndex = reader.ReadInt32();
            StateHash = reader.ReadInt64();
        }
    }

    /// <summary>Heartbeat.</summary>
    public sealed class NetHeartbeat : NetMessage
    {
        public long Timestamp;

        public override MsgId Id { get { return MsgId.Heartbeat; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Timestamp);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Timestamp = reader.ReadInt64();
        }
    }
}
