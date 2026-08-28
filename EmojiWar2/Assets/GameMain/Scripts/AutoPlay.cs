//------------------------------------------------------------
// EmojiWar GameMain - 自动化联调辅助（构建版）
// 启动参数含 -autocreate 时自动执行：开始游戏 → 创建房间 → 进入战斗，
// 并把战斗实体/美术加载结果写入 Builds/Logs/runtime_probe.txt。
// 用途：沙箱无法操作 exe GUI，通过该参数验证构建版完整战斗流程。
//------------------------------------------------------------

using System.Collections;
using UnityEngine;

namespace EmojiWar.GameMain
{
    /// <summary>
    /// 自动化流程辅助（仅 -autocreate 启动参数时激活）。
    /// </summary>
    public class AutoPlay : MonoBehaviour
    {
        private static bool s_Started = false;
        private static bool s_IsJoiner = false;

        /// <summary>AutoPlay 是否激活（流程据此决定是否保留自动移动，用于回环测试）。</summary>
        public static bool IsActive
        {
            get { return s_Started; }
        }

        public static void TryStart()
        {
            if (s_Started)
            {
                return;
            }

            var args = System.Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg == "-autocreate" || arg == "-autojoin")
                {
                    s_Started = true;
                    s_IsJoiner = arg == "-autojoin";
                    var go = new GameObject("AutoPlay");
                    go.AddComponent<AutoPlay>();
                    Debug.Log("[AutoPlay] " + arg + " 参数检测到，启动自动流程");
                    return;
                }
            }
        }

        private void Start()
        {
            StartCoroutine(s_IsJoiner ? AutoJoinFlow() : AutoFlow());
            StartCoroutine(FrameTimeMonitor());
        }

        /// <summary>退出诊断：记录进程退出时刻与上下文（区分崩溃/正常关闭）。</summary>
        private void OnApplicationQuit()
        {
            WriteProbe("[auto] OnApplicationQuit called (graceful quit path)");
        }

        private void OnDisable()
        {
            WriteProbe("[auto] AutoPlay OnDisable (component disabled/destroyed)");
        }

        private void OnDestroy()
        {
            WriteProbe("[auto] AutoPlay OnDestroy");
        }

        /// <summary>
        /// 帧时间监视：每 5 秒报告一次平均帧时间与最大帧时间（定位卡顿尖峰）。
        /// </summary>
        private IEnumerator FrameTimeMonitor()
        {
            var wait = new WaitForSeconds(5f);
            while (true)
            {
                yield return wait;
                int frames = 0;
                float total = 0f;
                float maxDt = 0f;
                for (int i = 0; i < 300; i++)   // 采样 300 帧（约 1-5 秒）
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    frames++;
                    total += dt;
                    if (dt > maxDt) maxDt = dt;
                }
                float avg = frames > 0 ? total / frames : 0f;
                WriteProbe(string.Format("[fps] avg={0:F1}ms ({1:F0}fps) max={2:F1}ms ({3:F0}fps)",
                    avg * 1000f, frames > 0 ? frames / total : 0f,
                    maxDt * 1000f, maxDt > 0f ? 1f / maxDt : 0f));
            }
        }

        /// <summary>加入者自动流程：开始游戏 → 加入 → 房间自动准备 → 全部准备后自动开始。</summary>
        private IEnumerator AutoJoinFlow()
        {
            WriteProbe("[auto] join flow started");

            yield return new WaitForSeconds(4f);
            WriteProbe("[auto] trigger start game");
            UI.MenuForm.TriggerStartGame();

            // 角色选择：自动选 1 号角色
            yield return new WaitForSeconds(3f);
            WriteProbe("[auto] select character 1");
            UI.CharacterSelectEvents.Select(1);

            yield return new WaitForSeconds(2f);
            WriteProbe("[auto] trigger join 127.0.0.1:7777");
            UI.LobbyForm.TriggerJoinRoom("127.0.0.1", Network.NetworkService.DefaultPort);

            // 进入房间后自动准备
            yield return new WaitForSeconds(3f);
            WriteProbe("[auto] joiner trigger ready");
            UI.RoomFormEvents.RequestReady();

            // 等待战斗开始（全部准备 → 自动开始）
            yield return new WaitForSeconds(12f);
            var battleScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Battle");
            WriteProbe("[auto] joiner battleSceneLoaded=" + battleScene.isLoaded);

            // UI 状态探针：列出当前所有 UIForm（验证进战斗后旧 UI 是否已关闭）
            try
            {
                var ui = GameEntry.UI;
                var forms = new System.Collections.Generic.List<string>();
                if (ui != null)
                {
                    foreach (var g in ui.GetAllUIGroups())
                    {
                        foreach (var f in g.GetAllUIForms())
                        {
                            var logic = (f as UnityGameFramework.Runtime.UIForm);
                            if (logic != null && logic.Logic != null)
                            {
                                forms.Add(logic.Logic.Name + "(" + (logic.Logic.gameObject.activeInHierarchy ? "A" : "I") + ")");
                            }
                        }
                    }
                }
                var canvases = Object.FindObjectsOfType<Canvas>(true);
                var overlayList = new System.Collections.Generic.List<string>();
                foreach (var c in canvases)
                {
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy)
                    {
                        overlayList.Add(c.gameObject.name);
                    }
                }
                WriteProbe("[auto] joiner UIForms=" + string.Join(",", forms.ToArray()) +
                    " overlay=" + string.Join(",", overlayList.ToArray()));
            }
            catch (System.Exception e)
            {
                WriteProbe("[auto] joiner UI probe error: " + e.Message);
            }

            // 统计网络实体（确定性模拟玩家）
            var netLogics = Object.FindObjectsOfType<Network.NetClientLogic>();
            int total = 0;
            foreach (var n in netLogics)
            {
                if (n.Simulation != null)
                {
                    total += n.Simulation.Players.Count;
                }
            }
            WriteProbe("[auto] joiner net-players=" + total + " (expect >=1 = host entity)");
            Debug.Log("[AutoPlay] 加入者模拟玩家数=" + total);
        }

        private IEnumerator AutoFlow()
        {
            WriteProbe("[auto] auto flow started");

            // 等待启动流程与主菜单就绪
            yield return new WaitForSeconds(4f);
            WriteProbe("[auto] trigger start game");
            UI.MenuForm.TriggerStartGame();

            // 角色选择：自动选 1 号角色
            yield return new WaitForSeconds(3f);
            WriteProbe("[auto] select character 1");
            UI.CharacterSelectEvents.Select(1);

            yield return new WaitForSeconds(2f);
            WriteProbe("[auto] trigger create room");
            UI.LobbyForm.TriggerCreateRoom();

            // 进入房间后自动准备（延迟等待其他玩家加入；全部准备后自动开始）
            yield return new WaitForSeconds(30f);
            WriteProbe("[auto] host trigger ready");
            UI.RoomFormEvents.RequestReady();

            // 等待战斗开始（模拟驱动：SimView 渲染玩家/敌人/子弹）
            yield return new WaitForSeconds(10f);

            // UI 状态探针：列出当前所有 UIForm（验证进战斗后旧 UI 是否已关闭）
            try
            {
                var ui = GameEntry.UI;
                var forms = new System.Collections.Generic.List<string>();
                if (ui != null)
                {
                    foreach (var g in ui.GetAllUIGroups())
                    {
                        foreach (var f in g.GetAllUIForms())
                        {
                            var logic = (f as UnityGameFramework.Runtime.UIForm);
                            if (logic != null && logic.Logic != null)
                            {
                                forms.Add(logic.Logic.Name + "(" + (logic.Logic.gameObject.activeInHierarchy ? "A" : "I") + ")");
                            }
                        }
                    }
                }
                var canvases = Object.FindObjectsOfType<Canvas>(true);
                var overlayList = new System.Collections.Generic.List<string>();
                foreach (var c in canvases)
                {
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy)
                    {
                        overlayList.Add(c.gameObject.name);
                    }
                }
                WriteProbe("[auto] UIForms=" + string.Join(",", forms.ToArray()) +
                    " overlay=" + string.Join(",", overlayList.ToArray()));
            }
            catch (System.Exception e)
            {
                WriteProbe("[auto] UI probe error: " + e.Message);
            }

            // 统计场景中实际渲染的 SpriteRenderer（验证美术是否生效）
            var renderers = Object.FindObjectsOfType<SpriteRenderer>();
            int withSprite = 0;
            foreach (var r in renderers)
            {
                if (r != null && r.sprite != null)
                {
                    withSprite++;
                }
            }

            // 统计确定性模拟实体（SimView 渲染 SimPlayer_/SimEnemy_/SimBullet_）
            int simPlayers = 0;
            int simEnemies = 0;
            int simBullets = 0;
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null)
                {
                    continue;
                }
                if (go.name.StartsWith("SimPlayer_"))
                {
                    simPlayers++;
                }
                else if (go.name.StartsWith("SimEnemy_"))
                {
                    simEnemies++;
                }
                else if (go.name.StartsWith("SimBullet_"))
                {
                    simBullets++;
                }
            }

            // 读取本地确定性模拟状态（帧号/玩家位置/敌人数，供双实例一致性对比）
            var sim = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            string simState = "no-sim";
            if (sim != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("frame=").Append(sim.FrameIndex)
                  .Append(" wave=").Append(sim.WaveIndex)
                  .Append(" players=").Append(sim.Players.Count)
                  .Append(" enemies=").Append(sim.Enemies.Count)
                  .Append(" bullets=").Append(sim.Bullets.Count);
                foreach (var p in sim.Players)
                {
                    sb.Append(" P").Append(p.SessionId).Append(":(").Append(p.Position.x.ToString("F2"))
                      .Append(",").Append(p.Position.y.ToString("F2")).Append(")");
                }
                simState = sb.ToString();
            }

            var players = Object.FindObjectsOfType<Entity.PlayerEntity>();
            var enemies = Object.FindObjectsOfType<Entity.EnemyEntity>();
            WriteProbe(string.Format("[auto] SpriteRenderers={0} simPlayers={1} simEnemies={2} simBullets={3} simState={4}",
                renderers.Length, simPlayers, simEnemies, simBullets, simState));
            Debug.Log(string.Format("[AutoPlay] 模拟实体统计: simPlayers={0} simEnemies={1} simBullets={2} simState={3}",
                simPlayers, simEnemies, simBullets, simState));

            // 等玩家被敌人打死 → 结算界面 → 模拟点"重新开始"，验证重开路径不产生多玩家
            yield return new WaitForSeconds(15f);
            WriteProbe("[auto] trigger restart (game over flow)");
            UI.GameOverEvents.RequestRestart();
            yield return new WaitForSeconds(8f);

            var players2 = Object.FindObjectsOfType<Entity.PlayerEntity>();
            var enemies2 = Object.FindObjectsOfType<Entity.EnemyEntity>();
            WriteProbe(string.Format("[auto] after-restart: legacyPlayers={0} legacyEnemies={1}",
                players2.Length, enemies2.Length));
            Debug.Log(string.Format("[AutoPlay] 重开后: legacyPlayers={0} legacyEnemies={1}",
                players2.Length, enemies2.Length));

            // 同进程多局回归（文档 §6 B 组）：回到房间后再次准备开第二局，
            // 对比两局的 seed 与模拟初始状态，验证无跨局状态污染。
            // 房间页已恢复（seed=0 房间模拟）；再次触发准备 → 全部准备 → 第二局开始
            WriteProbe("[auto] 多局回归：第二局准备");
            UI.RoomFormEvents.RequestReady();
            yield return new WaitForSeconds(10f);

            var sim2 = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            if (sim2 != null)
            {
                WriteProbe(string.Format("[auto] run2 frame={0} wave={1} players={2} enemies={3}",
                    sim2.FrameIndex, sim2.WaveIndex, sim2.Players.Count, sim2.Enemies.Count));
            }
            else
            {
                WriteProbe("[auto] run2 no-sim（第二局未启动，检查房间/准备流程）");
            }

            // 流程结束标记：若协程自然跑完，后续 OnDestroy/OnApplicationQuit 探针可定位进程退出原因
            WriteProbe("[auto] AutoFlow finished (coroutine end)");
        }

        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
