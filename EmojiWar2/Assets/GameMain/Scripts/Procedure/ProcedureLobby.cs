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

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Lobby =====");

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

            GoBattle();
        }

        private void OnJoinRoomRequested(string playerName, string ip, int port)
        {
            m_PlayerName = playerName;
            Log.Info("[ProcedureLobby] 加入房间: {0} @ {1}:{2}", playerName, ip, port);

            GameEntry.NetworkService.ConnectToServer(ip, port);
            GoBattle();
        }

        private void OnBackRequested()
        {
            Log.Info("[ProcedureLobby] 返回主菜单");
            ChangeState<ProcedureMenu>(m_ProcedureFsm);
        }

        private void GoBattle()
        {
            Log.Info("[ProcedureLobby] 进入战斗");
            ChangeState<ProcedureBattle>(m_ProcedureFsm);
        }

        private Network.NetHostLogic GetOrAddHostLogic()
        {
            // 在 GameEntry 下挂载 Host 逻辑（服务器权威模拟）
            var instance = GameEntry.Instance;
            if (instance == null)
            {
                return null;
            }

            var existing = instance.GetComponent<Network.NetHostLogic>();
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
            m_ProcedureFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}
