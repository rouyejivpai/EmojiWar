//------------------------------------------------------------
// EmojiWar GameMain - 客户端同步逻辑
// 每帧上行输入（C2SPlayerInput），应用下行状态（S2CEntityState）。
// 客户端只负责输入与表现，权威状态在 Host。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 客户端同步：输入上行 + 状态应用。
    /// </summary>
    public class NetClientLogic : MonoBehaviour
    {
        // 本地实体映射（entityId → 本地对象）
        private readonly Dictionary<int, Transform> m_LocalEntities = new Dictionary<int, Transform>();

        private NetworkService m_Service = null;
        private bool m_Joined = false;

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
                m_Service.OnServerMessage -= OnServerMessage;
            }
            m_Service = service;
            if (m_Service != null)
            {
                m_Service.OnServerMessage += OnServerMessage;
            }
        }

        private void OnEnable()
        {
            if (m_Service != null)
            {
                m_Service.OnServerMessage += OnServerMessage;
            }
        }

        private void OnDisable()
        {
            if (m_Service != null)
            {
                m_Service.OnServerMessage -= OnServerMessage;
            }
        }

        /// <summary>
        /// 加入房间（连接建立后调用）。
        /// </summary>
        public void JoinRoom(string playerName)
        {
            m_Joined = true;
            m_Service.Send(new C2SJoinRoom { PlayerName = playerName });
            Debug.Log("[NetClientLogic] 发送加入房间请求: " + playerName);
        }

        private void Update()
        {
            if (!m_Joined || m_Service == null || m_Service.Mode != NetMode.Client)
            {
                return;
            }

            // 上行输入（测试：自动移动；正式：读取真实输入）
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

            // 无键盘输入时自动转圈（便于回环验证）
            if (inputX == 0f && inputY == 0f)
            {
                inputX = Mathf.Cos(Time.time);
                inputY = Mathf.Sin(Time.time);
            }

            var input = new C2SPlayerInput
            {
                InputX = inputX,
                InputY = inputY,
                AimX = 1f,
                AimY = 0f,
                FirePrimary = false,
                FireSecondary = false,
            };
            m_Service.Send(input);
        }

        /// <summary>
        /// 处理服务器消息。
        /// </summary>
        private void OnServerMessage(NetMessage message)
        {
            switch (message.Id)
            {
                case MsgId.RoomState:
                    var room = message as S2CRoomState;
                    Debug.Log(string.Format("[NetClientLogic] 房间 {0}，玩家数 {1}", room.RoomId, room.PlayerCount));
                    break;

                case MsgId.PlayerJoined:
                    var joined = message as S2CPlayerJoined;
                    Debug.Log(string.Format("[NetClientLogic] 玩家 {0} 加入", joined.PlayerName));
                    break;

                case MsgId.SpawnEntity:
                    HandleSpawn(message as S2CSpawnEntity);
                    break;

                case MsgId.EntityState:
                    HandleEntityState(message as S2CEntityState);
                    break;

                case MsgId.RemoveEntity:
                    Debug.Log("[NetClientLogic] 实体移除");
                    break;
            }
        }

        private void HandleSpawn(S2CSpawnEntity spawn)
        {
            if (spawn == null || m_LocalEntities.ContainsKey(spawn.EntityId))
            {
                return;
            }

            // 生成本地表现对象（占位方块）
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "NetEntity_" + spawn.EntityId;
            go.transform.position = new Vector3(spawn.X, spawn.Y, 0f);
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                // 玩家绿色，敌人红色
                renderer.material.color = spawn.Type == 0 ? Color.green : new Color(0.9f, 0.2f, 0.2f);
            }
            m_LocalEntities[spawn.EntityId] = go.transform;

            Debug.Log(string.Format("[NetClientLogic] 生成实体 {0} type={1} 于 ({2:F1},{3:F1})", spawn.EntityId, spawn.Type, spawn.X, spawn.Y));
        }

        private void HandleEntityState(S2CEntityState state)
        {
            if (state == null || !m_LocalEntities.TryGetValue(state.EntityId, out var tf))
            {
                return;
            }

            tf.position = new Vector3(state.X, state.Y, 0f);
        }

        /// <summary>当前实体数量（测试用）。</summary>
        public int LocalEntityCount
        {
            get { return m_LocalEntities.Count; }
        }
    }
}
