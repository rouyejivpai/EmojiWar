//------------------------------------------------------------
// EmojiWar GameMain - Host 权威逻辑（服务器端）
// 维护所有玩家/实体的权威状态：
//   收到 C2SPlayerInput → 更新玩家位置 → 广播 S2CEntityState
// 波次/敌人/掉落均由 Host 驱动（多人合作 PvE 核心）。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// Host 端权威状态：服务器模拟逻辑。
    /// </summary>
    public class NetHostLogic : MonoBehaviour
    {
        // 玩家权威状态（sessionId → 状态）
        private sealed class PlayerState
        {
            public int SessionId;
            public int EntityId;
            public Vector2 Position;
            public float Hp = 100f;
        }

        private readonly Dictionary<int, PlayerState> m_Players = new Dictionary<int, PlayerState>();
        private int m_NextEntityId = 1000;

        private NetworkService m_Service = null;

        private void Awake()
        {
            // 默认绑定 GameEntry 的服务；测试可通过 Bind 覆盖
            m_Service = GameEntry.NetworkService;
        }

        /// <summary>
        /// 绑定网络服务（测试/多实例场景使用）。
        /// </summary>
        public void Bind(NetworkService service)
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage -= OnClientMessage;
                m_Service.OnModeChanged -= OnModeChanged;
            }
            m_Service = service;
            if (m_Service != null)
            {
                m_Service.OnClientMessage += OnClientMessage;
                m_Service.OnModeChanged += OnModeChanged;
            }
        }

        private void OnEnable()
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage += OnClientMessage;
                m_Service.OnModeChanged += OnModeChanged;
            }
        }

        private void OnDisable()
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage -= OnClientMessage;
                m_Service.OnModeChanged -= OnModeChanged;
            }
        }

        private void OnModeChanged(NetMode mode)
        {
            if (mode == NetMode.Host)
            {
                m_Players.Clear();
                Debug.Log("[NetHostLogic] Host 模式就绪，等待玩家加入");
            }
        }

        /// <summary>
        /// 处理客户端消息。
        /// </summary>
        private void OnClientMessage(int sessionId, NetMessage message)
        {
            switch (message.Id)
            {
                case MsgId.JoinRoom:
                    HandleJoin(sessionId, message as C2SJoinRoom);
                    break;

                case MsgId.PlayerInput:
                    HandleInput(sessionId, message as C2SPlayerInput);
                    break;

                case MsgId.LeaveRoom:
                    HandleLeave(sessionId);
                    break;
            }
        }

        private void HandleJoin(int sessionId, C2SJoinRoom join)
        {
            if (join == null)
            {
                return;
            }

            var state = new PlayerState
            {
                SessionId = sessionId,
                EntityId = m_NextEntityId++,
                Position = new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)),
            };
            m_Players[sessionId] = state;

            // 通知所有客户端：新玩家加入 + 生成实体
            var joined = new S2CPlayerJoined { PlayerId = sessionId, PlayerName = join.PlayerName };
            m_Service.BroadcastToClients(joined);

            var spawn = new S2CSpawnEntity
            {
                EntityId = state.EntityId,
                Type = 0,
                Team = 1,
                X = state.Position.x,
                Y = state.Position.y,
            };
            m_Service.BroadcastToClients(spawn);

            // 房间状态
            var room = new S2CRoomState { RoomId = "ROOM-001", PlayerCount = m_Players.Count };
            m_Service.BroadcastToClients(room);

            Debug.Log(string.Format("[NetHostLogic] 玩家 {0}({1}) 加入，生成实体 {2}",
                join.PlayerName, sessionId, state.EntityId));
        }

        private void HandleInput(int sessionId, C2SPlayerInput input)
        {
            if (input == null || !m_Players.TryGetValue(sessionId, out var state))
            {
                return;
            }

            // Host 权威：根据输入更新位置
            Vector2 moveDir = new Vector2(input.InputX, input.InputY);
            if (moveDir.sqrMagnitude > 1f)
            {
                moveDir = moveDir.normalized;
            }

            const float speed = 5f;
            state.Position += moveDir * speed * Time.deltaTime;

            // 广播状态给所有客户端
            var entityState = new S2CEntityState
            {
                EntityId = state.EntityId,
                X = state.Position.x,
                Y = state.Position.y,
                Hp = state.Hp,
            };
            m_Service.BroadcastToClients(entityState);
        }

        private void HandleLeave(int sessionId)
        {
            if (m_Players.Remove(sessionId))
            {
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = 0 });
                Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 离开");
            }
        }
    }
}
