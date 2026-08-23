//------------------------------------------------------------
// EmojiWar GameMain - TCP 服务器（Host 权威）
// 监听端口，管理多个客户端连接，消息广播。
// 主线程轮询驱动（Unity 友好）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 服务端会话（单个客户端连接）。
    /// </summary>
    public sealed class NetServerSession
    {
        private readonly TcpClient m_Client;
        private NetworkStream m_Stream;
        private readonly List<byte> m_ReceiveBuffer = new List<byte>();

        public int Id { get; private set; }

        public NetServerSession(int id, TcpClient client)
        {
            Id = id;
            m_Client = client;
            m_Stream = client.GetStream();
        }
        public bool IsAlive
        {
            get { return m_Client != null && m_Client.Connected; }
        }

        /// <summary>收到消息事件（参数：sessionId, message）。</summary>
        public event Action<int, NetMessage> OnMessage;

        /// <summary>断开事件。</summary>
        public event Action<int> OnDisconnected;

        public void Send(NetMessage message)
        {
            if (!IsAlive || m_Stream == null || message == null)
            {
                return;
            }

            try
            {
                byte[] frame = NetCodec.Encode(message);
                m_Stream.Write(frame, 0, frame.Length);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServerSession] Send failed: " + e.Message);
            }
        }

        public void Poll()
        {
            if (!IsAlive || m_Stream == null || !m_Stream.DataAvailable)
            {
                return;
            }

            try
            {
                byte[] buffer = new byte[4096];
                int read = m_Stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    Disconnect();
                    return;
                }

                for (int i = 0; i < read; i++)
                {
                    m_ReceiveBuffer.Add(buffer[i]);
                }

                ProcessBuffer();
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServerSession] Poll failed: " + e.Message);
                Disconnect();
            }
        }

        private void ProcessBuffer()
        {
            while (m_ReceiveBuffer.Count >= NetCodec.HeaderLength)
            {
                int length = m_ReceiveBuffer[2] | (m_ReceiveBuffer[3] << 8);
                int total = NetCodec.HeaderLength + length;

                if (m_ReceiveBuffer.Count < total)
                {
                    break;
                }

                byte[] frame = m_ReceiveBuffer.GetRange(0, total).ToArray();
                m_ReceiveBuffer.RemoveRange(0, total);

                var message = NetCodec.Decode(frame, 0, total);
                if (message != null)
                {
                    OnMessage?.Invoke(Id, message);
                }
            }
        }

        public void Disconnect()
        {
            try
            {
                if (m_Stream != null)
                {
                    m_Stream.Close();
                    m_Stream = null;
                }
                if (m_Client != null)
                {
                    m_Client.Close();
                }
            }
            catch
            {
            }

            m_ReceiveBuffer.Clear();
            OnDisconnected?.Invoke(Id);
        }
    }

    /// <summary>
    /// TCP 服务器。
    /// </summary>
    public class NetServer
    {
        private TcpListener m_Listener = null;
        private readonly Dictionary<int, NetServerSession> m_Sessions = new Dictionary<int, NetServerSession>();
        private int m_NextSessionId = 1;

        public bool IsRunning { get; private set; }
        public int SessionCount { get { return m_Sessions.Count; } }
        public IReadOnlyCollection<NetServerSession> Sessions { get { return m_Sessions.Values; } }

        /// <summary>新客户端连接事件。</summary>
        public event Action<int> OnClientConnected;

        /// <summary>收到消息事件。</summary>
        public event Action<int, NetMessage> OnMessage;

        /// <summary>客户端断开事件。</summary>
        public event Action<int> OnClientDisconnected;

        /// <summary>
        /// 启动监听。
        /// </summary>
        public bool Start(int port)
        {
            try
            {
                m_Listener = new TcpListener(IPAddress.Any, port);
                m_Listener.Start();
                m_Listener.BeginAcceptTcpClient(OnAcceptCallback, null);
                IsRunning = true;
                Debug.Log("[NetServer] Listening on port " + port);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServer] Start failed: " + e.Message);
                return false;
            }
        }

        private void OnAcceptCallback(IAsyncResult ar)
        {
            try
            {
                if (m_Listener == null)
                {
                    return;
                }

                TcpClient client = m_Listener.EndAcceptTcpClient(ar);
                int sessionId = m_NextSessionId++;
                var session = new NetServerSession(sessionId, client);
                session.OnMessage += (id, msg) => OnMessage?.Invoke(id, msg);
                session.OnDisconnected += OnSessionDisconnected;
                m_Sessions[sessionId] = session;

                OnClientConnected?.Invoke(sessionId);

                // 继续接受新连接
                m_Listener.BeginAcceptTcpClient(OnAcceptCallback, null);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServer] Accept failed: " + e.Message);
            }
        }

        private void OnSessionDisconnected(int sessionId)
        {
            if (m_Sessions.Remove(sessionId))
            {
                OnClientDisconnected?.Invoke(sessionId);
            }
        }

        /// <summary>
        /// 每帧轮询所有会话。
        /// </summary>
        public void Poll()
        {
            foreach (var session in m_Sessions.Values)
            {
                session.Poll();
            }
        }

        /// <summary>
        /// 向所有会话广播消息。
        /// </summary>
        public void Broadcast(NetMessage message)
        {
            foreach (var session in m_Sessions.Values)
            {
                session.Send(message);
            }
        }

        /// <summary>
        /// 向指定会话发送消息。
        /// </summary>
        public void SendTo(int sessionId, NetMessage message)
        {
            if (m_Sessions.TryGetValue(sessionId, out var session))
            {
                session.Send(message);
            }
        }

        /// <summary>
        /// 停止服务器。
        /// </summary>
        public void Stop()
        {
            IsRunning = false;

            foreach (var session in m_Sessions.Values)
            {
                session.Disconnect();
            }
            m_Sessions.Clear();

            if (m_Listener != null)
            {
                try
                {
                    m_Listener.Stop();
                }
                catch
                {
                }
                m_Listener = null;
            }
        }
    }
}
