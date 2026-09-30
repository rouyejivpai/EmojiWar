//------------------------------------------------------------
// EmojiWar GameMain - 背包快捷键（Tab）
// 仅"战斗流程"（含波间商店阶段，都属 ProcedureBattle）内生效：
//   Tab 打开/关闭背包；离开战斗流程自动关闭。
// 背包打开不暂停游戏（Popup 组，不 Pause 其它 Default 窗体）。
//------------------------------------------------------------

using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.UI
{
    /// <summary>背包 Tab 热键（常驻组件，由 GameEntry 创建）。</summary>
    public sealed class BackpackHotkey : MonoBehaviour
    {
        private static readonly KeyCode Hotkey = KeyCode.Tab;

        private void Update()
        {
            var procedure = GameEntry.Procedure;
            bool inBattleFlow = procedure != null && procedure.CurrentProcedure is Procedure.ProcedureBattle;
            var open = BackpackForm.FindOpenInstance();

            if (!inBattleFlow)
            {
                var any = BackpackForm.FindInstance();
                if (any != null && any.IsOpen)
                {
                    any.TogglePanel(false, true);   // 离开战斗流程自动收起
                }
                return;
            }

            if (Input.GetKeyDown(Hotkey))
            {
                Toggle();
            }
        }

        /// <summary>打开/关闭背包（供热键与自动化测试调用）。</summary>
        public static void Toggle()
        {
            var open = BackpackForm.FindOpenInstance();
            if (open != null)
            {
                open.TogglePanel(false, true);
                WriteProbe("[backpack] toggle -> close");
                return;
            }

            // 实例已存在但处于收起态：直接展开（无需重新打开窗体）
            var idle = BackpackForm.FindInstance();
            if (idle != null)
            {
                idle.TogglePanel(true, true);
                WriteProbe("[backpack] toggle -> reopen (existing instance)");
                return;
            }

            var ui = GameEntry.UI;
            if (ui == null) { return; }
            if (!ui.HasUIGroup(Constant.UIGroup.Popup))
            {
                ui.AddUIGroup(Constant.UIGroup.Popup, Constant.UIGroup.DepthPopup);   // 弹出层：必须高于商店(Default)与 HUD
            }
            ui.OpenUIForm(Constant.UIFormAssetPath.BackpackForm, Constant.UIGroup.Popup, null);
            WriteProbe("[backpack] toggle -> open");
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
