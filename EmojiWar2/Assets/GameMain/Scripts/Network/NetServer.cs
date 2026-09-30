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
            // [W-19] 有界写超时：TCP 发送缓冲写满只可能是对端长时间不读（卡死/断网）。
            //   没有超时时 `Write` 会**无限阻塞主线程** → 一个坏客户端能拖死整个房主的 tick 循环。
            //   500ms 足够判定"它已经不读了"（40~100 B 的消息 × 30Hz 远小于任何 TCP 缓冲），
            //   超时抛异常 → 该连接被断开，房主继续跑（对局内的"托管"由 W-12 的输入新鲜度处理）。
            try { m_Stream.WriteTimeout = 500; } catch (Exception) { }
        }

        private byte[] m_ReadBuffer = new byte[4096];   // 复用读取缓冲（避免每帧分配）
        /// <summary>[W-19] 单次 Poll 最多读几轮（防病态连接把主线程拖住）。</summary>
        private const int MaxReadsPerPoll = 32;
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
                NetStats.RecordSent(length);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServerSession] Send failed: " + e.Message);
            }
        }

        /// <summary>
        /// [W-19] 发送**已经序列化好**的帧（广播用）。
        /// 原来 `Broadcast` 对每个会话各调一次 `Send` → 同一份 payload 被**序列化 N 次**
        /// （4 人局每帧 5 次，30Hz 下 150 次/秒的纯浪费）。
        /// ⚠ 传入的数组是 `NetCodec` 的**共享静态缓冲**：必须在同一线程上紧接着写完所有会话，
        /// 期间不得再调 `NetCodec.Encode`（否则缓冲被覆盖）。广播路径正是这样用的（主线程顺序发送）。
        /// </summary>
        public void SendPreEncoded(byte[] frame, int length)
        {
            if (!IsAlive || m_Stream == null || frame == null || length <= 0)
            {
                return;
            }
            try
            {
                m_Stream.Write(frame, 0, length);
                NetStats.RecordSent(length);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetServerSession] SendPreEncoded failed: " + e.Message);
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
                // [W-19] 循环读到**读空为止**。
                // 原来每帧每会话只 `Read` 一次（最多 4096 B）：一旦某帧积压超过 4096 B（例如一次渲染
                // 停顿期间客户端/房主卡了 100ms，30Hz 下就有 3~4 帧排队），剩余数据要等**下一个渲染帧**
                // 才被派发 —— 表现上就是"输入帧成批到达"，正是 W-13 抖动缓冲要抹平的那种突发。
                // 上限 32 次/帧防止病态连接把主线程拖住。
                int reads = 0;
                while (reads < MaxReadsPerPoll && m_Stream.DataAvailable)
                {
                    int read = m_Stream.Read(m_ReadBuffer, 0, m_ReadBuffer.Length);
                    if (read <= 0)
                    {
                        Disconnect();
                        return;
                    }
                    NetStats.RecordReceived(read);
                    reads++;

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
                }

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
                    // W-26.3：损伤注入（延迟/抖动/丢包）。被接管时不直接派发（由 NetSim.Pump 按 FIFO 投递）。
                    if (!NetSim.Intercept(Id, message, Time.realtimeSinceStartup))
                    {
                        OnMessage?.Invoke(Id, message);
                    }
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
        private readonly List<NetServerSession> m_SessionList = new List<NetServerSession>();   // 复用遍历列表（Poll 用，避免每帧分配）
        private readonly List<NetServerSession> m_BroadcastList = new List<NetServerSession>(); // 复用广播列表（与 Poll 分离：
        // Poll 的 foreach 遍历 m_SessionList 处理消息时，消息回调里 Broadcast() 会清空/填充本列表；
        // 若两者共用同一列表，会修改正在被枚举的集合 → InvalidOperationException: Collection was modified。
        // 历史 bug：双实例联机时 HandleJoin/HandleReadyChange 内广播 → 崩溃。）
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
            // 复用独立广播列表（与 Poll 的 m_SessionList 分离 —— Poll foreach 处理消息期间可能回调本方法，
            // 若共用列表会修改正在枚举的集合导致 Collection was modified 崩溃）。
            m_BroadcastList.Clear();
            lock (m_SyncRoot)
            {
                m_BroadcastList.AddRange(m_Sessions.Values);
            }
            // 高频消息（输入帧 20Hz）不打日志，避免每帧 Debug.Log 严重掉帧
            if (message.Id != MsgId.InputFrame)
            {
                Debug.Log("[NetServer] Broadcast " + message.Id + " to " + m_BroadcastList.Count + " sessions");
            }
            // [W-19] 序列化一次、多次发送（原实现对每个会话各序列化一次）
            if (m_BroadcastList.Count == 0) { return; }
            byte[] frame = NetCodec.Encode(message, out int length);
            foreach (var session in m_BroadcastList)
            {
                session.SendPreEncoded(frame, length);   // [W-19] 只序列化一次，多个会话复用同一份字节
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
