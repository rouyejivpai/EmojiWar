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

        // 可复用编码缓冲（高频消息：输入上行/输入帧广播，避免每帧 MemoryStream/byte[] 分配）
        private static readonly MemoryStream s_EncodeStream = new MemoryStream(256);
        private static byte[] s_EncodeFrame = new byte[512];

        // 可复用解码缓冲（高频输入帧拆帧，避免 GetRange().ToArray() 每帧分配）
        private static byte[] s_DecodeFrame = new byte[512];
        private static readonly MemoryStream s_DecodeStream = new MemoryStream(512);   // 复用解码流（避免每帧 new）
        private static readonly BinaryReader s_DecodeReader;                            // 复用读取器

        private static readonly Dictionary<ushort, Func<NetMessage>> s_Factories = new Dictionary<ushort, Func<NetMessage>>();

        static NetCodec()
        {
            s_DecodeReader = new BinaryReader(s_DecodeStream, System.Text.Encoding.UTF8, true);
            Register<C2SJoinRoom>();
            Register<C2SPlayerInput>();
            Register<C2SBuyItem>();
            Register<C2SReadyChange>();
            Register<C2SChangeCharacter>();
            Register<C2SShopContinue>();
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
            Register<S2CChangeCharacter>();
            Register<S2CInputFrame>();
            Register<S2CStateCheck>();
            Register<S2CPlayerList>();
            Register<S2CRoomClosed>();
            Register<S2CJoinRejected>();
            Register<S2CShopContinue>();
            Register<NetHeartbeat>();
        }

        private static void Register<T>() where T : NetMessage, new()
        {
            var msg = new T();
            s_Factories[(ushort)msg.Id] = () => new T();
        }

        /// <summary>
        /// 序列化消息为完整帧（含头）并输出帧长度。
        /// 复用静态缓冲：单线程主线程调用（网络层在主线程轮询），无并发冲突。
        /// 注意：返回的数组是共享缓冲，length 为有效字节数，调用方必须立即使用。
        /// </summary>
        public static byte[] Encode(NetMessage message, out int length)
        {
            // 复用编码流（重置后写入）
            s_EncodeStream.SetLength(0);
            using (var writer = new BinaryWriter(s_EncodeStream, System.Text.Encoding.UTF8, true))
            {
                message.Serialize(writer);
            }

            int payloadLength = (int)s_EncodeStream.Length;
            int total = HeaderLength + payloadLength;

            // 帧缓冲不足时扩容（静态字段可重新赋值）
            if (s_EncodeFrame.Length < total)
            {
                Array.Resize(ref s_EncodeFrame, total * 2);
            }

            byte[] frame = s_EncodeFrame;
            frame[0] = (byte)((ushort)message.Id & 0xFF);
            frame[1] = (byte)(((ushort)message.Id >> 8) & 0xFF);
            frame[2] = (byte)(payloadLength & 0xFF);
            frame[3] = (byte)((payloadLength >> 8) & 0xFF);
            // Payload：从编码流直接拷贝
            System.Buffer.BlockCopy(s_EncodeStream.GetBuffer(), 0, frame, HeaderLength, payloadLength);

            length = total;
            return frame;
        }

        /// <summary>兼容旧调用：仅序列化返回帧（不做长度输出，内部一次性分配）。</summary>
        public static byte[] Encode(NetMessage message)
        {
            var bytes = Encode(message, out int length);
            if (length == bytes.Length)
            {
                return bytes;
            }
            var copy = new byte[length];
            Array.Copy(bytes, copy, length);
            return copy;
        }

        /// <summary>
        /// 从帧解码消息（复用解码流/读取器，避免每帧分配）。
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
            s_DecodeStream.SetLength(0);
            s_DecodeStream.Write(frame, offset + HeaderLength, length);
            s_DecodeStream.Position = 0;
            message.Deserialize(s_DecodeReader);
            return message;
        }

        /// <summary>
        /// 从接收缓冲（List&lt;byte&gt;）解码消息（高频输入帧路径：免 GetRange().ToArray() 分配）。
        /// 复用静态解码缓冲，单线程主线程调用安全。
        /// </summary>
        public static NetMessage DecodeFromList(List<byte> buffer, int offset, int count)
        {
            if (count < HeaderLength)
            {
                return null;
            }

            ushort msgId = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
            int length = buffer[offset + 2] | (buffer[offset + 3] << 8);
            if (count < HeaderLength + length)
            {
                return null;
            }

            if (!s_Factories.TryGetValue(msgId, out var factory))
            {
                return null;
            }

            int total = HeaderLength + length;
            if (s_DecodeFrame.Length < total)
            {
                Array.Resize(ref s_DecodeFrame, total * 2);
            }
            // 批量拷贝到复用缓冲
            for (int i = 0; i < total; i++)
            {
                s_DecodeFrame[i] = buffer[offset + i];
            }

            var message = factory();
            s_DecodeStream.SetLength(0);
            s_DecodeStream.Write(s_DecodeFrame, HeaderLength, length);
            s_DecodeStream.Position = 0;
            message.Deserialize(s_DecodeReader);
            return message;
        }
    }
}
