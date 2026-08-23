//------------------------------------------------------------
// EmojiWar GameMain - 商店流程
// 波间商店：监听战斗管理器的 OnShopPhase 事件，
// 打开商店 UI；"继续战斗"后关闭 UI 并恢复战斗。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 商店流程：波间商店环节。
    /// </summary>
    public class ProcedureShop : ProcedureBase
    {
        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Shop =====");

            OpenShopForm();
        }

        private void OpenShopForm()
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

            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.ShopForm, Constant.UIGroup.Default, this);
        }

        /// <summary>
        /// 关闭商店并返回战斗（由 ShopForm 的"继续战斗"触发）。
        /// </summary>
        public void CloseShop()
        {
            // 关闭所有 ShopForm 实例
            if (GameEntry.UI != null)
            {
                var forms = GameEntry.UI.GetAllUIGroups();
                // 简化：通过事件回调在 ShopForm 内关闭自身
            }

            // 恢复战斗波次
            var manager = UnityEngine.Object.FindObjectOfType<Battle.BattleManager>();
            if (manager != null)
            {
                manager.ResumeAfterShop();
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}
