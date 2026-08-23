//------------------------------------------------------------
// EmojiWar GameMain - 网络消息实现
// 具体消息类：序列化/反序列化。
//------------------------------------------------------------

using System.IO;

namespace EmojiWar.GameMain.Network
{
    // ==================== C2S ====================

    /// <summary>加入房间。</summary>
    public sealed class C2SJoinRoom : NetMessage
    {
        public string PlayerName;

        public override MsgId Id { get { return MsgId.JoinRoom; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(PlayerName ?? string.Empty);
        }

        public override void Deserialize(BinaryReader reader)
        {
            PlayerName = reader.ReadString();
        }
    }

    /// <summary>玩家输入（每帧上行）。</summary>
    public sealed class C2SPlayerInput : NetMessage
    {
        public float InputX;
        public float InputY;
        public float AimX;
        public float AimY;
        public bool FirePrimary;
        public bool FireSecondary;

        public override MsgId Id { get { return MsgId.PlayerInput; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(InputX);
            writer.Write(InputY);
            writer.Write(AimX);
            writer.Write(AimY);
            writer.Write(FirePrimary);
            writer.Write(FireSecondary);
        }

        public override void Deserialize(BinaryReader reader)
        {
            InputX = reader.ReadSingle();
            InputY = reader.ReadSingle();
            AimX = reader.ReadSingle();
            AimY = reader.ReadSingle();
            FirePrimary = reader.ReadBoolean();
            FireSecondary = reader.ReadBoolean();
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

    // ==================== S2C ====================

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

        public override MsgId Id { get { return MsgId.SpawnEntity; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(EntityId);
            writer.Write(Type);
            writer.Write(Team);
            writer.Write(X);
            writer.Write(Y);
        }

        public override void Deserialize(BinaryReader reader)
        {
            EntityId = reader.ReadInt32();
            Type = reader.ReadInt32();
            Team = reader.ReadInt32();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
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
