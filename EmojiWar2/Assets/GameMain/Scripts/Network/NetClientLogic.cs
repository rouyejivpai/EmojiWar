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
    /// 实体位置使用插值平滑（减少网络抖动）。
    /// </summary>
    public class NetClientLogic : MonoBehaviour
    {
        // 本地实体表现
        private sealed class LocalEntity
        {
            public Transform Transform;
            public Vector3 TargetPosition;
        }

        private readonly Dictionary<int, LocalEntity> m_LocalEntities = new Dictionary<int, LocalEntity>();

        [SerializeField]
        private float m_InterpolationSpeed = 12f;   // 插值速度（越高越快跟随）

        /// <summary>
        /// 无输入时是否自动绕圈移动（回环测试用；正式联机时由流程关闭）。
        /// </summary>
        [SerializeField]
        private bool m_AutoMoveWhenIdle = true;

        /// <summary>关闭空闲自动移动（真实联机玩家静止时不应绕圈）。</summary>
        public void DisableAutoMove()
        {
            m_AutoMoveWhenIdle = false;
        }

        private NetworkService m_Service = null;
        private bool m_Joined = false;
        private bool m_JoinSent = false;
        private bool m_IntentionalLeave = false;

        /// <summary>加入确认（收到 S2CMyEntity 视为加入成功）；超时未确认则重发 JoinRoom。</summary>
        private bool m_JoinConfirmed = false;
        private float m_JoinSendTime = 0f;
        private const float JoinConfirmTimeout = 3f;

        /// <summary>自己的网络实体 ID（Host 告知；客户端不渲染自己，避免与本地玩家重复）。</summary>
        private int m_MyEntityId = -1;
        private string m_PlayerName = "玩家";
        private string m_ServerIp = "127.0.0.1";
        private int m_ServerPort = NetworkService.DefaultPort;

        // 重连状态：服务器未就绪时持续重试（间隔 2s），不设死上限（用户可能先开加入者后开房主）
        private bool m_Reconnecting = false;
        private float m_ReconnectDelay = 2f;
        private float m_ReconnectTimer = 0f;
        private int m_ReconnectAttempts = 0;

        /// <summary>重连状态变化事件（参数：是否重连中）。</summary>
        public event System.Action<bool> OnReconnectStateChanged;

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
            WriteProbe("[net] OnModeChanged -> " + mode + " joined=" + m_Joined + " intentionalLeave=" + m_IntentionalLeave);
            if (mode == NetMode.Offline && m_Joined && !m_IntentionalLeave)
            {
                // 连接断开（房主退出/网络中断）：通知流程返回大厅，停止重连
                Debug.Log("[NetClientLogic] 连接断开（房主退出），返回大厅");
                WriteProbe("[net] 连接断开，返回大厅");
                m_IntentionalLeave = true;
                m_Joined = false;
                m_Reconnecting = false;
                ClearLocalEntities();
                UI.RoomEvents.RoomClosed();
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
            m_JoinSent = false;
            WriteProbe("[net] JoinRoom 排队: " + m_PlayerName + " @ " + m_ServerIp + ":" + m_ServerPort);
            Debug.Log("[NetClientLogic] 加入房间请求已排队，连接建立后发送: " + m_PlayerName);
        }

        /// <summary>
        /// 发送准备/取消准备请求。
        /// </summary>
        public void SendReady(bool ready)
        {
            if (m_Service != null)
            {
                m_Service.Send(new C2SReadyChange { Ready = ready });
                WriteProbe("[net] 发送准备状态: " + ready);
            }
        }

        /// <summary>
        /// 主动离开房间：停止重连并清理本地实体。
        /// </summary>
        public void LeaveRoom()
        {
            m_IntentionalLeave = true;
            m_Joined = false;
            m_Reconnecting = false;
            ClearLocalEntities();
            Debug.Log("[NetClientLogic] 主动离开房间");
        }

        private void Update()
        {
            if (m_Reconnecting)
            {
                UpdateReconnect();
            }

            // 插值平滑所有实体（每帧向目标位置移动）
            UpdateInterpolation();

            if (!m_Joined || m_Service == null || m_Service.Mode != NetMode.Client)
            {
                return;
            }

            // 连接建立后补发加入房间请求（异步连接时序）
            if (!m_JoinSent && m_Service.IsConnected)
            {
                m_JoinSent = true;
                m_JoinConfirmed = false;
                m_JoinSendTime = Time.realtimeSinceStartup;
                m_Service.Send(new C2SJoinRoom { PlayerName = m_PlayerName });
                WriteProbe("[net] 发送 C2SJoinRoom: " + m_PlayerName);
                Debug.Log("[NetClientLogic] 发送加入房间请求: " + m_PlayerName);
            }

            // 加入未确认（可能被连接时序丢弃）：3 秒后重发
            if (m_JoinSent && !m_JoinConfirmed && Time.realtimeSinceStartup - m_JoinSendTime > JoinConfirmTimeout)
            {
                m_JoinSent = false;
                WriteProbe("[net] JoinRoom 3 秒未确认，重发");
                Debug.Log("[NetClientLogic] JoinRoom 未确认，重发");
            }

            // 上行输入（测试：自动移动；正式：读取真实输入）
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

            // 无键盘输入时自动转圈（回环验证用；真实联机由 DisableAutoMove 关闭）
            if (m_AutoMoveWhenIdle && inputX == 0f && inputY == 0f)
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
            OnReconnectStateChanged?.Invoke(true);
            Debug.Log("[NetClientLogic] 2 秒后自动重连 " + m_ServerIp + ":" + m_ServerPort);
        }

        private void UpdateReconnect()
        {
            m_ReconnectTimer -= Time.deltaTime;
            if (m_ReconnectTimer > 0f)
            {
                return;
            }

            // 无限重试（服务器未就绪时持续等）；每 20 次在 probe 里记录一次，避免刷屏
            m_ReconnectAttempts++;
            if (m_ReconnectAttempts % 20 == 0)
            {
                WriteProbe("[net] 持续重连中... 第 " + m_ReconnectAttempts + " 次");
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
                m_ReconnectAttempts = 0;
                m_JoinSent = true;
                m_Service.Send(new C2SJoinRoom { PlayerName = m_PlayerName });
                m_Reconnecting = false;
                OnReconnectStateChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// 清理本地实体（重连/离开时）。
        /// </summary>
        public void ClearLocalEntities()
        {
            foreach (var kv in m_LocalEntities)
            {
                if (kv.Value.Transform != null)
                {
                    Destroy(kv.Value.Transform.gameObject);
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

                case MsgId.RunRestart:
                    HandleRunRestart();
                    break;

                case MsgId.PlayerList:
                    var pl = message as S2CPlayerList;
                    if (pl != null)
                    {
                        WriteProbe("[net] 收到玩家列表: " + pl.Players);
                        UI.RoomEvents.PlayerListUpdated(pl.Players);
                    }
                    break;

                case MsgId.BattleStart:
                    WriteProbe("[net] 收到战斗开始广播");
                    UI.RoomEvents.BattleStart();
                    break;

                case MsgId.RoomClosed:
                    WriteProbe("[net] 收到房间解散广播");
                    m_IntentionalLeave = true;
                    m_Joined = false;
                    m_Reconnecting = false;
                    ClearLocalEntities();
                    UI.RoomEvents.RoomClosed();
                    break;

                case MsgId.MyEntity:
                    var my = message as S2CMyEntity;
                    if (my != null)
                    {
                        m_MyEntityId = my.EntityId;
                        m_JoinConfirmed = true;    // 收到加入确认
                        WriteProbe("[net] 收到 S2CMyEntity, 我的实体ID=" + m_MyEntityId + "（加入成功）");                        // 兜底：若自己的实体已被渲染（消息时序竞争），移除，避免误当其他玩家
                        if (m_LocalEntities.TryGetValue(m_MyEntityId, out var self))
                        {
                            if (self != null && self.Transform != null)
                            {
                                Destroy(self.Transform.gameObject);
                            }
                            m_LocalEntities.Remove(m_MyEntityId);
                            Debug.Log("[NetClientLogic] 移除已误渲染的自己实体 " + m_MyEntityId);
                        }
                        Debug.Log("[NetClientLogic] 我的实体 ID = " + m_MyEntityId + "（客户端不渲染自己）");
                    }
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
        /// 处理房主重开指令：清理远端实体并重启本地战斗。
        /// 若本机正处于结算流程，则直接触发结算界面的重开按钮逻辑。
        /// </summary>
        private void HandleRunRestart()
        {
            Debug.Log("[NetClientLogic] 收到房主重开指令 S2CRunRestart");
            ClearLocalEntities();

            if (Procedure.ProcedureBattle.Current != null)
            {
                Procedure.ProcedureBattle.Current.RestartRunLocally();
            }
            else
            {
                UI.GameOverEvents.RequestRestart();
            }
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

            // 不渲染自己的网络实体（本地玩家已代表自己，避免同屏多个"玩家"）
            if (spawn.Type == 0 && spawn.EntityId == m_MyEntityId)
            {
                WriteProbe("[net] 跳过渲染自己实体 " + spawn.EntityId);
                Debug.Log("[NetClientLogic] 跳过渲染自己的实体 " + spawn.EntityId);
                return;
            }

            WriteProbe("[net] 渲染网络实体 id=" + spawn.EntityId + " type=" + spawn.Type + " (my=" + m_MyEntityId + ")");

            // 生成本地表现对象：SpriteRenderer + 旧项目迁移的 emoji 美术
            // （玩家=黄色笑脸 1f603，敌人=红色恶魔 1f47f；替代原占位方块，
            //   修复构建版 3D 默认材质 shader 未打包导致的粉色方块）
            var go = new GameObject("NetEntity_" + spawn.EntityId);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            Sprite sprite = spawn.Type == 0 ? Art.ArtManager.GetPlayerSprite() : Art.ArtManager.GetEnemySprite();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = spawn.Type == 0 ? 10 : 5;

            // 玩家实体按 EntityId 着色（黄金比例色相分布）：联机时多个玩家可区分
            if (spawn.Type == 0)
            {
                float hue = (spawn.EntityId * 0.61803398875f) % 1f;
                spriteRenderer.color = Color.HSVToRGB(hue, 0.55f, 1f);
            }

            Vector3 spawnPos = new Vector3(spawn.X, spawn.Y, 0f);
            go.transform.position = spawnPos;

            // 按精灵尺寸缩放（与本地实体视觉大小一致）
            if (sprite != null)
            {
                float spriteWidth = sprite.bounds.size.x;
                if (spriteWidth > 0.01f)
                {
                    go.transform.localScale = Vector3.one * (1f / spriteWidth);
                }
            }

            m_LocalEntities[spawn.EntityId] = new LocalEntity { Transform = go.transform, TargetPosition = spawnPos };

            Debug.Log(string.Format("[NetClientLogic] 生成实体 {0} type={1} 于 ({2:F1},{3:F1}) sprite={4}",
                spawn.EntityId, spawn.Type, spawn.X, spawn.Y, sprite != null ? "OK" : "NULL"));
        }

        private void HandleEntityState(S2CEntityState state)
        {
            if (state == null || !m_LocalEntities.TryGetValue(state.EntityId, out var entity))
            {
                return;
            }

            // 更新目标位置（由 UpdateInterpolation 平滑跟随）
            entity.TargetPosition = new Vector3(state.X, state.Y, 0f);
        }

        private void HandleRemoveEntity(S2CRemoveEntity remove)
        {
            if (remove == null)
            {
                return;
            }

            if (m_LocalEntities.TryGetValue(remove.EntityId, out var entity))
            {
                if (entity.Transform != null)
                {
                    Destroy(entity.Transform.gameObject);
                }
                m_LocalEntities.Remove(remove.EntityId);
                Debug.Log("[NetClientLogic] 实体 " + remove.EntityId + " 移除");
            }
        }

        /// <summary>
        /// 插值平滑所有实体位置。
        /// </summary>
        private void UpdateInterpolation()
        {
            float t = m_InterpolationSpeed * Time.deltaTime;
            foreach (var kv in m_LocalEntities)
            {
                var entity = kv.Value;
                if (entity == null || entity.Transform == null)
                {
                    continue;
                }

                entity.Transform.position = Vector3.Lerp(entity.Transform.position, entity.TargetPosition, t);
            }
        }

        /// <summary>当前实体数量（测试用）。</summary>
        public int LocalEntityCount
        {
            get { return m_LocalEntities.Count; }
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }

        /// <summary>是否正在重连。</summary>
        public bool IsReconnecting
        {
            get { return m_Reconnecting; }
        }
    }
}
