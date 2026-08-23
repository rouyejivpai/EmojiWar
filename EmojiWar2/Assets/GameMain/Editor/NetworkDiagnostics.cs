//------------------------------------------------------------
// EmojiWar GameMain - 网络诊断工具（Editor）
// 菜单：EmojiWar/Diagnostics/Network Loopback Test
// 单进程内：启动 Host(7777) + Client 连接回环，验证消息收发。
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 网络回环测试：验证协议编解码 + TCP 传输。
    /// </summary>
    public static class NetworkDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetworkService s_Client = null;
        private static bool s_ClientReceived = false;
        private static bool s_HostReceived = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Network Loopback Test")]
        public static void RunLoopbackTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetDiag] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_ClientReceived = false;
            s_HostReceived = false;
            s_Timeout = 8f;

            // 创建 Host
            var hostGo = new GameObject("NetDiagHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_Host.OnClientMessage += (sessionId, msg) =>
            {
                Debug.Log(string.Format("[NetDiag] HOST 收到客户端消息: {0}", msg.Id));
                s_HostReceived = true;

                // 服务器回发
                if (msg.Id == MsgId.JoinRoom)
                {
                    var roomState = new S2CRoomState { RoomId = "ROOM-001", PlayerCount = 1 };
                    s_Host.SendToClient(sessionId, roomState);
                }
            };

            if (!s_Host.StartHost(7777))
            {
                Debug.LogError("[NetDiag] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // 创建 Client
            var clientGo = new GameObject("NetDiagClient");
            s_Client = clientGo.AddComponent<NetworkService>();
            s_Client.OnServerMessage += (msg) =>
            {
                Debug.Log(string.Format("[NetDiag] CLIENT 收到服务器消息: {0}", msg.Id));
                if (msg is S2CRoomState roomState)
                {
                    Debug.Log(string.Format("[NetDiag] RoomState: room={0} players={1}", roomState.RoomId, roomState.PlayerCount));
                    s_ClientReceived = true;
                }
            };

            s_Client.ConnectToServer("127.0.0.1", 7777);

            // 协程：等待连接后发消息，超时检查
            CoroutineRunner.Start(RunTestCoroutine());
        }

        private static IEnumerator RunTestCoroutine()
        {
            // 等待连接建立
            float wait = 3f;
            while (wait > 0f && (s_Client == null || !s_Client.IsConnected))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (s_Client == null || !s_Client.IsConnected)
            {
                Debug.LogError("[NetDiag] 客户端未能连接服务器");
                Cleanup();
                yield break;
            }
            Debug.Log("[NetDiag] 客户端已连接，发送 JoinRoom");

            // 客户端发加入房间
            s_Client.Send(new C2SJoinRoom { PlayerName = "TestPlayer" });

            // 等待回环完成
            while (s_Timeout > 0f && !(s_ClientReceived && s_HostReceived))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            if (s_ClientReceived && s_HostReceived)
            {
                Debug.Log("===== [NetDiag] 网络回环测试通过 ✅ =====");
            }
            else
            {
                Debug.LogWarning(string.Format("[NetDiag] 回环未完全完成 client={0} host={1}", s_ClientReceived, s_HostReceived));
            }

            Cleanup();
        }

        private static void Cleanup()
        {
            if (s_Host != null)
            {
                s_Host.Shutdown();
                Object.Destroy(s_Host.gameObject);
                s_Host = null;
            }
            if (s_Client != null)
            {
                s_Client.Shutdown();
                Object.Destroy(s_Client.gameObject);
                s_Client = null;
            }
        }

        /// <summary>
        /// 协程运行器（静态，供编辑器菜单启动协程）。
        /// </summary>
        private sealed class CoroutineRunner : MonoBehaviour
        {
            private static CoroutineRunner s_Instance = null;

            public static void Start(IEnumerator routine)
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("NetDiagRunner");
                    s_Instance = go.AddComponent<CoroutineRunner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
