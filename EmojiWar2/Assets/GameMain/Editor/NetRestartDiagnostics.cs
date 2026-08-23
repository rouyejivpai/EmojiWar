//------------------------------------------------------------
// EmojiWar GameMain - 多人重开（下一局）测试工具（Editor）
// 菜单：EmojiWar/Diagnostics/Net Restart Test
// 单进程内：Host + ClientA + ClientB 回环（端口 7795），验证：
//   1. 双客户端加入 + 敌人生成广播
//   2. Host.ResetRunAndBroadcast() → 双客户端收到 S2CRunRestart
//   3. 服务器波次归零并重启（重置后新敌人生成）
//   4. 客户端实体重建（玩家实体重新广播）
// 结果写入 Logs/diag.txt（绕 console 截断）。
//------------------------------------------------------------

using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 多人重开（下一局）测试。
    /// </summary>
    public static class NetRestartDiagnostics
    {
        private const int Port = 7795;

        private static NetworkService s_Host = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetworkService s_ClientA = null;
        private static NetClientLogic s_ClientALogic = null;
        private static NetworkService s_ClientB = null;
        private static NetClientLogic s_ClientBLogic = null;

        private static int s_EnemiesSpawnedA = 0;
        private static int s_EnemiesSpawnedB = 0;
        private static bool s_RunRestartReceivedA = false;
        private static bool s_RunRestartReceivedB = false;
        private static bool s_ResetTriggered = false;
        private static int s_PostResetEnemiesA = 0;
        private static int s_PostResetEnemiesB = 0;
        private static int s_PostResetPlayersA = 0;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Restart Test")]
        public static void RunRestartTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[Restart] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_EnemiesSpawnedA = 0;
            s_EnemiesSpawnedB = 0;
            s_RunRestartReceivedA = false;
            s_RunRestartReceivedB = false;
            s_ResetTriggered = false;
            s_PostResetEnemiesA = 0;
            s_PostResetEnemiesB = 0;
            s_PostResetPlayersA = 0;
            s_Timeout = 30f;

            // ---- Host ----
            var hostGo = new GameObject("RestartHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            if (!s_Host.StartHost(Port))
            {
                Debug.LogError("[Restart] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client A ----
            var clientAGo = new GameObject("RestartClientA");
            s_ClientA = clientAGo.AddComponent<NetworkService>();
            s_ClientALogic = clientAGo.AddComponent<NetClientLogic>();
            s_ClientALogic.Bind(s_ClientA);
            s_ClientA.OnServerMessage += (msg) =>
            {
                if (msg is S2CSpawnEntity spawn)
                {
                    if (spawn.Type == 1)
                    {
                        s_EnemiesSpawnedA++;
                        if (s_ResetTriggered)
                        {
                            s_PostResetEnemiesA++;
                        }
                    }
                    else if (s_ResetTriggered)
                    {
                        s_PostResetPlayersA++;
                    }
                }
                if (msg is S2CRunRestart)
                {
                    s_RunRestartReceivedA = true;
                }
            };
            s_ClientA.ConnectToServer("127.0.0.1", Port);

            // ---- Client B ----
            var clientBGo = new GameObject("RestartClientB");
            s_ClientB = clientBGo.AddComponent<NetworkService>();
            s_ClientBLogic = clientBGo.AddComponent<NetClientLogic>();
            s_ClientBLogic.Bind(s_ClientB);
            s_ClientB.OnServerMessage += (msg) =>
            {
                if (msg is S2CSpawnEntity spawn && spawn.Type == 1)
                {
                    s_EnemiesSpawnedB++;
                    if (s_ResetTriggered)
                    {
                        s_PostResetEnemiesB++;
                    }
                }
                if (msg is S2CRunRestart)
                {
                    s_RunRestartReceivedB = true;
                }
            };
            s_ClientB.ConnectToServer("127.0.0.1", Port);

            Runner.Start(RunCoroutine());
        }

        private static IEnumerator RunCoroutine()
        {
            var sb = new StringBuilder();

            // 等待双客户端连接
            float wait = 5f;
            while (wait > 0f && (s_ClientA == null || !s_ClientA.IsConnected || s_ClientB == null || !s_ClientB.IsConnected))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (s_ClientA == null || !s_ClientA.IsConnected || s_ClientB == null || !s_ClientB.IsConnected)
            {
                sb.AppendLine("[Restart] FAIL: 客户端连接失败");
                Finish(sb);
                yield break;
            }

            // 加入房间（连接建立后自动补发加入请求）
            s_ClientALogic.JoinRoom("PlayerA");
            s_ClientBLogic.JoinRoom("PlayerB");
            Debug.Log("[Restart] 两个客户端已加入，等待波次敌人生成");

            // 等待：两客户端都看到敌人
            while (s_Timeout > 0f && !(s_EnemiesSpawnedA >= 2 && s_EnemiesSpawnedB >= 2))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            if (!(s_EnemiesSpawnedA >= 2 && s_EnemiesSpawnedB >= 2))
            {
                sb.AppendLine(string.Format("[Restart] FAIL: 敌人生成不足 A={0} B={1}", s_EnemiesSpawnedA, s_EnemiesSpawnedB));
                Finish(sb);
                yield break;
            }

            int countBeforeA = s_ClientALogic != null ? s_ClientALogic.LocalEntityCount : 0;
            int countBeforeB = s_ClientBLogic != null ? s_ClientBLogic.LocalEntityCount : 0;
            int waveBefore = s_HostLogic != null ? s_HostLogic.WaveIndex : -1;
            sb.AppendLine(string.Format("[Restart] 重置前: A实体={0} B实体={1} 服务器波次={2}", countBeforeA, countBeforeB, waveBefore));

            // 房主重开：服务器权威重置 + 广播
            s_ResetTriggered = true;
            if (s_HostLogic != null)
            {
                s_HostLogic.ResetRunAndBroadcast();
            }

            // 等待 S2CRunRestart 到达
            wait = 6f;
            while (wait > 0f && !(s_RunRestartReceivedA && s_RunRestartReceivedB))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            // 等待新波次敌人生成
            wait = 6f;
            while (wait > 0f && !(s_PostResetEnemiesA >= 1 && s_PostResetEnemiesB >= 1))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            int countAfterA = s_ClientALogic != null ? s_ClientALogic.LocalEntityCount : 0;
            int countAfterB = s_ClientBLogic != null ? s_ClientBLogic.LocalEntityCount : 0;
            int waveAfter = s_HostLogic != null ? s_HostLogic.WaveIndex : -1;
            sb.AppendLine(string.Format("[Restart] 重置后: A实体={0} B实体={1} 服务器波次={2} 重置后新敌A={3} 重置后新敌B={4} 重置后玩家重建A={5}",
                countAfterA, countAfterB, waveAfter, s_PostResetEnemiesA, s_PostResetEnemiesB, s_PostResetPlayersA));

            // ---- 判定 ----
            bool ok = true;
            if (!(s_RunRestartReceivedA && s_RunRestartReceivedB))
            {
                sb.AppendLine("[Restart] FAIL: 客户端未收到 S2CRunRestart");
                ok = false;
            }
            if (!(waveAfter >= 1))
            {
                sb.AppendLine(string.Format("[Restart] FAIL: 服务器波次未重启 {0}->{1}", waveBefore, waveAfter));
                ok = false;
            }
            if (!(s_PostResetEnemiesA >= 1 && s_PostResetEnemiesB >= 1))
            {
                sb.AppendLine(string.Format("[Restart] FAIL: 重置后新敌人生成不足 A={0} B={1}", s_PostResetEnemiesA, s_PostResetEnemiesB));
                ok = false;
            }
            if (!(countAfterA > 0 && countAfterB > 0))
            {
                sb.AppendLine(string.Format("[Restart] FAIL: 客户端实体未重建 A={0} B={1}", countAfterA, countAfterB));
                ok = false;
            }

            sb.AppendLine(ok ? "===== [Restart] 多人重开（下一局）测试通过 ✅ =====" : "===== [Restart] 多人重开测试未通过 ❌ =====");
            Finish(sb);
        }

        private static void Finish(StringBuilder sb)
        {
            Debug.Log(sb.ToString());
            try
            {
                string path = Path.Combine(Application.dataPath, "../Logs/diag.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Restart] 写 diag 失败: " + e.Message);
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
                    var go = new GameObject("RestartRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
