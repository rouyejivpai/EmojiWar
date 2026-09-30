//------------------------------------------------------------
// EmojiWar GameMain - TCP 连接（客户端侧）
// 封装 TcpClient：连接、收发、拆帧。
// 主线程轮询驱动（Unity 友好）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// TCP 客户端连接。
    /// </summary>
    public class NetConnection
    {
        private TcpClient m_Client = null;
        private NetworkStream m_Stream = null;
        private readonly List<byte> m_ReceiveBuffer = new List<byte>();
        private byte[] m_ReadBuffer = new byte[4096];   // 复用读取缓冲（避免每帧分配）
        /// <summary>[W-19] 单次 Poll 最多读几轮（防对端猛灌数据把主线程拖住）。</summary>
        private const int MaxReadsPerPoll = 32;
        private int m_Consumed = 0;                     // 已拆帧消费的字节数（游标，避免每次 RemoveRange O(n) 前移）

        public bool IsConnected
        {
            get { return m_Client != null && m_Client.Connected; }
        }

        /// <summary>
        /// 是否真正可收发（流已就绪）。TcpClient.Connected 在握手完成即 true，
        /// 但 m_Stream 需等异步回调赋值 —— 在此之前 Send 会丢弃消息（JoinRoom 丢失根因）。
        /// </summary>
        public bool IsReady
        {
            get { return m_Client != null && m_Client.Connected && m_Stream != null; }
        }

        /// <summary>收到消息事件。</summary>
        public event Action<NetMessage> OnMessage;

        /// <summary>连接断开事件。</summary>
        public event Action OnDisconnected;

        /// <summary>
        /// 异步连接服务器。
        /// </summary>
        public void Connect(string host, int port)
        {
            try
            {
                // 防御：host 可能带端口（如 "127.0.0.1:7777" 被用户直接填入 IP 框），拆出端口避免 DNS 解析失败
                int colon = host.LastIndexOf(':');
                if (colon > 0)
                {
                    string maybePort = host.Substring(colon + 1);
                    int parsedPort;
                    if (int.TryParse(maybePort, out parsedPort) && parsedPort > 0 && parsedPort < 65536)
                    {
                        port = parsedPort;
                        host = host.Substring(0, colon);
                        Debug.Log("[NetConnection] 从 host 解析端口: host=" + host + " port=" + port);
                    }
                }

                m_Client = new TcpClient();
                // NoDelay：禁用 Nagle 算法，避免小包（输入帧）延迟堆积
                m_Client.NoDelay = true;
                m_Client.BeginConnect(host, port, OnConnectCallback, null);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetConnection] Connect failed: " + e.Message);
            }
        }

        private void OnConnectCallback(IAsyncResult ar)
        {
            // ★ [W-19] 这个回调运行在**线程池线程**上，绝不能在这里碰 Unity API / 触发事件：
            //   原来它直接 `Debug.Log` + 探针写文件 + `OnDisconnected?.Invoke()` ——
            //   订阅方（NetworkService → 流程/UI）会在**非主线程**执行 Unity API，属于未定义行为。
            //   现在只做"记录结果 + 置标志"，真正的收尾放到主线程 `Poll` 里做。
            try
            {
                m_Client.EndConnect(ar);
                m_Stream = m_Client.GetStream();
                try { m_Stream.WriteTimeout = 500; } catch (Exception) { }   // [W-19] 有界写超时
                m_PendingConnected = true;      // volatile：主线程可见
            }
            catch (Exception)
            {
                m_PendingConnectFailed = true;  // 原因在主线程里统一记录（避免跨线程日志）
            }
        }

        // ---- [W-19] 连接结果的主线程收尾（volatile：跨线程可见性）----
        private volatile bool m_PendingConnected;
        private volatile bool m_PendingConnectFailed;

        /// <summary>[W-19] 主线程处理"连接已完成/失败"：日志、探针、事件都在主线程发。</summary>
        private void FlushConnectResult()
        {
            if (m_PendingConnected)
            {
                m_PendingConnected = false;
                string ep = "(unknown)";
                try { ep = m_Client.Client.RemoteEndPoint != null ? m_Client.Client.RemoteEndPoint.ToString() : "(null)"; }
                catch (Exception) { }
                Debug.Log("[NetConnection] Connected to " + ep);
                WriteProbe("[net] TCP 连接建立成功: " + ep);
            }
            else if (m_PendingConnectFailed)
            {
                m_PendingConnectFailed = false;
                Debug.LogError("[NetConnection] EndConnect failed（连接被拒绝或超时）");
                WriteProbe("[net] TCP 连接失败");
                m_Client = null;
                OnDisconnected?.Invoke();   // ★ 现在在**主线程**上派发
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
        /// 发送消息。
        /// </summary>
        public void Send(NetMessage message)
        {
            if (!IsConnected || m_Stream == null || message == null)
            {
                return;
            }

            try
            {
                byte[] frame = NetCodec.Encode(message, out int length);
                m_Stream.Write(frame, 0, length);
                NetStats.RecordSent(length);   // [W-19] 带宽可观测
            }
            catch (Exception e)
            {
                Debug.LogError("[NetConnection] Send failed: " + e.Message);
            }
        }

        /// <summary>
        /// 每帧轮询：读取数据并拆帧分发。
        /// </summary>
        public void Poll()
        {
            // [W-19] 先收尾连接结果（主线程），再判是否已连接 —— 否则"刚连接成功"的那一帧会被这里挡掉
            FlushConnectResult();

            if (!IsConnected || m_Stream == null)
            {
                return;
            }

            // 主动检测对端关闭（FIN）：可读但无数据 = 连接关闭
            try
            {
                if (m_Client.Client.Poll(0, SelectMode.SelectRead) && m_Client.Available == 0)
                {
                    HandleDisconnect("peer-closed");
                    return;
                }
            }
            catch
            {
                HandleDisconnect("poll-error");
                return;
            }

            if (!m_Stream.DataAvailable)
            {
                return;
            }

            try
            {
                // [W-19] 循环读到**读空为止**（原来每渲染帧只读一次 4096 B）：
                //   一次 TCP Read 最多 4096 B，而 30Hz 输入帧 100 B/帧 → 只要有一帧积压超过 4096 B，
                //   剩下的就要等下一个渲染帧才派发 → 帧成批到达（正是 W-13 要抹平的突发）。
                int reads = 0;
                while (reads < MaxReadsPerPoll && m_Stream.DataAvailable)
                {
                    int read = m_Stream.Read(m_ReadBuffer, 0, m_ReadBuffer.Length);
                    if (read <= 0)
                    {
                        HandleDisconnect("read<=0");
                        return;
                    }
                    NetStats.RecordReceived(read);   // [W-19]
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
                }   // [W-19] while (reads < MaxReadsPerPoll && m_Stream.DataAvailable)

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
                Debug.LogError("[NetConnection] Poll failed: " + e.Message);
                HandleDisconnect("poll-exception");
            }
        }

        /// <summary>
        /// 拆帧并分发消息（用游标 m_Consumed 读取，避免每次 RemoveRange O(n) 前移）。
        /// </summary>
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
                    break; // 等待完整帧
                }

                // 免拷贝解码（复用静态缓冲，避免 GetRange().ToArray() 每帧分配）
                var message = NetCodec.DecodeFromList(m_ReceiveBuffer, offset, total);
                m_Consumed += total;

                if (message != null)
                {
                    // W-26.3：损伤注入（客户端侧 sessionId = -1）。
                    // 被接管时不直接派发（由 NetSim.Pump 按 FIFO 投递）。保持 FIFO 是关键 ——
                    // 顺序打乱会制造当前 TCP 架构下不可能发生的故障（假的不同步）。
                    if (!NetSim.Intercept(-1, message, Time.realtimeSinceStartup))
                    {
                        OnMessage?.Invoke(message);
                    }
                }
            }
        }

        private void HandleDisconnect(string reason = "")
        {
            Debug.Log("[NetConnection] Disconnecting. reason=" + reason);
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
                    m_Client = null;
                }
            }
            catch
            {
            }

            m_ReceiveBuffer.Clear();
            OnDisconnected?.Invoke();
        }

        /// <summary>
        /// 关闭连接。
        /// </summary>
        public void Close()
        {
            HandleDisconnect();
        }
    }
}
