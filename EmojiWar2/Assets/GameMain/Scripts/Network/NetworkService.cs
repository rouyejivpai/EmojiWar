//------------------------------------------------------------
// EmojiWar GameMain - 网络服务组件
// 业务网络门面：支持"服务器模式"（Host）与"客户端模式"。
// 本组件供多人联机使用；单机模式可直接走本地逻辑。
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 网络模式。
    /// </summary>
    public enum NetMode
    {
        Offline = 0,    // 单机
        Host = 1,       // 作为服务器（Host 权威）
        Client = 2,     // 连接服务器
    }

    /// <summary>
    /// 网络服务组件：管理连接/监听与消息分发。
    /// </summary>
    public class NetworkService : MonoBehaviour
    {
        public const int DefaultPort = 7777;

        private NetServer m_Server = null;
        private NetConnection m_Connection = null;
        private NetMode m_Mode = NetMode.Offline;

        public NetMode Mode { get { return m_Mode; } }
        public bool IsConnected { get { return m_Connection != null && m_Connection.IsReady; } }
        public bool IsHosting { get { return m_Server != null && m_Server.IsRunning; } }
        public int ConnectedClients { get { return m_Server != null ? m_Server.SessionCount : 0; } }

        /// <summary>收到服务器消息事件。</summary>
        public event Action<NetMessage> OnServerMessage;

        /// <summary>收到客户端消息事件（Host 模式）。</summary>
        public event Action<int, NetMessage> OnClientMessage;

        /// <summary>客户端断开事件（Host 模式，参数 sessionId）。</summary>
        public event Action<int> OnClientDisconnected;

        /// <summary>连接状态变化事件。</summary>
        public event Action<NetMode> OnModeChanged;

        private void Update()
        {
            // 轮询网络（服务器与客户端）
            if (m_Server != null)
            {
                if (Time.frameCount % 90 == 0)
                {
                    Debug.Log("[NetworkService] Update tick, server=" + (m_Server != null) + " sessions=" + (m_Server != null ? m_Server.SessionCount : -1));
                }
                m_Server.Poll();
            }
            if (m_Connection != null)
            {
                m_Connection.Poll();
            }
        }
        /// <summary>
        /// 启动为服务器（Host）。
        /// </summary>
        public bool StartHost(int port = DefaultPort)
        {
            WriteProbe("[net-host] StartHost called port=" + port);
            Shutdown();

            m_Server = new NetServer();
            m_Server.OnClientConnected += OnClientConnected;
            m_Server.OnMessage += OnClientMessageHandler;
            m_Server.OnClientDisconnected += OnInternalClientDisconnected;

            if (!m_Server.Start(port))
            {
                m_Server = null;
                WriteProbe("[net-host] StartHost FAILED port=" + port);
                return false;
            }

            m_Mode = NetMode.Host;
            OnModeChanged?.Invoke(m_Mode);
            Debug.Log("[NetworkService] Host started on port " + port);
            WriteProbe("[net-host] StartHost OK, mode=Host port=" + port);
            return true;
        }

        /// <summary>
        /// 连接服务器。
        /// </summary>
        public bool ConnectToServer(string host, int port = DefaultPort)
        {
            Shutdown();

            m_Connection = new NetConnection();
            m_Connection.OnMessage += OnServerMessageHandler;
            m_Connection.OnDisconnected += OnConnectionDisconnected;
            m_Connection.Connect(host, port);

            m_Mode = NetMode.Client;
            OnModeChanged?.Invoke(m_Mode);
            return true;
        }

        /// <summary>
        /// 发送消息（客户端→服务器，或 Host 广播到所有客户端）。
        /// </summary>
        public void Send(NetMessage message)
        {
            if (m_Mode == NetMode.Client && m_Connection != null)
            {
                m_Connection.Send(message);
            }
            else if (m_Mode == NetMode.Host && m_Server != null)
            {
                m_Server.Broadcast(message);
            }
        }

        /// <summary>
        /// Host 模式：向指定客户端发送。
        /// </summary>
        public void SendToClient(int sessionId, NetMessage message)
        {
            if (m_Mode == NetMode.Host && m_Server != null)
            {
                m_Server.SendTo(sessionId, message);
            }
        }

        /// <summary>
        /// Host 模式：向所有客户端广播。
        /// </summary>
        public void BroadcastToClients(NetMessage message)
        {
            if (m_Mode == NetMode.Host && m_Server != null)
            {
                m_Server.Broadcast(message);
            }
        }

        private void OnClientConnected(int sessionId)
        {
            Debug.Log("[NetworkService] Client connected: " + sessionId);
        }

        private void OnClientMessageHandler(int sessionId, NetMessage message)
        {
            OnClientMessage?.Invoke(sessionId, message);
        }

        private void OnServerMessageHandler(NetMessage message)
        {
            OnServerMessage?.Invoke(message);
        }

        private void OnInternalClientDisconnected(int sessionId)
        {
            Debug.Log("[NetworkService] Client disconnected: " + sessionId);
            OnClientDisconnected?.Invoke(sessionId);
        }

        private void OnConnectionDisconnected()
        {
            Debug.Log("[NetworkService] Disconnected from server.");
            m_Mode = NetMode.Offline;
            OnModeChanged?.Invoke(m_Mode);
        }

        /// <summary>
        /// 关闭所有网络。
        /// </summary>
        public void Shutdown()
        {
            WriteProbe("[net-host] Shutdown (server=" + (m_Server != null && m_Server.IsRunning) +
                " conn=" + (m_Connection != null && m_Connection.IsConnected) + ")");
            if (m_Server != null)
            {
                m_Server.Stop();
                m_Server = null;
            }

            if (m_Connection != null)
            {
                m_Connection.Close();
                m_Connection = null;
            }

            m_Mode = NetMode.Offline;
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

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
