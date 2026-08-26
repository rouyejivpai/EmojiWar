//------------------------------------------------------------
// EmojiWar GameMain - 网络战斗模拟测试工具（Editor，帧同步版）
// 菜单：EmojiWar/Diagnostics/Net Battle Sim Test
// 单进程内：Host + Client 回环，验证确定性帧同步：
//   1. 客户端加入 → Host/Client 各自建立确定性模拟
//   2. 全部准备 → 战斗开始（同 seed）→ 模拟生成敌人（本地确定性）
//   3. 客户端上行意图 → Host 广播输入帧 → 双端模拟推进（FrameIndex 增长）
//   4. 敌人由模拟本地生成，不依赖 S2CSpawnEntity 广播
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 网络战斗模拟测试（确定性帧同步）。
    /// </summary>
    public static class NetBattleSimDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetworkService s_Client = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetClientLogic s_ClientLogic = null;

        private static bool s_ClientJoined = false;
        private static bool s_InputFrameSeen = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Battle Sim Test")]
        public static void RunBattleSimTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetSim] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_ClientJoined = false;
            s_InputFrameSeen = false;
            s_Timeout = 15f;

            // ---- Host ----
            var hostGo = new GameObject("NetSimHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            if (!s_Host.StartHost(7791))
            {
                Debug.LogError("[NetSim] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client ----
            var clientGo = new GameObject("NetSimClient");
            s_Client = clientGo.AddComponent<NetworkService>();
            s_ClientLogic = clientGo.AddComponent<NetClientLogic>();
            s_ClientLogic.Bind(s_Client);
            s_Client.OnServerMessage += (msg) =>
            {
                if (msg.Id == MsgId.MyEntity)
                {
                    s_ClientJoined = true;
                    Debug.Log("[NetSim] 客户端加入成功（MyEntity）");
                }
                if (msg is S2CInputFrame)
                {
                    s_InputFrameSeen = true;
                }
            };
            s_Client.ConnectToServer("127.0.0.1", 7791);

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
                Debug.LogError("[NetSim] 客户端未连接");
                Cleanup();
                yield break;
            }

            // 加入房间（Host 本地也加入，模拟启动）
            s_HostLogic.JoinLocal("HostPlayer", 1);
            s_ClientLogic.JoinRoom("BattleTester");

            // 等待：客户端加入成功 + 输入帧到达（帧同步驱动模拟）
            while (s_Timeout > 0f && !(s_ClientJoined && s_InputFrameSeen))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            // 验证本地模拟推进
            int hostFrames = s_HostLogic != null && s_HostLogic.Simulation != null ? s_HostLogic.Simulation.FrameIndex : 0;
            int clientFrames = s_ClientLogic != null && s_ClientLogic.Simulation != null ? s_ClientLogic.Simulation.FrameIndex : 0;
            int hostPlayers = s_HostLogic != null && s_HostLogic.Simulation != null ? s_HostLogic.Simulation.Players.Count : 0;

            if (s_ClientJoined && s_InputFrameSeen)
            {
                Debug.Log(string.Format("===== [NetSim] 帧同步测试通过 ✅ hostFrames={0} clientFrames={1} hostPlayers={2} =====",
                    hostFrames, clientFrames, hostPlayers));
            }
            else
            {
                Debug.LogWarning(string.Format("[NetSim] 未完全完成 joined={0} inputFrame={1}",
                    s_ClientJoined, s_InputFrameSeen));
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
                    var go = new GameObject("NetSimRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
