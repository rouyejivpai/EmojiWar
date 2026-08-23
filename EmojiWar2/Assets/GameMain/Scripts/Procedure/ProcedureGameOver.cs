//------------------------------------------------------------
// EmojiWar GameMain - 游戏结束流程
// 玩家死亡后进入：打开结算 UI，处理重新开始/返回菜单。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
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

            // 关闭当前战斗 HUD 与结算 UI
            GameEntry.UI.CloseUIForm(GetUIForm("BattleHudForm(Clone)"));
            GameEntry.UI.CloseUIForm(GetUIForm("GameOverForm(Clone)"));

            ChangeState<ProcedureBattle>(m_ProcedureFsm);
        }

        private void OnMenuRequested()
        {
            Log.Info("[ProcedureGameOver] 返回菜单");

            GameEntry.UI.CloseUIForm(GetUIForm("GameOverForm(Clone)"));
            ChangeState<ProcedureMenu>(m_ProcedureFsm);
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
    }
}
