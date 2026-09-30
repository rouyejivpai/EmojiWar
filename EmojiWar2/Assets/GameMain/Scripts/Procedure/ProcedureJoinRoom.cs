//------------------------------------------------------------
// EmojiWar GameMain - 加入游戏流程（局域网房间列表）
// 多人游戏页点"加入游戏"进入本流程：打开 JoinListForm。
//   - 列表项（房间按钮）点击 → 连接该房主 + 加入 → ChangeState<ProcedureRoom>
//   - 返回 → ChangeState<ProcedureMultiplayer>
// 房间来源：RoomDiscovery（UDP 局域网广播发现；房主在房间阶段应答）。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 加入游戏流程：局域网房间列表 + 刷新。
    /// </summary>
    public class ProcedureJoinRoom : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;
        private bool m_Entered = false;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar JoinRoom =====");
            WriteProbe("[joinroom] OnEnter");

            if (m_Entered)
            {
                WriteProbe("[joinroom] DUPLICATE OnEnter ignored");
                return;
            }
            m_Entered = true;
            m_ProcedureFsm = procedureOwner;

            // 场景：菜单场景共用
            SceneCameraHelper.ActivateScene("Menu");

            // 订阅
            UI.JoinListEvents.OnJoinRoomRequested += OnJoinRoomRequested;
            UI.JoinListEvents.OnBackRequested += OnBackRequested;

            OpenJoinListForm();
        }

        private void OpenJoinListForm()
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
            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.JoinListForm, Constant.UIGroup.Default, this);
        }

        /// <summary>点房间项：连接房主并加入（TCP 直连 + 帧同步）。</summary>
        private void OnJoinRoomRequested(string hostName, string ip, int port)
        {
            string playerName = ProcedureMultiplayer.PlayerName;
            WriteProbe("[joinroom] 加入房间 " + hostName + " @ " + ip + ":" + port + " player=" + playerName);
            Log.Info("[ProcedureJoinRoom] 加入房间: {0} @ {1}:{2}", hostName, ip, port);

            GameEntry.NetworkService.ConnectToServer(ip, port);

            var clientLogic = GetOrAddClientLogic();
            if (clientLogic != null)
            {
                clientLogic.JoinRoom(playerName, ip, port);
            }

            GoRoom();
        }

        private void OnBackRequested()
        {
            WriteProbe("[joinroom] 返回多人游戏");
            ChangeState<ProcedureMultiplayer>(m_ProcedureFsm);
        }

        private void GoRoom()
        {
            WriteProbe("[joinroom] 进入房间流程");
            ChangeState<ProcedureRoom>(m_ProcedureFsm);
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

            var go = new GameObject("NetClientLogic");
            go.transform.SetParent(instance.transform);
            var logic = go.AddComponent<Network.NetClientLogic>();
            logic.Bind(GameEntry.NetworkService);
            if (!AutoPlay.IsActive)
            {
                logic.DisableAutoMove();
            }
            return logic;
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.JoinListEvents.OnJoinRoomRequested -= OnJoinRoomRequested;
            UI.JoinListEvents.OnBackRequested -= OnBackRequested;

            UI.UIFormCloser.CloseByName("JoinListForm(Clone)");

            // 离开本流程：停止扫描，释放发现 socket（返回多人页/进房间前清理）
            Network.RoomDiscovery.Shutdown();

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
