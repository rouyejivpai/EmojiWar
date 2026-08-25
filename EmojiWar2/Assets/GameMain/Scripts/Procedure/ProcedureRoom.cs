//------------------------------------------------------------
// EmojiWar GameMain - 联机房间流程
// 创建/加入房间后停留在此：显示玩家列表，处理准备/离开。
// 全部玩家准备后由 Host 广播 S2CBattleStart → 所有端同时进入战斗。
// 一局结束后（结算）返回本流程继续下一局。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 联机房间流程：准备 → 全部准备 → 自动开始。
    /// </summary>
    public class ProcedureRoom : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;
        private bool m_Entered = false;
        private bool m_LocalReady = false;
        private bool m_IsHost = false;
        private GameObject m_RoomPlayer = null;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("===== EmojiWar Room =====");
            WriteProbe("[room] OnEnter, isHost=" + (GameEntry.NetworkService != null && GameEntry.NetworkService.Mode == Network.NetMode.Host));

            // 防重复进入
            if (m_Entered)
            {
                WriteProbe("[room] DUPLICATE OnEnter ignored");
                return;
            }
            m_Entered = true;

            m_ProcedureFsm = procedureOwner;
            m_IsHost = GameEntry.NetworkService != null && GameEntry.NetworkService.Mode == Network.NetMode.Host;
            m_LocalReady = false;

            // 房间页：激活菜单场景 + 启用菜单相机（展示可移动的房间玩家）
            SceneCameraHelper.ActivateScene("Menu");
            SpawnRoomPlayer();

            // 订阅事件
            UI.RoomFormEvents.OnReadyRequested += OnReadyRequested;
            UI.RoomFormEvents.OnLeaveRequested += OnLeaveRequested;
            UI.RoomEvents.OnBattleStart += OnBattleStart;
            UI.RoomEvents.OnRoomClosed += OnRoomClosed;
            if (m_IsHost)
            {
                Network.NetHostLogic.OnBattleStartRequested += OnBattleStart;
            }

            OpenRoomForm();
        }

        /// <summary>
        /// 房间页生成本地玩家（可 WASD 移动，等待期间自由活动）。
        /// </summary>
        private void SpawnRoomPlayer()
        {
            GameObject prefab = LoadPrefab("Assets/GameMain/Resources/Entities/Player.prefab");
            if (prefab == null)
            {
                WriteProbe("[room] SpawnRoomPlayer prefab=NULL");
                return;
            }

            m_RoomPlayer = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var player = m_RoomPlayer.GetComponent<Entity.PlayerEntity>();
            var character = GameEntry.Data != null
                ? GameEntry.Data.GetCharacter(ProcedureBattle.SelectedCharacterId)
                : null;
            if (player != null)
            {
                player.MoveSpeed = character != null ? character.MoveSpeed : 5f;
                player.CharacterId = ProcedureBattle.SelectedCharacterId;   // 角色专属美术
                player.NetworkEntityId = GetLocalNetworkEntityId();
            }
            WriteProbe("[room] 房间玩家已生成（可移动），char=" + ProcedureBattle.SelectedCharacterId);
        }

        /// <summary>获取本机网络实体 ID（Host=session0 实体，Client=MyEntityId）。</summary>
        private int GetLocalNetworkEntityId()
        {
            var net = GameEntry.NetworkService;
            if (net == null)
            {
                return -1;
            }

            if (net.Mode == Network.NetMode.Host)
            {
                var hostLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                    : null;
                return hostLogic != null ? hostLogic.GetLocalEntityId() : -1;
            }

            var clientLogic = GameEntry.Instance != null
                ? GameEntry.Instance.GetComponentInChildren<Network.NetClientLogic>()
                : null;
            return clientLogic != null ? clientLogic.MyEntityId : -1;
        }

        /// <summary>销毁房间页玩家（进战斗/离开房间时）。</summary>
        private void DestroyRoomPlayer()
        {
            if (m_RoomPlayer != null)
            {
                Object.Destroy(m_RoomPlayer);
                m_RoomPlayer = null;
            }
        }

        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            const string resourcesMarker = "Resources/";
            int index = path.IndexOf(resourcesMarker, System.StringComparison.Ordinal);
            string resourcesPath = index >= 0 ? path.Substring(index + resourcesMarker.Length) : path;
            resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - ".prefab".Length);
            return Resources.Load<GameObject>(resourcesPath);
