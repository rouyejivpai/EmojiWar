//------------------------------------------------------------
// EmojiWar GameMain - 主菜单流程
// 进入主菜单：加载 Menu 场景（若需要）并打开主菜单 UI。
// 订阅"开始游戏"事件，触发后切换到战斗流程。
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
    /// 主菜单流程：显示标题界面，等待玩家操作。
    /// </summary>
    public class ProcedureMenu : ProcedureBase
    {
        private const string MenuSceneAssetName = "Assets/GameMain/Scenes/Menu.unity";

        // 当前流程机引用（事件回调中需要）
        private IFsm<IProcedureManager> m_ProcedureFsm = null;
        private bool m_Entered = false;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Menu =====");

            // 防重复进入（重复 ChangeState 会导致菜单 UI 重复打开/事件双订阅）
            if (m_Entered)
            {
                WriteProbe("[menu] DUPLICATE OnEnter ignored");
                return;
            }
            m_Entered = true;

            m_ProcedureFsm = procedureOwner;
            UI.MenuForm.OnStartGameRequested += OnStartGameRequested;

            // 订阅场景加载成功事件（从战斗返回时打开菜单 UI）
            GameEntry.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);

            // 菜单场景：叠加加载架构下场景常驻 —— 已加载则复用并激活，避免重复加载产生重复框架对象
            var menuScene = SceneManager.GetSceneByName("Menu");
            if (menuScene.isLoaded)
            {
                if (SceneManager.GetActiveScene().name != "Menu")
                {
                    SceneManager.SetActiveScene(menuScene);
                }
                OpenMenuForm();
            }
            else
            {
                GameEntry.Scene.LoadScene(MenuSceneAssetName, this);
            }
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            var args = e as LoadSceneSuccessEventArgs;
            if (args == null || args.UserData != this)
            {
                return;
            }

            Log.Info("[ProcedureMenu] 菜单场景加载完成: {0}", args.SceneAssetName);
            OpenMenuForm();
        }

        private void OnStartGameRequested()
        {
            Log.Info("[ProcedureMenu] 开始游戏请求，进入角色选择");
            ChangeState<ProcedureCharacterSelect>(m_ProcedureFsm);
        }

        private void OpenMenuForm()
        {
            if (GameEntry.UI == null)
            {
                Log.Error("UIComponent is null.");
                return;
            }

            // 确保 UI 组存在
            if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Default))
            {
                GameEntry.UI.AddUIGroup(Constant.UIGroup.Default);
            }

            // 打开主菜单界面（编辑器模式：资源名为 Assets 相对路径）
            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.MenuForm, Constant.UIGroup.Default, this);

            // 运行时探针：验证构建版菜单 UI 打开
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, "[menu] MenuForm open requested\n");
            }
            catch
            {
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.MenuForm.OnStartGameRequested -= OnStartGameRequested;
            if (GameEntry.Event != null)
            {
                GameEntry.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            }

            // 关闭主菜单窗体，避免重复进入时累积
            UI.UIFormCloser.CloseByName("MenuForm(Clone)");

            m_Entered = false;
            m_ProcedureFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
        }

        /// <summary>运行时探针。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
