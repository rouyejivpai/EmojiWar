//------------------------------------------------------------
// EmojiWar GameMain - 战斗流程
// 加载战斗场景，监听场景加载完成事件后启动战斗管理器。
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
        private const string BattleManagerPrefabPath = "Assets/GameMain/Entities/BattleManager.prefab";

        // 通过 userData 传入的角色 ID（Phase 2 固定为 1，后续接选角）
        private int m_CharacterId = 1;
        private bool m_SceneLoaded = false;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Battle =====");

            m_SceneLoaded = false;

            // 订阅场景加载成功事件
            GameEntry.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);

            if (SceneManager.GetActiveScene().name != "Battle")
            {
                GameEntry.Scene.LoadScene(BattleSceneAssetName, this);
            }
            else
            {
                m_SceneLoaded = true;
                StartBattle();
            }
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            var args = e as LoadSceneSuccessEventArgs;
            if (args == null || args.UserData != this)
            {
                return;
            }

            Log.Info("[ProcedureBattle] 战斗场景加载完成: {0}", args.SceneAssetName);
            m_SceneLoaded = true;
            StartBattle();
        }

        private void StartBattle()
        {
            if (GameEntry.Data == null || !GameEntry.Data.IsReady)
            {
                Log.Error("[ProcedureBattle] DataComponent not ready.");
                return;
            }

            GameObject managerPrefab = LoadPrefab(BattleManagerPrefabPath);
            if (managerPrefab == null)
            {
                Log.Error("[ProcedureBattle] BattleManager prefab not found.");
                return;
            }

            GameObject managerGo = Object.Instantiate(managerPrefab);
            var manager = managerGo.GetComponent<Battle.BattleManager>();
            if (manager != null)
            {
                manager.StartBattle(m_CharacterId);
                Log.Info("[ProcedureBattle] Battle started, character id={0}", m_CharacterId);
            }
        }

        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return Resources.Load<GameObject>(path);
#endif
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            if (GameEntry.Event != null)
            {
                GameEntry.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            }
            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}
