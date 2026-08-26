//------------------------------------------------------------
// EmojiWar GameMain - 网络帧同步诊断工具（Editor）
// 菜单：EmojiWar/Diagnostics/Net Sync Loopback Test
// 单进程内：Host + Client 回环，验证确定性帧同步：
//   Client 加入 → Host/Client 建立同一模拟（同 seed）→ 输入帧广播 → 双端推进
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 网络帧同步回环测试。
    /// </summary>
    public static class NetworkSyncDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetworkService s_Client = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetClientLogic s_ClientLogic = null;

        private static bool s_Joined = false;
        private static bool s_InputFrame = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Sync Loopback Test")]
        public static void RunSyncTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetSync] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_Joined = false;
            s_InputFrame = false;
            s_Timeout = 10f;

            // ---- Host ----
            var hostGo = new GameObject("NetSyncHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            if (!s_Host.StartHost(7790))
            {
                Debug.LogError("[NetSync] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client ----
            var clientGo = new GameObject("NetSyncClient");
            s_Client = clientGo.AddComponent<NetworkService>();
            s_ClientLogic = clientGo.AddComponent<NetClientLogic>();
            s_ClientLogic.Bind(s_Client);
            s_Client.OnServerMessage += (msg) =>
            {
                if (msg.Id == MsgId.MyEntity)
                {
                    s_Joined = true;
                }
                if (msg is S2CInputFrame)
                {
                    s_InputFrame = true;
                }
            };
            s_Client.ConnectToServer("127.0.0.1", 7790);

            Runner.Start(RunCoroutine());
        }

        private static IEnumerator RunCoroutine()
        {
            // 等待连接
            float wait = 3f;
            while (wait > 0f && (s_Client == null || !s_Client.IsConnected))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (s_Client == null || !s_Client.IsConnected)
            {
                Debug.LogError("[NetSync] 客户端未连接");
                Cleanup();
                yield break;
            }

            Debug.Log(string.Format("[NetSync] 客户端已连接。host hosting={0} sessions={1}", s_Host.IsHosting, s_Host.ConnectedClients));

            // 加入房间
            s_HostLogic.JoinLocal("HostPlayer", 1);
            s_ClientLogic.JoinRoom("SyncTester");

            // 等待加入 + 输入帧（帧同步活跃）
            while (s_Timeout > 0f && !(s_Joined && s_InputFrame))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            // 额外等待几帧，确认模拟持续推进（帧号增长）
            int frameA = s_ClientLogic != null && s_ClientLogic.Simulation != null ? s_ClientLogic.Simulation.FrameIndex : 0;
            yield return new WaitForSeconds(1f);
            int frameB = s_ClientLogic != null && s_ClientLogic.Simulation != null ? s_ClientLogic.Simulation.FrameIndex : 0;
            bool framesAdvancing = frameB > frameA;

            Debug.Log(string.Format("[NetSync] 测试结束。joined={0} inputFrame={1} frames {2}->{3} hostSessions={4}",
                s_Joined, s_InputFrame, frameA, frameB, s_Host.ConnectedClients));

            if (s_Joined && s_InputFrame && framesAdvancing)
            {
                Debug.Log(string.Format("===== [NetSync] 帧同步测试通过 ✅ 模拟持续推进 ({0} -> {1}) =====",
                    frameA, frameB));
            }
            else
            {
                Debug.LogWarning(string.Format("[NetSync] 同步未完全完成 joined={0} inputFrame={1} advancing={2}",
                    s_Joined, s_InputFrame, framesAdvancing));
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
            s_HostLogic = null;
            s_ClientLogic = null;
        }

        private sealed class Runner : MonoBehaviour
        {
            private static Runner s_Instance = null;

            public static void Start(IEnumerator routine)
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("NetSyncRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}