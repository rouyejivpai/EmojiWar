//------------------------------------------------------------
// EmojiWar GameMain - 网络编解码
// 帧格式：[消息ID: ushort][长度: ushort][Payload: bytes]
// 提供消息的序列化/反序列化工厂。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 消息编解码器：NetMessage <-> 字节帧。
    /// </summary>
    public static class NetCodec
    {
        /// <summary>帧头长度（ID 2 字节 + 长度 2 字节）。</summary>
        public const int HeaderLength = 4;

        private static readonly Dictionary<ushort, Func<NetMessage>> s_Factories = new Dictionary<ushort, Func<NetMessage>>();

        static NetCodec()
        {
            Register<C2SJoinRoom>();
            Register<C2SPlayerInput>();
            Register<C2SBuyItem>();
            Register<C2SReadyChange>();
            Register<S2CRoomState>();
            Register<S2CBattleStart>();
            Register<S2CPlayerJoined>();
            Register<S2CPlayerLeft>();
            Register<S2CSpawnEntity>();
            Register<S2CEntityState>();
            Register<S2CRemoveEntity>();
            Register<S2CWaveState>();
            Register<S2CShopOffer>();
            Register<S2CGameOver>();
            Register<S2CRunRestart>();
            Register<S2CMyEntity>();
            Register<S2CWeaponUpdate>();
            Register<S2CInputFrame>();
            Register<S2CPlayerList>();
            Register<S2CRoomClosed>();
            Register<NetHeartbeat>();
        }

        private static void Register<T>() where T : NetMessage, new()
        {
            var msg = new T();
            s_Factories[(ushort)msg.Id] = () => new T();
        }

        /// <summary>
        /// 序列化消息为完整帧（含头）。
        /// </summary>
        public static byte[] Encode(NetMessage message)
        {
            using (var payloadStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payloadStream))
                {
                    message.Serialize(writer);
                }

                byte[] payload = payloadStream.ToArray();
                byte[] frame = new byte[HeaderLength + payload.Length];

                // 消息 ID
                frame[0] = (byte)((ushort)message.Id & 0xFF);
                frame[1] = (byte)(((ushort)message.Id >> 8) & 0xFF);
                // 长度
                frame[2] = (byte)(payload.Length & 0xFF);
                frame[3] = (byte)((payload.Length >> 8) & 0xFF);
                // Payload
                Array.Copy(payload, 0, frame, HeaderLength, payload.Length);

                return frame;
            }
        }

        /// <summary>
        /// 从帧解码消息。
        /// </summary>
        public static NetMessage Decode(byte[] frame, int offset, int count)
        {
            if (count < HeaderLength)
            {
                return null;
            }

            ushort msgId = (ushort)(frame[offset] | (frame[offset + 1] << 8));
            int length = frame[offset + 2] | (frame[offset + 3] << 8);
            if (count < HeaderLength + length)
            {
                return null;
            }

            if (!s_Factories.TryGetValue(msgId, out var factory))
            {
                return null;
            }

            var message = factory();
            using (var payloadStream = new MemoryStream(frame, offset + HeaderLength, length, false))
            {
                using (var reader = new BinaryReader(payloadStream))
                {
                    message.Deserialize(reader);
                }
            }
            return message;
        }
    }
}
