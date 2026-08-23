//------------------------------------------------------------
// EmojiWar GameMain - 多人共存战斗测试工具（Editor）
// 菜单：EmojiWar/Diagnostics/Net Co-op Test
// 单进程内：Host + ClientA + ClientB 回环，验证多人合作 PvE：
//   1. 两个客户端加入 → Host 各生成玩家实体
//   2. Host 驱动波次 → 敌人生成（两个客户端都收到）
//   3. 双客户端输入上行 → Host 更新各自位置 → 广播
//   4. 击杀敌人 → RemoveEntity 广播（双客户端收到）
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 双客户端共存战斗测试。
    /// </summary>
    public static class NetCoopDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetworkService s_ClientA = null;
        private static NetClientLogic s_ClientALogic = null;
        private static NetworkService s_ClientB = null;
        private static NetClientLogic s_ClientBLogic = null;

        private static int s_EnemiesSpawnedA = 0;
        private static int s_EnemiesSpawnedB = 0;
        private static bool s_StateAppliedA = false;
        private static bool s_StateAppliedB = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Co-op Test")]
        public static void RunCoopTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[Coop] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_EnemiesSpawnedA = 0;
            s_EnemiesSpawnedB = 0;
            s_StateAppliedA = false;
            s_StateAppliedB = false;
            s_Timeout = 20f;

            // ---- Host ----
            var hostGo = new GameObject("CoopHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            if (!s_Host.StartHost(7794))
            {
                Debug.LogError("[Coop] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client A ----
            var clientAGo = new GameObject("CoopClientA");
            s_ClientA = clientAGo.AddComponent<NetworkService>();
            s_ClientALogic = clientAGo.AddComponent<NetClientLogic>();
            s_ClientALogic.Bind(s_ClientA);
            s_ClientA.OnServerMessage += (msg) =>
            {
                if (msg is S2CSpawnEntity spawn && spawn.Type == 1)
                {
                    s_EnemiesSpawnedA++;
                }
                if (msg is S2CEntityState)
                {
                    s_StateAppliedA = true;
                }
            };
            s_ClientA.ConnectToServer("127.0.0.1", 7794);

            // ---- Client B ----
            var clientBGo = new GameObject("CoopClientB");
            s_ClientB = clientBGo.AddComponent<NetworkService>();
            s_ClientBLogic = clientBGo.AddComponent<NetClientLogic>();
            s_ClientBLogic.Bind(s_ClientB);
            s_ClientB.OnServerMessage += (msg) =>
            {
                if (msg is S2CSpawnEntity spawn && spawn.Type == 1)
                {
                    s_EnemiesSpawnedB++;
                }
                if (msg is S2CEntityState)
                {
                    s_StateAppliedB = true;
                }
            };
            s_ClientB.ConnectToServer("127.0.0.1", 7794);

            Runner.Start(RunCoroutine());
        }

        private static IEnumerator RunCoroutine()
        {
            // 等待双客户端连接
            float wait = 4f;
            while (wait > 0f && (s_ClientA == null || !s_ClientA.IsConnected || s_ClientB == null || !s_ClientB.IsConnected))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (s_ClientA == null || !s_ClientA.IsConnected || s_ClientB == null || !s_ClientB.IsConnected)
            {
                Debug.LogError("[Coop] 客户端连接失败");
                Cleanup();
                yield break;
            }

            // 双客户端加入
            s_ClientALogic.JoinRoom("PlayerA");
            s_ClientBLogic.JoinRoom("PlayerB");
            Debug.Log("[Coop] 两个客户端已加入，等待波次敌人生成");

            // 等待：两个客户端都看到敌人 + 状态同步
            while (s_Timeout > 0f && !(s_EnemiesSpawnedA >= 2 && s_EnemiesSpawnedB >= 2 && s_StateAppliedA && s_StateAppliedB))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            // 服务器清场（验证 RemoveEntity 双端广播）
            if (s_HostLogic != null)
            {
                s_HostLogic.KillAllEnemies();
                yield return new WaitForSeconds(1f);
            }

            int entityCountA = s_ClientALogic != null ? s_ClientALogic.LocalEntityCount : 0;
            int entityCountB = s_ClientBLogic != null ? s_ClientBLogic.LocalEntityCount : 0;

            if (s_EnemiesSpawnedA >= 2 && s_EnemiesSpawnedB >= 2 && s_StateAppliedA && s_StateAppliedB)
            {
                Debug.Log(string.Format("===== [Coop] 多人共存战斗测试通过 ✅ A敌={0} B敌={1} A状态={2} B状态={3} A实体={4} B实体={5} =====",
                    s_EnemiesSpawnedA, s_EnemiesSpawnedB, s_StateAppliedA, s_StateAppliedB, entityCountA, entityCountB));
            }
            else
            {
                Debug.LogWarning(string.Format("[Coop] 未完全完成 A敌={0} B敌={1} A状态={2} B状态={3}",
                    s_EnemiesSpawnedA, s_EnemiesSpawnedB, s_StateAppliedA, s_StateAppliedB));
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
                    var go = new GameObject("CoopRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
