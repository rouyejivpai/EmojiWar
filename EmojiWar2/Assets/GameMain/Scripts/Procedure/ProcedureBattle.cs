//------------------------------------------------------------
// EmojiWar GameMain - 战斗流程
// 加载战斗场景，监听场景加载完成事件后启动战斗管理器。
// 监听波间商店事件，打开商店 UI。
//------------------------------------------------------------

using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 战斗流程：加载战斗场景，启动本地战斗。
    /// </summary>
    public class ProcedureBattle : ProcedureBase
    {
        private const string BattleSceneAssetName = "Assets/GameMain/Scenes/Battle.unity";
        private const string BattleManagerPrefabPath = "Assets/GameMain/Resources/Entities/BattleManager.prefab";

        // 通过 userData 传入的角色 ID（Phase 2 固定为 1，后续接选角）
        private int m_CharacterId = 1;

        /// <summary>当前战斗管理器实例（重开时销毁，避免重复波次）。</summary>
        private Battle.BattleManager m_BattleManager = null;

        /// <summary>当前流程实例（供网络层触发本地重开）。</summary>
        public static ProcedureBattle Current { get; private set; }

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Battle =====");
            WriteProbe("[battle-proc] OnEnter, activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

            Current = this;
            CurrentFsm = procedureOwner;

            // 订阅场景加载成功事件
            GameEntry.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);

            // 订阅波间商店事件
            Battle.BattleManager.OnShopPhase += OnShopPhase;

            // 订阅玩家死亡事件
            Entity.PlayerEntity.OnPlayerDied += OnPlayerDied;

            // 战斗场景：叠加加载架构下场景常驻 —— 已加载则复用并激活，避免重复加载
            var battleScene = SceneManager.GetSceneByName("Battle");
            if (!battleScene.isLoaded)
            {
                GameEntry.Scene.LoadScene(BattleSceneAssetName, this);
            }
            else
            {
                if (SceneManager.GetActiveScene().name != "Battle")
                {
                    SceneManager.SetActiveScene(battleScene);
                }
                // 场景已在战斗（重开/远端重开）：先清理旧实体与旧管理器，再重新开局
                CleanupBattleScene();
                StartBattle();
            }
        }

        private void OnShopPhase(int waveIndex)
        {
            Log.Info("[ProcedureBattle] 第 {0} 波结束，打开商店", waveIndex);

            if (GameEntry.UI == null)
            {
                Log.Error("UIComponent is null, cannot open shop.");
                return;
            }

            if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Default))
            {
                GameEntry.UI.AddUIGroup(Constant.UIGroup.Default);
            }

            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.ShopForm, Constant.UIGroup.Default, this);
        }

        private void OnPlayerDied()
        {
            Log.Info("[ProcedureBattle] 玩家死亡，进入结算");
            ChangeState<ProcedureGameOver>(CurrentFsm);
        }

        /// <summary>当前流程机（事件回调中切换流程用）。</summary>
        private GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager> CurrentFsm { get; set; }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            var args = e as LoadSceneSuccessEventArgs;
            if (args == null || args.UserData != this)
            {
                return;
            }

            Log.Info("[ProcedureBattle] 战斗场景加载完成: {0}", args.SceneAssetName);
            WriteProbe("[battle-proc] LoadSceneSuccess: " + args.SceneAssetName);
            StartBattle();
        }

        private void StartBattle()
        {
            WriteProbe("[battle-proc] StartBattle called, DataReady=" + (GameEntry.Data != null ? GameEntry.Data.IsReady.ToString() : "DataNULL"));
            if (GameEntry.Data == null || !GameEntry.Data.IsReady)
            {
                Log.Error("[ProcedureBattle] DataComponent not ready.");
                return;
            }

            GameObject managerPrefab = LoadPrefab(BattleManagerPrefabPath);
            WriteProbe("[battle-proc] BattleManager prefab=" + (managerPrefab != null ? "OK" : "NULL"));
            if (managerPrefab == null)
            {
                Log.Error("[ProcedureBattle] BattleManager prefab not found.");
                return;
            }

            GameObject managerGo = Object.Instantiate(managerPrefab);
            var manager = managerGo.GetComponent<Battle.BattleManager>();
            m_BattleManager = manager;
            if (manager != null)
            {
                manager.StartBattle(m_CharacterId);
                Log.Info("[ProcedureBattle] Battle started, character id={0}", m_CharacterId);

                // 打开战斗 HUD
                if (GameEntry.UI != null)
                {
                    if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Default))
                    {
                        GameEntry.UI.AddUIGroup(Constant.UIGroup.Default);
                    }
                    GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.BattleHudForm, Constant.UIGroup.Default, this);
                }
            }
        }

        /// <summary>
        /// 重启本地战斗（房主重开广播触发）：清理旧实体后重新开局。
        /// 不重载场景 —— 框架场景为叠加加载，重载会销毁框架对象。
        /// </summary>
        public void RestartRunLocally()
        {
            Log.Info("[ProcedureBattle] 重开本局（房主广播）");

            CloseBattleForms();
            CleanupBattleScene();
            StartBattle();
        }

        /// <summary>
        /// 清理本局残留：旧 BattleManager（停止旧波次）、残留敌人/子弹/玩家。
        /// </summary>
        private void CleanupBattleScene()
        {
            if (m_BattleManager != null)
            {
                Object.Destroy(m_BattleManager.gameObject);
                m_BattleManager = null;
            }

            foreach (var enemy in Object.FindObjectsOfType<Entity.EnemyEntity>())
            {
                if (enemy != null)
                {
                    Object.Destroy(enemy.gameObject);
                }
            }
            foreach (var projectile in Object.FindObjectsOfType<Weapon.Projectile>())
            {
                if (projectile != null)
                {
                    Object.Destroy(projectile.gameObject);
                }
            }
            foreach (var player in Object.FindObjectsOfType<Entity.PlayerEntity>())
            {
                if (player != null)
                {
                    Object.Destroy(player.gameObject);
                }
            }

            Log.Info("[ProcedureBattle] 本局已清理，准备重新开局");
        }

        /// <summary>关闭战斗相关 UI 窗体（HUD / 商店 / 结算）。</summary>
        private void CloseBattleForms()
        {
            if (GameEntry.UI == null)
            {
                return;
            }
            CloseFormIfOpen("BattleHudForm(Clone)");
            CloseFormIfOpen("ShopForm(Clone)");
            CloseFormIfOpen("GameOverForm(Clone)");
        }

        /// <summary>若指定窗体存在则关闭（未打开时静默跳过）。</summary>
        private void CloseFormIfOpen(string name)
        {
            var form = GetUIForm(name);
            if (form != null)
            {
                GameEntry.UI.CloseUIForm(form);
            }
        }

        /// <summary>按名称查找 UIForm（简化实现）。</summary>
        private UnityGameFramework.Runtime.UIForm GetUIForm(string name)
        {
            var ui = GameEntry.UI;
            if (ui == null)
            {
                return null;
            }

            var groups = ui.GetAllUIGroups();
            foreach (var group in groups)
            {
                foreach (var form in group.GetAllUIForms())
                {
                    var formLogic = form as UnityGameFramework.Runtime.UIForm;
                    if (formLogic != null && formLogic.Logic != null && formLogic.Logic.Name == name)
                    {
                        return formLogic;
                    }
                }
            }
            return null;
        }

        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            // 运行时：从 Resources 目录按相对路径加载（去掉 "Assets/.../Resources/" 前缀）
            const string resourcesMarker = "Resources/";
            int index = path.IndexOf(resourcesMarker, System.StringComparison.Ordinal);
            string resourcesPath = index >= 0 ? path.Substring(index + resourcesMarker.Length) : path;
            resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - ".prefab".Length);
            return Resources.Load<GameObject>(resourcesPath);
#endif
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            Battle.BattleManager.OnShopPhase -= OnShopPhase;
            Entity.PlayerEntity.OnPlayerDied -= OnPlayerDied;
            if (GameEntry.Event != null)
            {
                GameEntry.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            }
            if (Current == this)
            {
                Current = null;
            }
            CurrentFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
        }

        /// <summary>运行时探针。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/runtime_probe.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
