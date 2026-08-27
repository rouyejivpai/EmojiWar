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
        private int m_Consumed = 0;                     // 已拆帧消费的字节数（游标，避免每次 RemoveRange O(n) 前移）

        public int Id { get; private set; }

        public NetServerSession(int id, TcpClient client)
        {
            Id = id;
            m_Client = client;
            m_Stream = client.GetStream();
        }

        private byte[] m_ReadBuffer = new byte[4096];   // 复用读取缓冲（避免每帧分配）
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
                byte[] frame = NetCodec.Encode(message, out int length);
                m_Stream.Write(frame, 0, length);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServerSession] Send failed: " + e.Message);
            }
        }

        public void Poll()
        {
            if (!IsAlive || m_Stream == null)
            {
                return;
            }

            // 主动检测对端关闭（FIN）：可读但无数据 = 连接关闭
            try
            {
                if (m_Client.Client.Poll(0, SelectMode.SelectRead) && m_Client.Available == 0)
                {
                    Disconnect();
                    return;
                }
            }
            catch
            {
                Disconnect();
                return;
            }

            if (!m_Stream.DataAvailable)
            {
                return;
            }

            try
            {
                int read = m_Stream.Read(m_ReadBuffer, 0, m_ReadBuffer.Length);
                if (read <= 0)
                {
                    Disconnect();
                    return;
                }

                // 批量追加（避免逐字节 Add 的 List 扩容开销）
                int need = m_ReceiveBuffer.Count + read;
                if (m_ReceiveBuffer.Capacity < need)
                {
                    m_ReceiveBuffer.Capacity = need;
                }
                for (int i = 0; i < read; i++)
                {
                    m_ReceiveBuffer.Add(m_ReadBuffer[i]);
                }

                ProcessBuffer();

                // 批量消费完成后统一压缩：仅当已消费字节较多时前移一次（避免每帧 O(n)）
                if (m_Consumed > 0)
                {
                    if (m_Consumed >= m_ReceiveBuffer.Count)
                    {
                        m_ReceiveBuffer.Clear();
                    }
                    else
                    {
                        m_ReceiveBuffer.RemoveRange(0, m_Consumed);
                    }
                    m_Consumed = 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServerSession] Poll failed: " + e.Message);
                Disconnect();
            }
        }

        private void ProcessBuffer()
        {
            int count = m_ReceiveBuffer.Count;
            while (count - m_Consumed >= NetCodec.HeaderLength)
            {
                int offset = m_Consumed;
                int length = m_ReceiveBuffer[offset + 2] | (m_ReceiveBuffer[offset + 3] << 8);
                int total = NetCodec.HeaderLength + length;

                if (count - m_Consumed < total)
                {
                    break;
                }

                // 免拷贝解码（复用静态缓冲，避免 GetRange().ToArray() 每帧分配）
                var message = NetCodec.DecodeFromList(m_ReceiveBuffer, offset, total);
                m_Consumed += total;

                if (message != null)
                {
                    // 高频消息（输入帧）不打日志，避免每帧 Debug.Log 严重掉帧
                    if (message.Id != MsgId.PlayerInput && message.Id != MsgId.InputFrame)
                    {
                        Debug.Log("[NetServerSession] Received msg " + message.Id + " from session " + Id);
                    }
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
        private readonly List<NetServerSession> m_SessionList = new List<NetServerSession>();   // 复用遍历列表（避免每帧分配）
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
                WriteProbe("[net-host] NetServer listening on " + m_Listener.LocalEndpoint);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServer] Start failed: " + e.Message);
                WriteProbe("[net-host] NetServer Start FAILED: " + e.Message);
                return false;
            }
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
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
                if (pending)
                {
                    try
                    {
                        TcpClient client = m_Listener.AcceptTcpClient();
                        client.NoDelay = true;   // 禁用 Nagle，降低小包延迟
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

            // 处理各会话消息（复用列表避免每帧分配；会话只在连接/断开时增删）
            m_SessionList.Clear();
            lock (m_SyncRoot)
            {
                m_SessionList.AddRange(m_Sessions.Values);
            }
            foreach (var session in m_SessionList)
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
            // 复用列表避免每帧分配（InputFrame 20Hz 广播高频）
            m_SessionList.Clear();
            lock (m_SyncRoot)
            {
                m_SessionList.AddRange(m_Sessions.Values);
            }
            // 高频消息（输入帧 20Hz）不打日志，避免每帧 Debug.Log 严重掉帧
            if (message.Id != MsgId.InputFrame)
            {
                Debug.Log("[NetServer] Broadcast " + message.Id + " to " + m_SessionList.Count + " sessions");
            }
            foreach (var session in m_SessionList)
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
