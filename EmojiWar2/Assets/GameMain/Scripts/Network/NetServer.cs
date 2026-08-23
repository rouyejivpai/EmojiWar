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
                    Debug.Log("[NetServerSession] Received msg " + message.Id + " from session " + Id);
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
        private readonly object m_SyncRoot = new object();
        private int m_NextSessionId = 1;

        public bool IsRunning { get; private set; }
        public int SessionCount
        {
            get
            {
                lock (m_SyncRoot)
                {
                    return m_Sessions.Count;
                }
            }
        }
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
                IsRunning = true;
                Debug.Log("[NetServer] Listening on " + m_Listener.LocalEndpoint);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServer] Start failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 每帧轮询（主线程）：接受新连接 + 处理各会话消息。
        /// 主线程同步模型，避免异步回调线程竞态。
        /// </summary>
        public void Poll()
        {
            // 接受新连接（主线程同步）
            if (m_Listener != null)
            {
                bool pending = m_Listener.Pending();
                if (Time.frameCount % 30 == 0)
                {
                    Debug.Log("[NetServer] Poll pending=" + pending + " sessions=" + m_Sessions.Count);
                }
                if (pending)
                {
                    try
                    {
                        TcpClient client = m_Listener.AcceptTcpClient();
                        int sessionId = m_NextSessionId++;
                        var session = new NetServerSession(sessionId, client);
                        session.OnMessage += (id, msg) => OnMessage?.Invoke(id, msg);
                        session.OnDisconnected += OnSessionDisconnected;
                        lock (m_SyncRoot)
                        {
                            m_Sessions[sessionId] = session;
                        }

                        Debug.Log("[NetServer] Accepted client, sessionId=" + sessionId);
                        OnClientConnected?.Invoke(sessionId);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[NetServer] Accept failed: " + e.Message);
                    }
                }
            }

            // 处理各会话消息（先拷贝避免遍历中修改）
            List<NetServerSession> sessions;
            lock (m_SyncRoot)
            {
                sessions = new List<NetServerSession>(m_Sessions.Values);
            }
            foreach (var session in sessions)
            {
                if (session.IsAlive)
                {
                    session.Poll();
                }
            }
        }

        private void OnSessionDisconnected(int sessionId)
        {
            lock (m_SyncRoot)
            {
                if (m_Sessions.Remove(sessionId))
                {
                    OnClientDisconnected?.Invoke(sessionId);
                }
            }
        }

        /// <summary>
        /// 向所有会话广播消息。
        /// </summary>
        public void Broadcast(NetMessage message)
        {
            List<NetServerSession> sessions;
            lock (m_SyncRoot)
            {
                sessions = new List<NetServerSession>(m_Sessions.Values);
            }
            foreach (var session in sessions)
            {
                session.Send(message);
            }
        }

        /// <summary>
        /// 向指定会话发送消息。
        /// </summary>
        public void SendTo(int sessionId, NetMessage message)
        {
            NetServerSession session;
            lock (m_SyncRoot)
            {
                m_Sessions.TryGetValue(sessionId, out session);
            }
            session?.Send(message);
        }

        /// <summary>
        /// 停止服务器。
        /// </summary>
        public void Stop()
        {
            IsRunning = false;

            // 先拷贝再遍历，避免 Disconnect 事件回调修改字典导致枚举异常
            List<NetServerSession> sessions;
            lock (m_SyncRoot)
            {
                sessions = new List<NetServerSession>(m_Sessions.Values);
            }
            foreach (var session in sessions)
            {
                session.Disconnect();
            }
            lock (m_SyncRoot)
            {
                m_Sessions.Clear();
            }

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
