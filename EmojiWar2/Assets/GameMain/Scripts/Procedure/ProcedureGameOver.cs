//------------------------------------------------------------
// EmojiWar GameMain - 游戏结束流程
// 玩家死亡后进入：打开结算 UI，处理重新开始/返回菜单。
// 重新开始：重载战斗场景（清场）保证单机与多人下一局干净开局；
//           Host 模式下同时重置服务器权威状态并广播 S2CRunRestart。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 游戏结束流程：结算展示与去向选择。
    /// </summary>
    public class ProcedureGameOver : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Game Over =====");
            WriteProbe("[gameover] OnEnter, activeScene=" + SceneManager.GetActiveScene().name);

            m_ProcedureFsm = procedureOwner;

            UI.GameOverEvents.OnRestartRequested += OnRestartRequested;
            UI.GameOverEvents.OnMenuRequested += OnMenuRequested;

            OpenGameOverForm();
        }

        private void OpenGameOverForm()
        {
            if (GameEntry.UI == null)
            {
                Log.Error("UIComponent is null.");
                return;
            }

            if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Default))
            {
                GameEntry.UI.AddUIGroup(Constant.UIGroup.Default);
            }

            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.GameOverForm, Constant.UIGroup.Default, this);
        }

        private void OnRestartRequested()
        {
            Log.Info("[ProcedureGameOver] 重新开始");
            WriteProbe("[gameover] RestartRequested");

            // 关闭当前战斗 HUD、结算与商店 UI
            CloseBattleForms();

            // Host 模式：重置服务器权威状态（清敌、波次归零、重置 HP）并广播 S2CRunRestart
            var net = GameEntry.NetworkService;
            if (net != null && net.Mode == Network.NetMode.Host)
            {
                var hostLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponent<Network.NetHostLogic>()
                    : null;
                if (hostLogic != null)
                {
                    hostLogic.ResetRunAndBroadcast();
                }
            }

            // 进入战斗流程：由 ProcedureBattle 清理旧实体后重新开局（不重载场景，避免破坏框架）
            ChangeState<ProcedureBattle>(m_ProcedureFsm);
        }

        private void OnMenuRequested()
        {
            Log.Info("[ProcedureGameOver] 返回菜单");
            WriteProbe("[gameover] MenuRequested");

            // 离开房间：停止服务器 / 断开连接
            if (GameEntry.NetworkService != null)
            {
                GameEntry.NetworkService.Shutdown();
            }
            var clientLogic = GameEntry.Instance != null
                ? GameEntry.Instance.GetComponent<Network.NetClientLogic>()
                : null;
            if (clientLogic != null)
            {
                clientLogic.LeaveRoom();
            }

            CloseBattleForms();

            // 菜单流程负责加载菜单场景并打开菜单 UI
            ChangeState<ProcedureMenu>(m_ProcedureFsm);
        }

        /// <summary>关闭战斗相关的 UI 窗体（HUD / 商店 / 结算）。</summary>
        private void CloseBattleForms()
        {
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

        /// <summary>按名称查找 UIForm 并关闭（简化实现）。</summary>
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

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.GameOverEvents.OnRestartRequested -= OnRestartRequested;
            UI.GameOverEvents.OnMenuRequested -= OnMenuRequested;
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
