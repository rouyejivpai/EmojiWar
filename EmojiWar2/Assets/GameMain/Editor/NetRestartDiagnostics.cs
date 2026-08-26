//------------------------------------------------------------
// EmojiWar GameMain - 多人重开（下一局）测试工具（Editor，帧同步版）
// 菜单：EmojiWar/Diagnostics/Net Restart Test
// 单进程内：Host + ClientA + ClientB 回环（端口 7795），验证确定性帧同步：
//   1. 双客户端加入 → 各自建立确定性模拟
//   2. Host.ResetRoom() → 模拟重建（房间阶段，玩家保留）
//   3. 输入帧持续广播 → 双端模拟 FrameIndex 持续增长（一致性推进）
// 结果写入 Logs/diag.txt。
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
    /// 多人重开（下一局）测试（帧同步）。
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

        private static bool s_JoinedA = false;
        private static bool s_JoinedB = false;
        private static bool s_InputFrameA = false;
        private static bool s_InputFrameB = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Restart Test")]
        public static void RunRestartTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[Restart] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_JoinedA = false;
            s_JoinedB = false;
            s_InputFrameA = false;
            s_InputFrameB = false;
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
                if (msg.Id == MsgId.MyEntity)
                {
                    s_JoinedA = true;
                }
                if (msg is S2CInputFrame)
                {
                    s_InputFrameA = true;
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
                if (msg.Id == MsgId.MyEntity)
                {
                    s_JoinedB = true;
                }
                if (msg is S2CInputFrame)
                {
                    s_InputFrameB = true;
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

            // 加入房间
            s_HostLogic.JoinLocal("HostPlayer", 1);
            s_ClientALogic.JoinRoom("PlayerA");
            s_ClientBLogic.JoinRoom("PlayerB");
            Debug.Log("[Restart] 两个客户端已加入，等待输入帧广播");

            // 等待：双客户端加入 + 输入帧到达
            while (s_Timeout > 0f && !(s_JoinedA && s_JoinedB && s_InputFrameA && s_InputFrameB))
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            if (!(s_JoinedA && s_JoinedB && s_InputFrameA && s_InputFrameB))
            {
                sb.AppendLine(string.Format("[Restart] FAIL: 加入/输入帧不足 A={0}/{1} B={2}/{3}",
                    s_JoinedA, s_InputFrameA, s_JoinedB, s_InputFrameB));
                Finish(sb);
                yield break;
            }

            // 记录推进前帧号
            int framesBeforeA = s_ClientALogic != null && s_ClientALogic.Simulation != null ? s_ClientALogic.Simulation.FrameIndex : 0;
            int framesBeforeB = s_ClientBLogic != null && s_ClientBLogic.Simulation != null ? s_ClientBLogic.Simulation.FrameIndex : 0;
            int hostPlayers = s_HostLogic != null && s_HostLogic.Simulation != null ? s_HostLogic.Simulation.Players.Count : 0;
            sb.AppendLine(string.Format("[Restart] 重置前: A帧={0} B帧={1} Host玩家={2}", framesBeforeA, framesBeforeB, hostPlayers));

            // 房主重置房间（回房间 → 下一局准备）
            if (s_HostLogic != null)
            {
                s_HostLogic.ResetRoom();
                Debug.Log("[Restart] Host.ResetRoom() 已调用");
            }

            // 等待输入帧继续推进（模拟持续运行）
            wait = 6f;
            while (wait > 0f && !(s_ClientALogic.Simulation != null && s_ClientALogic.Simulation.FrameIndex > framesBeforeA
                && s_ClientBLogic.Simulation != null && s_ClientBLogic.Simulation.FrameIndex > framesBeforeB))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            int framesAfterA = s_ClientALogic != null && s_ClientALogic.Simulation != null ? s_ClientALogic.Simulation.FrameIndex : 0;
            int framesAfterB = s_ClientBLogic != null && s_ClientBLogic.Simulation != null ? s_ClientBLogic.Simulation.FrameIndex : 0;
            int hostPlayersAfter = s_HostLogic != null && s_HostLogic.Simulation != null ? s_HostLogic.Simulation.Players.Count : 0;
            sb.AppendLine(string.Format("[Restart] 重置后: A帧={0} B帧={1} Host玩家={2}", framesAfterA, framesAfterB, hostPlayersAfter));

            // ---- 判定 ----
            bool ok = true;
            if (!(framesAfterA > framesBeforeA && framesAfterB > framesBeforeB))
            {
                sb.AppendLine("[Restart] FAIL: 重置后模拟未继续推进（帧号未增长）");
                ok = false;
            }
            if (hostPlayersAfter < 1)
            {
                sb.AppendLine("[Restart] FAIL: 重置后 Host 模拟无玩家");
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