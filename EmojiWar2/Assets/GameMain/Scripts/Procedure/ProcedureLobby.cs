//------------------------------------------------------------
// EmojiWar GameMain - 大厅流程
// 打开大厅 UI，处理创建/加入房间请求。
// 创建 = Host 模式 + 启动服务器 + 加入房间
// 加入 = Client 模式 + 连接服务器 + 加入房间
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 大厅流程：多人房间的创建与加入。
    /// </summary>
    public class ProcedureLobby : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;
        private string m_PlayerName = "玩家";
        private bool m_Entered = false;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Lobby =====");

            // 防重复进入（重复 ChangeState 会重复订阅事件，导致创建/加入被触发两次）
            if (m_Entered)
            {
                WriteProbe("[lobby] DUPLICATE OnEnter ignored");
                return;
            }
            m_Entered = true;

            m_ProcedureFsm = procedureOwner;

            UI.LobbyForm.OnCreateRoomRequested += OnCreateRoomRequested;
            UI.LobbyForm.OnJoinRoomRequested += OnJoinRoomRequested;
            UI.LobbyForm.OnBackRequested += OnBackRequested;

            OpenLobbyForm();
        }

        private void OpenLobbyForm()
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

            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.LobbyForm, Constant.UIGroup.Default, this);
        }

        private void OnCreateRoomRequested(string playerName)
        {
            WriteProbe("[lobby] OnCreateRoomRequested player=" + playerName);
            m_PlayerName = playerName;
            Log.Info("[ProcedureLobby] 创建房间: {0}", playerName);

            // 启动 Host 服务器
            bool started = GameEntry.NetworkService.StartHost();
            if (!started)
            {
                Log.Error("[ProcedureLobby] 服务器启动失败");
                return;
            }

            // Host 也需要作为玩家加入（本地加入）
            var hostLogic = GetOrAddHostLogic();
            hostLogic.JoinLocal(m_PlayerName);

            GoRoom();
        }

        private void OnJoinRoomRequested(string playerName, string ip, int port)
        {
            m_PlayerName = playerName;
            Log.Info("[ProcedureLobby] 加入房间: {0} @ {1}:{2}", playerName, ip, port);

            GameEntry.NetworkService.ConnectToServer(ip, port);

            // 挂载客户端同步逻辑并排队发送加入房间请求（连接建立后自动补发）
            var clientLogic = GetOrAddClientLogic();
            if (clientLogic != null)
            {
                clientLogic.JoinRoom(playerName, ip, port);
            }

            GoRoom();
        }

        private void GoRoom()
        {
            WriteProbe("[lobby] 进入房间流程（不再直接进战斗）");
            Log.Info("[ProcedureLobby] 进入房间");
            ChangeState<ProcedureRoom>(m_ProcedureFsm);
        }

        private void OnBackRequested()
        {
            Log.Info("[ProcedureLobby] 返回主菜单");

            // 离开房间：停止服务器 / 断开连接
            if (GameEntry.NetworkService != null)
            {
                GameEntry.NetworkService.Shutdown();
            }
            var clientLogic = GetOrAddClientLogic();
            if (clientLogic != null)
            {
                clientLogic.LeaveRoom();
            }

            ChangeState<ProcedureMenu>(m_ProcedureFsm);
        }

        private Network.NetClientLogic GetOrAddClientLogic()
        {
            var instance = GameEntry.Instance;
            if (instance == null)
            {
                return null;
            }

            var existing = instance.GetComponentInChildren<Network.NetClientLogic>();
            if (existing != null)
            {
                return existing;
            }

            var go = new UnityEngine.GameObject("NetClientLogic");
            go.transform.SetParent(instance.transform);
            var logic = go.AddComponent<Network.NetClientLogic>();
            logic.Bind(GameEntry.NetworkService);
            logic.DisableAutoMove();    // 真实联机：静止时不上行绕圈移动
            return logic;
        }

        private Network.NetHostLogic GetOrAddHostLogic()
        {
            // 在 GameEntry 下挂载 Host 逻辑（服务器权威模拟）
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

            var go = new UnityEngine.GameObject("NetHostLogic");
            go.transform.SetParent(instance.transform);
            var logic = go.AddComponent<Network.NetHostLogic>();
            logic.Bind(GameEntry.NetworkService);
            return logic;
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.LobbyForm.OnCreateRoomRequested -= OnCreateRoomRequested;
            UI.LobbyForm.OnJoinRoomRequested -= OnJoinRoomRequested;
            UI.LobbyForm.OnBackRequested -= OnBackRequested;

            // 关闭大厅窗体，避免重复进入时累积
            UI.UIFormCloser.CloseByName("LobbyForm(Clone)");

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
