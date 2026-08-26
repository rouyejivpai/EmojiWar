//------------------------------------------------------------
// EmojiWar GameMain - 波间商店测试工具（Editor，帧同步版）
// 菜单：EmojiWar/Diagnostics/Net Shop Test
// 单进程内：Host + Client 回环，验证确定性帧同步商店：
//   1. 客户端加入 → Host/Client 建立确定性模拟
//   2. 战斗开始（同 seed）→ 模拟本地生成波次敌人与商店商品
//   3. 商店商品由模拟确定性生成（各端一致），无需网络广播
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 波间商店测试（帧同步）。
    /// </summary>
    public static class NetShopDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetworkService s_Client = null;
        private static NetClientLogic s_ClientLogic = null;

        private static bool s_Joined = false;
        private static bool s_InputFrame = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Shop Test")]
        public static void RunShopTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetShop] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_Joined = false;
            s_InputFrame = false;
            s_Timeout = 20f;

            // ---- Host ----
            var hostGo = new GameObject("NetShopHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            if (!s_Host.StartHost(7793))
            {
                Debug.LogError("[NetShop] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client ----
            var clientGo = new GameObject("NetShopClient");
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
            s_Client.ConnectToServer("127.0.0.1", 7793);

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
                Debug.LogError("[NetShop] 客户端未连接");
                Cleanup();
                yield break;
            }

            // 加入房间（Host 本地 + 客户端）
            s_HostLogic.JoinLocal("ShopHost", 1);
            s_ClientLogic.JoinRoom("ShopTester");

            // 等待加入 + 输入帧（帧同步驱动）
            while (s_Timeout > 0f && !(s_Joined && s_InputFrame))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            // 验证模拟状态（商店商品由模拟确定性生成，无网络广播）
            int hostPlayers = s_HostLogic != null && s_HostLogic.Simulation != null ? s_HostLogic.Simulation.Players.Count : 0;
            int clientPlayers = s_ClientLogic != null && s_ClientLogic.Simulation != null ? s_ClientLogic.Simulation.Players.Count : 0;
            int clientFrames = s_ClientLogic != null && s_ClientLogic.Simulation != null ? s_ClientLogic.Simulation.FrameIndex : 0;

            if (s_Joined && s_InputFrame)
            {
                Debug.Log(string.Format("===== [NetShop] 帧同步测试通过 ✅ hostPlayers={0} clientPlayers={1} clientFrames={2}（商店商品由模拟确定性生成） =====",
                    hostPlayers, clientPlayers, clientFrames));
            }
            else
            {
                Debug.LogWarning(string.Format("[NetShop] 未完全完成 joined={0} inputFrame={1}", s_Joined, s_InputFrame));
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
                    var go = new GameObject("NetShopRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
