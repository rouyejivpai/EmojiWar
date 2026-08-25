//------------------------------------------------------------
// EmojiWar GameMain - 角色选择流程
// 主菜单 → 角色选择 → 大厅（创建/加入房间）。
// 选择结果影响本地战斗层的角色（HP/速度/默认武器）。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 角色选择流程。
    /// </summary>
    public class ProcedureCharacterSelect : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;
        private bool m_Entered = false;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar CharacterSelect =====");

            if (m_Entered)
            {
                return;
            }
            m_Entered = true;

            m_ProcedureFsm = procedureOwner;
            UI.CharacterSelectEvents.OnCharacterSelected += OnCharacterSelected;

            OpenCharacterSelectForm();
        }

        private void OpenCharacterSelectForm()
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

            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.CharacterSelectForm, Constant.UIGroup.Default, this);
        }

        private void OnCharacterSelected(int characterId)
        {
            Log.Info("[ProcedureCharacterSelect] 选择角色: {0}", characterId);
            WriteProbe("[charselect] 选择角色 id=" + characterId);

            ProcedureBattle.SelectedCharacterId = characterId;

            ChangeState<ProcedureLobby>(m_ProcedureFsm);
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.CharacterSelectEvents.OnCharacterSelected -= OnCharacterSelected;
            UI.UIFormCloser.CloseByName("CharacterSelectForm(Clone)");
            m_Entered = false;
            m_ProcedureFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
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
