//------------------------------------------------------------
// EmojiWar GameMain - 客户端同步逻辑
// 每帧上行输入（C2SPlayerInput），应用下行状态（S2CEntityState）。
// 支持：实体移除、玩家离开、断线自动重连。
// 客户端只负责输入与表现，权威状态在 Host。
//------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 客户端同步：输入上行 + 状态应用 + 重连。
    /// </summary>
    public class NetClientLogic : MonoBehaviour
    {
        // 本地实体映射（entityId → 本地对象）
        private readonly Dictionary<int, Transform> m_LocalEntities = new Dictionary<int, Transform>();

        private NetworkService m_Service = null;
        private bool m_Joined = false;
        private string m_PlayerName = "玩家";
        private string m_ServerIp = "127.0.0.1";
        private int m_ServerPort = NetworkService.DefaultPort;

        // 重连状态
        private bool m_Reconnecting = false;
        private float m_ReconnectDelay = 1f;
        private float m_ReconnectTimer = 0f;

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
                m_Service.OnModeChanged -= OnModeChanged;
            }
            m_Service = service;
            if (m_Service != null)
            {
                m_Service.OnServerMessage += OnServerMessage;
                m_Service.OnModeChanged += OnModeChanged;
            }
        }

        private void OnEnable()
        {
            if (m_Service != null)
            {
                m_Service.OnServerMessage += OnServerMessage;
                m_Service.OnModeChanged += OnModeChanged;
            }
        }

        private void OnDisable()
        {
            if (m_Service != null)
            {
                m_Service.OnServerMessage -= OnServerMessage;
                m_Service.OnModeChanged -= OnModeChanged;
            }
        }

        private void OnModeChanged(NetMode mode)
        {
            if (mode == NetMode.Offline && m_Joined)
            {
                Debug.Log("[NetClientLogic] 连接断开，启动重连");
                StartReconnect();
            }
        }

        /// <summary>
        /// 加入房间（连接建立后调用）。
        /// </summary>
        public void JoinRoom(string playerName, string serverIp = null, int port = -1)
        {
            m_PlayerName = playerName;
            if (!string.IsNullOrEmpty(serverIp))
            {
                m_ServerIp = serverIp;
            }
            if (port > 0)
            {
                m_ServerPort = port;
            }

            m_Joined = true;
            m_Service.Send(new C2SJoinRoom { PlayerName = m_PlayerName });
            Debug.Log("[NetClientLogic] 发送加入房间请求: " + m_PlayerName);
        }

        private void Update()
        {
            if (m_Reconnecting)
            {
                UpdateReconnect();
            }

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

        // ==================== 重连 ====================

        private void StartReconnect()
        {
            if (m_Reconnecting)
            {
                return;
            }
            m_Reconnecting = true;
            m_ReconnectTimer = m_ReconnectDelay;
            ClearLocalEntities();
            Debug.Log("[NetClientLogic] 2 秒后自动重连 " + m_ServerIp + ":" + m_ServerPort);
        }

        private void UpdateReconnect()
        {
            m_ReconnectTimer -= Time.deltaTime;
            if (m_ReconnectTimer > 0f)
            {
                return;
            }

            // 重连
            if (m_Service != null)
            {
                m_Service.ConnectToServer(m_ServerIp, m_ServerPort);
                m_ReconnectTimer = m_ReconnectDelay;

                // 短暂等待连接后重新加入
                StartCoroutine(RejoinAfterConnect());
            }
        }

        private IEnumerator RejoinAfterConnect()
        {
            float wait = 1.5f;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (m_Service != null && m_Service.IsConnected)
            {
                Debug.Log("[NetClientLogic] 重连成功，重新加入房间");
                m_Service.Send(new C2SJoinRoom { PlayerName = m_PlayerName });
                m_Reconnecting = false;
            }
        }

        /// <summary>
        /// 清理本地实体（重连/离开时）。
        /// </summary>
        public void ClearLocalEntities()
        {
            foreach (var kv in m_LocalEntities)
            {
                if (kv.Value != null)
                {
                    Destroy(kv.Value.gameObject);
                }
            }
            m_LocalEntities.Clear();
        }

        // ==================== 消息处理 ====================

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

                case MsgId.PlayerLeft:
                    var left = message as S2CPlayerLeft;
                    Debug.Log(string.Format("[NetClientLogic] 玩家 {0} 离开", left.PlayerId));
                    break;

                case MsgId.SpawnEntity:
                    HandleSpawn(message as S2CSpawnEntity);
                    break;

                case MsgId.EntityState:
                    HandleEntityState(message as S2CEntityState);
                    break;

                case MsgId.RemoveEntity:
                    HandleRemoveEntity(message as S2CRemoveEntity);
                    break;

                case MsgId.ShopOffer:
                    HandleShopOffer(message as S2CShopOffer);
                    break;
            }
        }

        /// <summary>最近一次商店商品（测试/UI 用）。</summary>
        public string LastShopOffer { get; private set; } = string.Empty;

        /// <summary>是否收到商店商品。</summary>
        public bool GotShopOffer { get; private set; } = false;

        private void HandleShopOffer(S2CShopOffer offer)
        {
            if (offer == null)
            {
                return;
            }

            LastShopOffer = offer.Items;
            GotShopOffer = true;
            Debug.Log("[NetClientLogic] 收到商店商品: " + offer.Items);
        }

        /// <summary>
        /// 发送购买请求。
        /// </summary>
        public void RequestBuy(int shopItemIndex)
        {
            if (m_Service != null)
            {
                m_Service.Send(new C2SBuyItem { ShopItemIndex = shopItemIndex });
                Debug.Log("[NetClientLogic] 发送购买请求: " + shopItemIndex);
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

        private void HandleRemoveEntity(S2CRemoveEntity remove)
        {
            if (remove == null)
            {
                return;
            }

            if (m_LocalEntities.TryGetValue(remove.EntityId, out var tf))
            {
                if (tf != null)
                {
                    Destroy(tf.gameObject);
                }
                m_LocalEntities.Remove(remove.EntityId);
                Debug.Log("[NetClientLogic] 实体 " + remove.EntityId + " 移除");
            }
        }

        /// <summary>当前实体数量（测试用）。</summary>
        public int LocalEntityCount
        {
            get { return m_LocalEntities.Count; }
        }

        /// <summary>是否正在重连。</summary>
        public bool IsReconnecting
        {
            get { return m_Reconnecting; }
        }
    }
}
