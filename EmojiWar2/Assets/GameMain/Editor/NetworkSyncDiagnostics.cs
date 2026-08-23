//------------------------------------------------------------
// EmojiWar GameMain - 网络同步诊断工具（Editor）
// 菜单：EmojiWar/Diagnostics/Net Sync Loopback Test
// 单进程内：Host + Client 回环，验证：
//   Client 加入 → Host 生成实体 → Client 上行输入 → Host 更新位置
//   → 广播 EntityState → Client 应用位置
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 网络同步回环测试。
    /// </summary>
    public static class NetworkSyncDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetworkService s_Client = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetClientLogic s_ClientLogic = null;

        private static bool s_Spawned = false;
        private static bool s_StateApplied = false;
        private static Vector3 s_LastPosition = Vector3.zero;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Sync Loopback Test")]
        public static void RunSyncTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetSync] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_Spawned = false;
            s_StateApplied = false;
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
                if (msg.Id == MsgId.SpawnEntity)
                {
                    s_Spawned = true;
                }
                if (msg is S2CEntityState state)
                {
                    s_StateApplied = true;
                    s_LastPosition = new Vector3(state.X, state.Y, 0f);
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
            s_ClientLogic.JoinRoom("SyncTester");

            // 等待生成 + 状态应用（客户端每帧上行输入，Host 广播状态）
            while (s_Timeout > 0f && !(s_Spawned && s_StateApplied))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            Debug.Log(string.Format("[NetSync] 测试结束。spawned={0} stateApplied={1} hostSessions={2}",
                s_Spawned, s_StateApplied, s_Host.ConnectedClients));

            // 额外等待几帧，确认位置持续更新（同步流活跃）
            Vector3 posA = s_LastPosition;
            yield return new WaitForSeconds(1f);
            Vector3 posB = s_LastPosition;

            bool positionMoving = (posB - posA).sqrMagnitude > 0.0001f;

            if (s_Spawned && s_StateApplied && positionMoving)
            {
                Debug.Log(string.Format("===== [NetSync] 同步测试通过 ✅ 实体位置持续更新 ({0:F2},{1:F2}) -> ({2:F2},{3:F2}) =====",
                    posA.x, posA.y, posB.x, posB.y));
            }
            else
            {
                Debug.LogWarning(string.Format("[NetSync] 同步未完全完成 spawned={0} stateApplied={1} moving={2}",
                    s_Spawned, s_StateApplied, positionMoving));
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
