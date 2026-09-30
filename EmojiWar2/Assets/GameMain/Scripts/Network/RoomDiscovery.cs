//------------------------------------------------------------
// EmojiWar GameMain - 局域网房间发现（UDP，请求-应答式）
// 需求：主界面 → 多人游戏 → 加入游戏 页显示"正在房间的玩家"列表，可点击加入。
// 现有网络层只有房间内 TCP（客户端直连房主 IP），无房间发现机制，本文件补齐：
//   - 房主（Host 且处于房间阶段）监听固定 UDP 端口；收到 "EWSCAN" 扫描请求
//     即单播应答 "EWROOM|房主名|房间人数|游戏端口|房间Id"。
//   - 扫描方（加入游戏页）向广播地址 + 本机回环发送 "EWSCAN"，随机端口收应答
//     收集 (房主名, 人数, ip, 端口)，供列表展示/点击加入。
// 请求-应答而非房主持续广播：多实例各自监听固定端口的冲突最小化
// （应答目标端口为扫描方随机端口，互不冲突）。
// 全部在主线程轮询（Unity 友好，无后台线程）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 一个被发现的房间。
    /// </summary>
    public sealed class DiscoveredRoom
    {
        public string HostName;   // 房主名
        public int PlayerCount;   // 房间人数（含房主）
        public string Ip;         // 房主 IP（应答来源）
        public int Port;          // 游戏 TCP 端口

        public override string ToString()
        {
            return string.Format("{0} 的房间（{1} 人）", HostName, PlayerCount);
        }
    }

    /// <summary>
    /// 房间发现：房主应答器 + 扫描器。
    /// 用法：
    ///  房主侧：每帧调 RoomDiscovery.TickAdvertiser(hostName, playerCount, gamePort, isInRoom)
    ///  扫描侧：RoomDiscovery.BeginScan() 后每帧 TickScanner()，结果在 Rooms 中。
    /// </summary>
    public static class RoomDiscovery
    {
        /// <summary>发现服务 UDP 端口（固定；游戏 TCP 7777 之外的独立端口）。</summary>
        public const int DiscoverPort = 47779;
        private const string ScanToken = "EWSCAN";
        private const string RoomToken = "EWROOM";

        // ---- 房主应答器 ----
        private static UdpClient s_Listener = null;
        private static bool s_ListenerBound = false;
        private static bool s_AdvertiserActive = false;

        // ---- 扫描器 ----
        private static UdpClient s_Scanner = null;
        private static float s_ScanTimer = 0f;
        private const float ScanWindow = 1.0f;   // 收集应答窗口（秒）
        private static bool s_Scanning = false;
        private static readonly List<DiscoveredRoom> s_Rooms = new List<DiscoveredRoom>();

        /// <summary>最近一次扫描收集到的房间列表（只读）。</summary>
        public static IReadOnlyList<DiscoveredRoom> Rooms
        {
            get { return s_Rooms; }
        }

        public static int RoomCount { get { return s_Rooms.Count; } }

        /// <summary>清理全部资源（切离线/退出时）。</summary>
        public static void Shutdown()
        {
            CloseListener();
            CloseScanner();
        }

        // ==================== 房主应答器 ====================

        /// <summary>
        /// 房主侧 tick：房间阶段时确保监听，非房间阶段关闭监听。
        /// 参数随帧更新（房主名/人数可能变化）。
        /// </summary>
        public static void TickAdvertiser(bool inRoom, string hostName, int playerCount, int gamePort)
        {
            if (!inRoom)
            {
                // 离开房间：停监听
                CloseListener();
                return;
            }

            // 需要监听时首次绑定
            if (!s_ListenerBound)
            {
                TryBindListener();
            }

            // 轮询收到的扫描请求并应答
            PollListener(hostName, playerCount, gamePort);
        }

        private static void TryBindListener()
        {
            try
            {
                s_Listener = new UdpClient();
                s_Listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                s_Listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoverPort));
                s_Listener.Client.ReceiveTimeout = 0;
                s_ListenerBound = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RoomDiscovery] 发现监听绑定失败: " + e.Message);
                s_Listener = null;
                s_ListenerBound = false;
            }
        }

        private static void PollListener(string hostName, int playerCount, int gamePort)
        {
            if (s_Listener == null || !s_ListenerBound)
            {
                return;
            }

            try
            {
                // 非阻塞读取全部可用请求
                while (s_Listener.Available > 0)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = s_Listener.Receive(ref from);
                    string msg = Encoding.UTF8.GetString(data);
                    if (string.IsNullOrEmpty(msg))
                    {
                        continue;
                    }
                    if (msg.StartsWith(ScanToken, System.StringComparison.Ordinal))
                    {
                        // 回单播应答
                        string payload = string.Format("{0}|{1}|{2}|{3}|{4}",
                            RoomToken, hostName ?? "房主", playerCount, gamePort,
                            "ROOM-001");
                        byte[] reply = Encoding.UTF8.GetBytes(payload);
                        s_Listener.Send(reply, reply.Length, from);
                    }
                }
            }
            catch (System.Exception)
            {
                // 无更多数据/非阻塞轮询到期属正常；严重错误重建监听
            }
        }

        private static void CloseListener()
        {
            if (s_Listener != null)
            {
                try
                {
                    s_Listener.Close();
                }
                catch { }
                s_Listener = null;
            }
            s_ListenerBound = false;
        }

        // ==================== 扫描器 ====================

        /// <summary>开始一次房间扫描（持续 ScanWindow 秒收集应答）。</summary>
        public static void BeginScan()
        {
            // 上一轮未结束则丢弃
            if (s_Scanning)
            {
                return;
            }

            try
            {
                CloseScanner();
                s_Scanner = new UdpClient();
                // 随机本地端口收应答（多实例互不冲突）
                s_Scanner.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                s_Scanner.Client.ReceiveTimeout = 0;
                // 允许向广播地址发送（255.255.255.255），否则 Send 抛 SocketException
                s_Scanner.Client.EnableBroadcast = true;

                // 向广播地址与本机回环发扫描请求（回环保证本机多实例可见）
                byte[] scan = Encoding.UTF8.GetBytes(ScanToken);
                var targets = new[]
                {
                    new IPEndPoint(IPAddress.Broadcast, DiscoverPort),
                    new IPEndPoint(IPAddress.Loopback, DiscoverPort),
                };
                foreach (var t in targets)
                {
                    try
                    {
                        s_Scanner.Send(scan, scan.Length, t);
                    }
                    catch { }
                }

                s_Rooms.Clear();
                s_Scanning = true;
                s_ScanTimer = ScanWindow;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RoomDiscovery] 扫描启动失败: " + e.Message);
                CloseScanner();
                s_Scanning = false;
            }
        }

        /// <summary>扫描 tick：窗口内收集应答；窗口结束则停。</summary>
        public static void TickScanner()
        {
            if (!s_Scanning || s_Scanner == null)
            {
                return;
            }

            s_ScanTimer -= Time.unscaledDeltaTime;

            // 收集可用应答
            try
            {
                while (s_Scanner.Available > 0)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = s_Scanner.Receive(ref from);
                    string msg = Encoding.UTF8.GetString(data);
                    DiscoveredRoom room = ParseRoom(msg, from);
                    if (room != null)
                    {
                        // 去重（同 ip:port 只留最新）
                        for (int i = s_Rooms.Count - 1; i >= 0; i--)
                        {
                            if (s_Rooms[i].Ip == room.Ip && s_Rooms[i].Port == room.Port)
                            {
                                s_Rooms.RemoveAt(i);
                            }
                        }
                        s_Rooms.Add(room);
                    }
                }
            }
            catch (System.Exception)
            {
            }

            if (s_ScanTimer <= 0f)
            {
                CloseScanner();
                s_Scanning = false;
            }
        }

        /// <summary>是否正在扫描（供 UI 显示"扫描中..."）。</summary>
        public static bool IsScanning
        {
            get { return s_Scanning; }
        }

        private static DiscoveredRoom ParseRoom(string msg, IPEndPoint from)
        {
            if (string.IsNullOrEmpty(msg) || from == null)
            {
                return null;
            }
            string[] parts = msg.Split('|');
            if (parts.Length < 5 || parts[0] != RoomToken)
            {
                return null;
            }
            var room = new DiscoveredRoom
            {
                HostName = parts[1],
                Ip = from.Address.ToString(),
            };
            int.TryParse(parts[2], out room.PlayerCount);
            int.TryParse(parts[3], out room.Port);
            return room;
        }

        private static void CloseScanner()
        {
            if (s_Scanner != null)
            {
                try
                {
                    s_Scanner.Close();
                }
                catch { }
                s_Scanner = null;
            }
        }
    }
}
