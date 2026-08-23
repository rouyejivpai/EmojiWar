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
    }
}
