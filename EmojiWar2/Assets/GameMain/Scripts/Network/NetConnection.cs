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
                m_Client.BeginConnect(host, port, OnConnectCallback, null);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetConnection] Connect failed: " + e.Message);
            }
        }

        private void OnConnectCallback(IAsyncResult ar)
        {
            try
            {
                m_Client.EndConnect(ar);
                m_Stream = m_Client.GetStream();
                Debug.Log("[NetConnection] Connected to " + m_Client.Client.RemoteEndPoint);
                WriteProbe("[net] TCP 连接建立成功: " + m_Client.Client.RemoteEndPoint);
            }
            catch (Exception e)
            {
                Debug.LogError("[NetConnection] EndConnect failed: " + e.Message);
                WriteProbe("[net] TCP 连接失败: " + e.Message);
                m_Client = null;
                OnDisconnected?.Invoke();
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
                byte[] frame = NetCodec.Encode(message);
                m_Stream.Write(frame, 0, frame.Length);
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
                byte[] buffer = new byte[4096];
                int read = m_Stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    HandleDisconnect("read<=0");
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
                Debug.LogError("[NetConnection] Poll failed: " + e.Message);
                HandleDisconnect("poll-exception");
            }
        }

        /// <summary>
        /// 拆帧并分发消息。
        /// </summary>
        private void ProcessBuffer()
        {
            while (m_ReceiveBuffer.Count >= NetCodec.HeaderLength)
            {
                int length = m_ReceiveBuffer[2] | (m_ReceiveBuffer[3] << 8);
                int total = NetCodec.HeaderLength + length;

                if (m_ReceiveBuffer.Count < total)
                {
                    break; // 等待完整帧
                }

                byte[] frame = m_ReceiveBuffer.GetRange(0, total).ToArray();
                m_ReceiveBuffer.RemoveRange(0, total);

                var message = NetCodec.Decode(frame, 0, total);
                if (message != null)
                {
                    OnMessage?.Invoke(message);
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