#endif
        }

        private void OpenRoomForm()
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

            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.RoomForm, Constant.UIGroup.Default, this);
        }

        /// <summary>准备/取消准备。</summary>
        private void OnReadyRequested()
        {
            m_LocalReady = !m_LocalReady;
            WriteProbe("[room] OnReadyRequested, localReady=" + m_LocalReady + " isHost=" + m_IsHost);

            if (m_IsHost)
            {
                // NetHostLogic 挂在 GameEntry 的子对象上，需用 GetComponentInChildren
                var hostLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                    : null;
                WriteProbe("[room] hostLogic=" + (hostLogic != null ? "OK" : "NULL") + " instance=" + (GameEntry.Instance != null ? "OK" : "NULL"));
                if (hostLogic != null)
                {
                    hostLogic.SetLocalReady(m_LocalReady);
                }
            }
            else
            {
                var clientLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponentInChildren<Network.NetClientLogic>()
                    : null;
                WriteProbe("[room] clientLogic=" + (clientLogic != null ? "OK" : "NULL") + " instance=" + (GameEntry.Instance != null ? "OK" : "NULL"));
                if (clientLogic != null)
                {
                    clientLogic.SendReady(m_LocalReady);
                }
            }

            UI.RoomEvents.LocalReadyChanged(m_LocalReady);
        }

        /// <summary>离开/解散房间。</summary>
        private void OnLeaveRequested()
        {
            Log.Info("[ProcedureRoom] 离开/解散房间");

            if (GameEntry.NetworkService != null)
            {
                GameEntry.NetworkService.Shutdown();
            }

            var clientLogic = GameEntry.Instance != null
                ? GameEntry.Instance.GetComponentInChildren<Network.NetClientLogic>()
                : null;
            if (clientLogic != null)
            {
                clientLogic.LeaveRoom();
            }

            // 回大厅（菜单场景常驻，直接切流程）
            UI.UIFormCloser.CloseByName("RoomForm(Clone)");
            ChangeState<ProcedureLobby>(m_ProcedureFsm);
        }

        /// <summary>全部准备 → 开始战斗（Host 广播或本机触发）。</summary>
        private void OnBattleStart()
        {
            Log.Info("[ProcedureRoom] 战斗开始，进入战斗流程");
            WriteProbe("[room] BattleStart -> 进入战斗");
            ChangeState<ProcedureBattle>(m_ProcedureFsm);
        }

        /// <summary>房主退出/连接断开 → 返回大厅。</summary>
        private void OnRoomClosed()
        {
            Log.Info("[ProcedureRoom] 房间解散/断开，返回大厅");
            WriteProbe("[room] RoomClosed -> 返回大厅");

            DestroyRoomPlayer();

            if (m_Entered)
            {
                m_Entered = false;
            }

            UI.UIFormCloser.CloseByName("RoomForm(Clone)");
            ChangeState<ProcedureLobby>(m_ProcedureFsm);
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UI.RoomFormEvents.OnReadyRequested -= OnReadyRequested;
            UI.RoomFormEvents.OnLeaveRequested -= OnLeaveRequested;
            UI.RoomEvents.OnBattleStart -= OnBattleStart;
            UI.RoomEvents.OnRoomClosed -= OnRoomClosed;
            if (m_IsHost)
            {
                Network.NetHostLogic.OnBattleStartRequested -= OnBattleStart;
            }

            // 离开房间流程时关闭房间 UI（避免遮挡战斗场景）
            UI.UIFormCloser.CloseByName("RoomForm(Clone)");
            DestroyRoomPlayer();

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
