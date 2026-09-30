//------------------------------------------------------------
// EmojiWar GameMain - 多人游戏流程
// 主界面点"多人游戏"进入本流程：打开 MultiplayerForm（玩家名 + 创建房间/加入游戏）。
//   - 创建房间 → StartHost + 本地加入 → ChangeState<ProcedureRoom>
//   - 加入游戏 → ChangeState<ProcedureJoinRoom>（局域网房间列表）
//   - 返回 → ChangeState<ProcedureMenu>
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 多人游戏流程：创建房间 / 加入游戏入口。
    /// </summary>
    public class ProcedureMultiplayer : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;
        private bool m_Entered = false;

        /// <summary>本流程玩家名（创建/加入共用；打开时从 MultiplayerForm 读取）。</summary>
        public static string PlayerName { get; private set; } = "玩家";

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Multiplayer =====");
            WriteProbe("[multiplayer] OnEnter");

            if (m_Entered)
            {
                WriteProbe("[multiplayer] DUPLICATE OnEnter ignored");
                return;
            }
            m_Entered = true;
            m_ProcedureFsm = procedureOwner;

            // 场景：菜单/大厅/房间共用 Menu 场景
            SceneCameraHelper.ActivateScene("Menu");

            // 订阅事件
            UI.MultiplayerEvents.OnCreateRoomRequested += OnCreateRoomRequested;
            UI.MultiplayerEvents.OnJoinGameRequested += OnJoinGameRequested;
            UI.MultiplayerEvents.OnBackRequested += OnBackRequested;

            OpenMultiplayerForm();
        }

        private void OpenMultiplayerForm()
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
            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.MultiplayerForm, Constant.UIGroup.Default, this);
        }

        /// <summary>创建房间：启动 Host 服务器 + 房主本机加入 → 房间页。</summary>
        private void OnCreateRoomRequested(string playerName)
        {
            PlayerName = string.IsNullOrEmpty(playerName) ? "房主" : playerName;
            WriteProbe("[multiplayer] 创建房间 player=" + PlayerName);
            Log.Info("[ProcedureMultiplayer] 创建房间: {0}", PlayerName);

            bool started = GameEntry.NetworkService.StartHost();
            if (!started)
            {
                Log.Error("[ProcedureMultiplayer] 服务器启动失败");
                return;
            }

            // Host 本机加入（房主名 = MultiplayerForm 玩家名）
            var hostLogic = GetOrAddHostLogic();
            hostLogic.JoinLocal(PlayerName, ProcedureBattle.SelectedCharacterId);

            GoRoom();
        }

        /// <summary>加入游戏：进入房间列表流程（局域网发现）。</summary>
        private void OnJoinGameRequested()
        {
            var form = GameEntry.UI != null
                ? FindMultiplayerForm()
                : null;
            if (form != null)
            {
                PlayerName = string.IsNullOrEmpty(form.GetPlayerName()) ? "玩家" : form.GetPlayerName();
            }
            WriteProbe("[multiplayer] 加入游戏 player=" + PlayerName);
            Log.Info("[ProcedureMultiplayer] 加入游戏（房间列表）");
            ChangeState<ProcedureJoinRoom>(m_ProcedureFsm);
        }

        private void OnBackRequested()
        {
            WriteProbe("[multiplayer] 返回主菜单");
            ChangeState<ProcedureMenu>(m_ProcedureFsm);
        }

        private void GoRoom()
        {
            WriteProbe("[multiplayer] 进入房间流程");
            ChangeState<ProcedureRoom>(m_ProcedureFsm);
        }

        private UI.MultiplayerForm FindMultiplayerForm()
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    return null;
                }
                foreach (var g in ui.GetAllUIGroups())
                {
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            var form = logic.Logic as UI.MultiplayerForm;
                            if (form != null)
                            {
                                return form;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private Network.NetHostLogic GetOrAddHostLogic()
        {
            var instance = GameEntry.Instance;
            if (instance == null)
            {
                return null;
            }

            var existing = instance.GetComponentInChildren<Network.NetHostLogic>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("NetHostLogic");
            go.transform.SetParent(instance.transform);
            var logic = go.AddComponent<Network.NetHostLogic>();
            logic.Bind(GameEntry.NetworkService);
            if (!AutoPlay.IsActive)
            {
                logic.DisableLocalAutoMove();
            }
            return logic;
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.MultiplayerEvents.OnCreateRoomRequested -= OnCreateRoomRequested;
            UI.MultiplayerEvents.OnJoinGameRequested -= OnJoinGameRequested;
            UI.MultiplayerEvents.OnBackRequested -= OnBackRequested;

            UI.UIFormCloser.CloseByName("MultiplayerForm(Clone)");

            m_Entered = false;
            m_ProcedureFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
        }

        /// <summary>运行时探针。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
