//------------------------------------------------------------
// EmojiWar GameMain - 主菜单流程
// 进入主菜单：加载 Menu 场景（若需要）并打开主菜单 UI。
// 订阅"开始游戏"事件，触发后切换到战斗流程。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
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

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Menu =====");

            m_ProcedureFsm = procedureOwner;
            UI.MenuForm.OnStartGameRequested += OnStartGameRequested;

            // 若当前场景不是菜单场景，则加载菜单场景（编辑器资源模式：Assets 路径）
            if (SceneManager.GetActiveScene().name != "Menu")
            {
                GameEntry.Scene.LoadScene(MenuSceneAssetName, this);
            }
            else
            {
                OpenMenuForm();
            }
        }

        private void OnStartGameRequested()
        {
            Log.Info("[ProcedureMenu] 开始游戏请求，进入大厅");
            ChangeState<ProcedureLobby>(m_ProcedureFsm);
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
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.MenuForm.OnStartGameRequested -= OnStartGameRequested;
            m_ProcedureFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}
