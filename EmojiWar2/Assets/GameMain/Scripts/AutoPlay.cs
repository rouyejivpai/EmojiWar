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

        public static void TryStart()
        {
            if (s_Started)
            {
                return;
            }

            var args = System.Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg == "-autocreate")
                {
                    s_Started = true;
                    var go = new GameObject("AutoPlay");
                    go.AddComponent<AutoPlay>();
                    Debug.Log("[AutoPlay] -autocreate 参数检测到，启动自动流程");
                    return;
                }
            }
        }

        private void Start()
        {
            StartCoroutine(AutoFlow());
        }

        private IEnumerator AutoFlow()
        {
            WriteProbe("[auto] auto flow started");

            // 等待启动流程与主菜单就绪
            yield return new WaitForSeconds(4f);
            WriteProbe("[auto] trigger start game");
            UI.MenuForm.TriggerStartGame();

            yield return new WaitForSeconds(2f);
            WriteProbe("[auto] trigger create room");
            UI.LobbyForm.TriggerCreateRoom();

            // 等待战斗开始（BattleManager 会写入 SpawnPlayer/SpawnEnemy 探针）
            yield return new WaitForSeconds(8f);

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

            var players = Object.FindObjectsOfType<Entity.PlayerEntity>();
            var enemies = Object.FindObjectsOfType<Entity.EnemyEntity>();
            WriteProbe(string.Format("[auto] SpriteRenderers={0} withSprite={1} players={2} enemies={3}",
                renderers.Length, withSprite, players.Length, enemies.Length));
            Debug.Log(string.Format("[AutoPlay] 战斗实体统计: SpriteRenderers={0} withSprite={1} players={2} enemies={3}",
                renderers.Length, withSprite, players.Length, enemies.Length));

            // 等玩家被敌人打死 → 结算界面 → 模拟点"重新开始"，验证重开路径不产生多玩家
            yield return new WaitForSeconds(15f);
            WriteProbe("[auto] trigger restart (game over flow)");
            UI.GameOverEvents.RequestRestart();
            yield return new WaitForSeconds(8f);

            var players2 = Object.FindObjectsOfType<Entity.PlayerEntity>();
            var enemies2 = Object.FindObjectsOfType<Entity.EnemyEntity>();
            WriteProbe(string.Format("[auto] after-restart: players={0} enemies={1}",
                players2.Length, enemies2.Length));
            Debug.Log(string.Format("[AutoPlay] 重开后: players={0} enemies={1}",
                players2.Length, enemies2.Length));
        }

        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
