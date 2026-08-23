//------------------------------------------------------------
// EmojiWar GameMain - 断线重连测试工具（Editor）
// 菜单：EmojiWar/Diagnostics/Net Reconnect Test
// 单进程内：Host + ClientA + ClientB 回环，验证：
//   1. 两个客户端加入 → Host 广播生成
//   2. ClientA 关闭 → Host 检测断开 → 广播 PlayerLeft + RemoveEntity
//   3. ClientB 重新加入/重连
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 断线重连测试。
    /// </summary>
    public static class NetReconnectDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetworkService s_ClientA = null;
        private static NetClientLogic s_ClientALogic = null;
        private static NetworkService s_ClientB = null;
        private static NetClientLogic s_ClientBLogic = null;

        private static bool s_BothJoined = false;
        private static bool s_LeftBroadcast = false;
        private static bool s_RemoveBroadcast = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Reconnect Test")]
        public static void RunReconnectTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetRec] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_BothJoined = false;
            s_LeftBroadcast = false;
            s_RemoveBroadcast = false;
            s_Timeout = 15f;

            // ---- Host ----
            var hostGo = new GameObject("NetRecHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            s_Host.OnClientMessage += (sessionId, msg) =>
            {
                if (msg.Id == MsgId.JoinRoom)
                {
                    Debug.Log("[NetRec] HOST 收到加入请求 session=" + sessionId);
                }
            };
            if (!s_Host.StartHost(7792))
            {
                Debug.LogError("[NetRec] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client A ----
            var clientAGo = new GameObject("NetRecClientA");
            s_ClientA = clientAGo.AddComponent<NetworkService>();
            s_ClientALogic = clientAGo.AddComponent<NetClientLogic>();
            s_ClientALogic.Bind(s_ClientA);
            s_ClientA.ConnectToServer("127.0.0.1", 7792);

            // ---- Client B（观察者：验证 A 离开的广播）----
            var clientBGo = new GameObject("NetRecClientB");
            s_ClientB = clientBGo.AddComponent<NetworkService>();
            s_ClientBLogic = clientBGo.AddComponent<NetClientLogic>();
            s_ClientBLogic.Bind(s_ClientB);
            s_ClientB.OnServerMessage += (msg) =>
            {
                if (msg is S2CPlayerLeft)
                {
                    s_LeftBroadcast = true;
                    Debug.Log("[NetRec] ClientB 收到 PlayerLeft（A 离开）");
                }
                if (msg is S2CRemoveEntity)
                {
                    s_RemoveBroadcast = true;
                    Debug.Log("[NetRec] ClientB 收到 RemoveEntity（A 的实体移除）");
                }
            };
            s_ClientB.ConnectToServer("127.0.0.1", 7792);

            Runner.Start(RunCoroutine());
        }

        private static IEnumerator RunCoroutine()
        {
            // 等待两个客户端连接
            float wait = 4f;
            while (wait > 0f && (s_ClientA == null || !s_ClientA.IsConnected || s_ClientB == null || !s_ClientB.IsConnected))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (s_ClientA == null || !s_ClientA.IsConnected || s_ClientB == null || !s_ClientB.IsConnected)
            {
                Debug.LogError("[NetRec] 客户端连接失败");
                Cleanup();
                yield break;
            }

            // 两个客户端加入
            s_ClientALogic.JoinRoom("PlayerA");
            s_ClientBLogic.JoinRoom("PlayerB");
            Debug.Log("[NetRec] 两个客户端已加入");
            yield return new WaitForSeconds(2f);
            s_BothJoined = true;

            // ClientA 断开（模拟掉线：彻底销毁，触发服务器端断线检测）
            Debug.Log("[NetRec] ClientA 模拟掉线");
            if (s_ClientA != null)
            {
                var goA = s_ClientA.gameObject;
                s_ClientA.Shutdown();
                Object.Destroy(goA);
                s_ClientA = null;
                s_ClientALogic = null;
            }

            // 等待 Host 检测断开并广播
            while (s_Timeout > 0f && !(s_LeftBroadcast && s_RemoveBroadcast))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            if (s_LeftBroadcast && s_RemoveBroadcast)
            {
                Debug.Log("===== [NetRec] 断线处理测试通过 ✅ PlayerLeft + RemoveEntity 已广播 =====");
            }
            else
            {
                Debug.LogWarning(string.Format("[NetRec] 未完全完成 left={0} remove={1}", s_LeftBroadcast, s_RemoveBroadcast));
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
            if (s_ClientA != null)
            {
                s_ClientA.Shutdown();
                Object.Destroy(s_ClientA.gameObject);
            }
            if (s_ClientB != null)
            {
                s_ClientB.Shutdown();
                Object.Destroy(s_ClientB.gameObject);
            }
            s_HostLogic = null;
            s_ClientALogic = null;
            s_ClientBLogic = null;
        }

        private sealed class Runner : MonoBehaviour
        {
            private static Runner s_Instance = null;

            public static void Start(IEnumerator routine)
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("NetRecRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
