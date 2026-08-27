//------------------------------------------------------------
// EmojiWar GameMain - UI 窗体关闭工具
// 提供按窗体 GameObject 名称关闭 UI 窗体的静态方法（供流程 OnLeave 清理用）。
//------------------------------------------------------------

using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// UI 窗体关闭工具。
    /// </summary>
    public static class UIFormCloser
    {
        /// <summary>
        /// 关闭指定名称的所有 UI 窗体实例（未打开时静默跳过）。
        /// </summary>
        /// <param name="name">窗体 GameObject 名称（含 "(Clone)"）。</param>
        public static void CloseByName(string name)
        {
            var ui = GameEntry.UI;
            if (ui == null)
            {
                return;
            }

            foreach (var group in ui.GetAllUIGroups())
            {
                foreach (var form in group.GetAllUIForms())
                {
                    var formLogic = form as UIForm;
                    if (formLogic != null && formLogic.Logic != null && formLogic.Logic.Name == name)
                    {
                        ui.CloseUIForm(formLogic);
                    }
                }
            }
        }

        /// <summary>
        /// 关闭除指定名称外的所有 UI 窗体（进入战斗时兜底清理旧界面）。
        /// </summary>
        /// <param name="excludeName">保留不关闭的窗体名称（如战斗 HUD），可为 null。</param>
        public static void CloseAllExcept(string excludeName)
        {
            var ui = GameEntry.UI;
            if (ui == null)
            {
                return;
            }

            var toClose = new System.Collections.Generic.List<UIForm>();
            foreach (var group in ui.GetAllUIGroups())
            {
                foreach (var form in group.GetAllUIForms())
                {
                    var formLogic = form as UIForm;
                    if (formLogic == null || formLogic.Logic == null)
                    {
                        continue;
                    }
                    if (excludeName != null && formLogic.Logic.Name == excludeName)
                    {
                        continue;
                    }
                    toClose.Add(formLogic);
                }
            }

            foreach (var form in toClose)
            {
                ui.CloseUIForm(form);
            }
        }
    }
}
