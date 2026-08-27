//------------------------------------------------------------
// EmojiWar GameMain - 客户端同步逻辑（确定性帧同步 / Lockstep）
// 职责：
//   1. 每帧上行输入意图（C2SPlayerInput，不含位置结果）
//   2. 接收 Host 广播的输入帧（S2CInputFrame）→ 推进本地 LockstepSimulation
//   3. 表现层（SimView）从模拟状态渲染，本地不再自己模拟战斗结果
// 支持：断线重连、房间解散返回大厅。
//------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LockstepSim = EmojiWar.GameMain.Simulation.LockstepSimulation;
using SimIntent = EmojiWar.GameMain.Simulation.PlayerIntent;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// 客户端：输入意图上行 + 输入帧驱动本地确定性模拟。
    /// </summary>
    public class NetClientLogic : MonoBehaviour
    {
        /// <summary>本地确定性模拟（由输入帧驱动）。</summary>
        public Simulation.LockstepSimulation Simulation { get; private set; }

        /// <summary>表现层（绑定到本地模拟）。</summary>
        public Simulation.SimView View { get; private set; }

        private NetworkService m_Service = null;
        private bool m_Joined = false;
        private bool m_JoinSent = false;
        private bool m_IntentionalLeave = false;

        /// <summary>加入确认（收到 S2CMyEntity 视为加入成功）；超时未确认则重发 JoinRoom。</summary>
        private bool m_JoinConfirmed = false;
        private float m_JoinSendTime = 0f;
        private const float JoinConfirmTimeout = 3f;

        /// <summary>自己的网络实体 ID（Host 告知）。</summary>
        private int m_MyEntityId = -1;

        /// <summary>本机网络实体 ID。</summary>
        public int MyEntityId { get { return m_MyEntityId; } }

        private string m_PlayerName = "玩家";
        private string m_ServerIp = "127.0.0.1";
        private int m_ServerPort = NetworkService.DefaultPort;

        // 玩家名册（entityId → characterId；从 S2CSpawnEntity 收集，战斗模拟重建用）
        private readonly Dictionary<int, int> m_Roster = new Dictionary<int, int>();

        // 重连状态
        private bool m_Reconnecting = false;
        private float m_ReconnectDelay = 2f;
        private float m_ReconnectTimer = 0f;
        private int m_ReconnectAttempts = 0;

        /// <summary>重连状态变化事件（参数：是否重连中）。</summary>
        public event System.Action<bool> OnReconnectStateChanged;

        /// <summary>无输入时自动转圈（回环验证用；真实联机由 DisableAutoMove 关闭）。</summary>
        [SerializeField]
        private bool m_AutoMoveWhenIdle = true;

        /// <summary>关闭空闲自动移动（真实联机玩家静止时不应绕圈）。</summary>
        public void DisableAutoMove()
        {
            m_AutoMoveWhenIdle = false;
        }

        private void Awake()
        {
            m_Service = GameEntry.NetworkService;
        }

        /// <summary>绑定网络服务（测试/多实例场景使用）。</summary>
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
                Debug.Log("[NetClientLogic] 连接断开（房主退出），返回大厅");
                WriteProbe("[net] 连接断开，返回大厅");
                m_IntentionalLeave = true;
                m_Joined = false;
                m_Reconnecting = false;
                ClearSimulation();
                UI.RoomEvents.RoomClosed();
            }
        }

        /// <summary>加入房间（连接建立后调用）。</summary>
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

        /// <summary>发送准备/取消准备请求。</summary>
        public void SendReady(bool ready)
        {
            if (m_Service != null)
            {
                m_Service.Send(new C2SReadyChange { Ready = ready });
                WriteProbe("[net] 发送准备状态: " + ready);
            }
        }

        /// <summary>主动离开房间：停止重连并清理。</summary>
        public void LeaveRoom()
        {
            m_IntentionalLeave = true;
            m_Joined = false;
            m_Reconnecting = false;
            ClearSimulation();
            Debug.Log("[NetClientLogic] 主动离开房间");
        }

        /// <summary>清理本地模拟与表现（离开/断开/重开时）。</summary>
        public void ClearSimulation()
        {
            if (View != null)
            {
                View.ClearAllViews();
            }
            Simulation = null;
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

            // 连接建立后补发加入房间请求（异步连接时序）
            if (!m_JoinSent && m_Service.IsConnected)
            {
                m_JoinSent = true;
                m_JoinConfirmed = false;
                m_JoinSendTime = Time.realtimeSinceStartup;
                m_Service.Send(new C2SJoinRoom { PlayerName = m_PlayerName, CharacterId = GetLocalCharacterId() });
                WriteProbe("[net] 发送 C2SJoinRoom: " + m_PlayerName + " char=" + GetLocalCharacterId());
                Debug.Log("[NetClientLogic] 发送加入房间请求: " + m_PlayerName);
            }

            // 加入未确认（可能被连接时序丢弃）：3 秒后重发
            if (m_JoinSent && !m_JoinConfirmed && Time.realtimeSinceStartup - m_JoinSendTime > JoinConfirmTimeout)
            {
                m_JoinSent = false;
                WriteProbe("[net] JoinRoom 3 秒未确认，重发");
                Debug.Log("[NetClientLogic] JoinRoom 未确认，重发");
            }

            // 上行输入意图（只传意图，不传位置结果 —— 帧同步）
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

            // 无键盘输入时自动转圈（回环验证用；真实联机由 DisableAutoMove 关闭）
            if (m_AutoMoveWhenIdle && inputX == 0f && inputY == 0f)
            {
                // 确定性自动转圈（用本地模拟帧号而非 Time.time，保证输入序列确定）
                float frame = Simulation != null ? Simulation.FrameIndex : Time.frameCount;
                inputX = Mathf.Cos(frame * 0.05f);
                inputY = Mathf.Sin(frame * 0.05f);
            }

            // 鼠标瞄准（世界坐标方向）
            Vector3 mouseWorld = Vector3.zero;
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mouseWorld = mainCam.ScreenToWorldPoint(Input.mousePosition);
            }

            // 复用输入消息对象（每帧上行，避免 60Hz 对象分配；Send 内同步序列化后即可复用）
            if (m_InputMsg == null)
            {
                m_InputMsg = new C2SPlayerInput();
            }
            m_InputMsg.InputX = inputX;
            m_InputMsg.InputY = inputY;
            m_InputMsg.AimX = mouseWorld.x;
            m_InputMsg.AimY = mouseWorld.y;
            m_InputMsg.FirePrimary = Input.GetMouseButton(0);
            m_InputMsg.FireSecondary = Input.GetMouseButton(1);
            m_InputMsg.Reload = Input.GetKeyDown(KeyCode.R);
            m_Service.Send(m_InputMsg);

            // 帧同步一致性探针（每 2 秒记录一次模拟状态，供双实例对比）
            m_ProbeTimer -= Time.deltaTime;
            if (m_ProbeTimer <= 0f)
            {
                m_ProbeTimer = 2f;
                if (Simulation != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[sim] CLIENT frame=").Append(Simulation.FrameIndex)
                      .Append(" wave=").Append(Simulation.WaveIndex)
                      .Append(" players=").Append(Simulation.Players.Count)
                      .Append(" enemies=").Append(Simulation.Enemies.Count)
                      .Append(" bullets=").Append(Simulation.Bullets.Count);
                    foreach (var p in Simulation.Players)
                    {
                        sb.Append(" E").Append(p.EntityId).Append(":(")
                          .Append(p.Position.x.ToString("F2")).Append(",")
                          .Append(p.Position.y.ToString("F2")).Append(")");
                    }
                    WriteProbe(sb.ToString());
                }
            }
        }

        private float m_ProbeTimer = 2f;
        private C2SPlayerInput m_InputMsg = null;   // 复用的上行输入消息（避免每帧分配）

        /// <summary>本机角色 ID（本地玩家当前角色；无玩家时用上次选择）。</summary>
        private int GetLocalCharacterId()
        {
            var local = FindLocalPlayer();
            if (local != null && local.CharacterId > 0)
            {
                return local.CharacterId;
            }
            return Procedure.ProcedureBattle.SelectedCharacterId;
        }

        /// <summary>查找本地玩家（房间页/战斗中的本机玩家实体）。</summary>
        private Entity.PlayerEntity FindLocalPlayer()
        {
            var players = Object.FindObjectsOfType<Entity.PlayerEntity>();
            if (players != null && players.Length > 0)
            {
                return players[0];
            }
            return null;
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
            ClearSimulation();
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

            m_ReconnectAttempts++;
            if (m_ReconnectAttempts % 20 == 0)
            {
                WriteProbe("[net] 持续重连中... 第 " + m_ReconnectAttempts + " 次");
            }

            if (m_Service != null)
            {
                m_Service.ConnectToServer(m_ServerIp, m_ServerPort);
                m_ReconnectTimer = m_ReconnectDelay;
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
                m_Service.Send(new C2SJoinRoom { PlayerName = m_PlayerName, CharacterId = GetLocalCharacterId() });
                m_Reconnecting = false;
                OnReconnectStateChanged?.Invoke(false);
            }
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

                case MsgId.RemoveEntity:
                    var rm = message as S2CRemoveEntity;
                    if (rm != null && Simulation != null)
                    {
                        Simulation.RemovePlayer(rm.EntityId);
                    }
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
                    var bs = message as S2CBattleStart;
                    WriteProbe("[net] 收到战斗开始广播 seed=" + (bs != null ? bs.Seed.ToString() : "null"));
                    if (bs != null)
                    {
                        InitializeBattleSimulation(bs.Seed);
                    }
                    UI.RoomEvents.BattleStart();
                    break;

                case MsgId.InputFrame:
                    HandleInputFrame(message as S2CInputFrame);
                    break;

                case MsgId.WeaponUpdate:
                    var wu = message as S2CWeaponUpdate;
                    if (wu != null && Simulation != null)
                    {
                        var cfg = new Simulation.SimPlayerConfig
                        {
                            WeaponId = wu.WeaponId,
                            WeaponName = wu.WeaponName,
                            WeaponIcon = wu.WeaponIcon,
                            WeaponDamage = wu.Damage,
                            FireRate = wu.FireRate,
                            MaxAmmo = wu.MaxAmmo,
                            ReloadTime = wu.ReloadTime,
                            BulletSpeed = wu.BulletSpeed,
                            Spread = wu.Spread,
                        };
                        Simulation.ApplyWeapon(wu.EntityId, cfg);
                        WriteProbe("[net] 武器更新 -> entity " + wu.EntityId + " " + wu.WeaponName);
                    }
                    break;

                case MsgId.RoomClosed:
                    WriteProbe("[net] 收到房间解散广播");
                    m_IntentionalLeave = true;
                    m_Joined = false;
                    m_Reconnecting = false;
                    ClearSimulation();
                    UI.RoomEvents.RoomClosed();
                    break;

                case MsgId.MyEntity:
                    var my = message as S2CMyEntity;
                    if (my != null)
                    {
                        m_MyEntityId = my.EntityId;
                        m_JoinConfirmed = true;
                        WriteProbe("[net] 收到 S2CMyEntity, 我的实体ID=" + m_MyEntityId + "（加入成功）");
                        Debug.Log("[NetClientLogic] 我的实体 ID = " + m_MyEntityId);

                        // 房间阶段模拟（seed=0，与 Host 一致）：加入自己，玩家移动由模拟同步
                        EnsureRoomSimulation();
                    }
                    break;
            }
        }

        /// <summary>处理输入帧：推进本地确定性模拟（表现层自动从模拟渲染）。</summary>
        private void HandleInputFrame(S2CInputFrame frame)
        {
            if (frame == null || Simulation == null)
            {
                return;
            }

            var inputs = new Dictionary<int, SimIntent>();
            for (int i = 0; i < frame.Count; i++)
            {
                int entityId = frame.EntityIds[i];
                inputs[entityId] = new SimIntent
                {
                    MoveX = frame.InputXs[i],
                    MoveY = frame.InputYs[i],
                    AimX = frame.AimXs[i],
                    AimY = frame.AimYs[i],
                    FirePrimary = frame.FirePrimaries[i],
                    FireSecondary = frame.FireSecondaries[i],
                    Reload = frame.Reloads[i],
                };
            }

            Simulation.Tick(inputs);
        }

        /// <summary>处理实体生成：玩家进名册并加入本地模拟（玩家位置由模拟驱动）。</summary>
        private void HandleSpawn(S2CSpawnEntity spawn)
        {
            if (spawn == null || spawn.Type != 0)
            {
                return;    // 只关心玩家（敌人由模拟生成）
            }

            if (!m_Roster.ContainsKey(spawn.EntityId))
            {
                m_Roster[spawn.EntityId] = spawn.CharacterId;
            }

            if (Simulation != null && Simulation.GetPlayerByEntityId(spawn.EntityId) == null)
            {
                Simulation.AddPlayer(BuildPlayerConfig(spawn.EntityId, spawn.CharacterId));
            }
        }

        /// <summary>从数据表构建确定性玩家配置（与 Host 相同规则）。</summary>
        private Simulation.SimPlayerConfig BuildPlayerConfig(int entityId, int characterId)
        {
            var config = new Simulation.SimPlayerConfig
            {
                SessionId = -1,
                EntityId = entityId,
                CharacterId = characterId,
                StartPosition = Vector2.zero,
            };

            if (GameEntry.Data != null)
            {
                var character = GameEntry.Data.GetCharacter(characterId);
                if (character != null)
                {
                    config.MoveSpeed = character.MoveSpeed;
                    var weapon = GameEntry.Data.GetWeapon(character.DefaultWeaponId);
                    if (weapon != null)
                    {
                        config.WeaponId = weapon.Id;
                        config.WeaponName = weapon.WeaponName;
                        config.WeaponIcon = weapon.Icon;
                        config.WeaponDamage = weapon.Damage;
                        config.FireRate = weapon.FireRate;
                        config.MaxAmmo = weapon.MaxAmmo;
                        config.ReloadTime = weapon.ReloadTime;
                        config.BulletSpeed = weapon.BulletSpeed;
                        config.Spread = weapon.Spread;
                    }
                }
            }
            return config;
        }

        /// <summary>
        /// 战斗开始：用广播种子重建本地确定性模拟（与 Host 一致）并开启波次。
        /// </summary>
        private void InitializeBattleSimulation(int seed)
        {
            Simulation = new Simulation.LockstepSimulation();

            var configs = new List<Simulation.SimPlayerConfig>();
            foreach (var kv in m_Roster)
            {
                configs.Add(BuildPlayerConfig(kv.Key, kv.Value));
            }
            // 兜底：若自己尚未进入名册（时序竞争），补上
            if (m_MyEntityId >= 0 && !m_Roster.ContainsKey(m_MyEntityId))
            {
                configs.Add(BuildPlayerConfig(m_MyEntityId, GetLocalCharacterId()));
            }

            Simulation.Initialize(seed, configs);
            Simulation.StartWave(1);
            WriteProbe("[net] 战斗模拟初始化 seed=" + seed + " players=" + configs.Count);
            Debug.Log("[NetClientLogic] 战斗确定性模拟已初始化，玩家数 " + configs.Count);

            if (GameEntry.SimView != null)
            {
                GameEntry.SimView.SetSimulation(Simulation, m_MyEntityId);
            }
        }

        /// <summary>房间阶段确定性模拟（seed=0，与 Host 一致；无敌人，仅玩家移动）。</summary>
        private void EnsureRoomSimulation()
        {
            if (Simulation != null)
            {
                return;
            }
            Simulation = new Simulation.LockstepSimulation();
            Simulation.Initialize(0, null);
            if (m_MyEntityId >= 0)
            {
                Simulation.AddPlayer(BuildPlayerConfig(m_MyEntityId, GetLocalCharacterId()));
                m_Roster[m_MyEntityId] = GetLocalCharacterId();
            }
            WriteProbe("[net] 房间模拟已启动（seed=0）");
            if (GameEntry.SimView != null)
            {
                GameEntry.SimView.SetSimulation(Simulation, m_MyEntityId);
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

        /// <summary>处理房主回房间指令：重置本地模拟到房间阶段（seed=0）。</summary>
        private void HandleRunRestart()
        {
            Debug.Log("[NetClientLogic] 收到房主回房间指令 S2CRunRestart");
            ClearSimulation();
            EnsureRoomSimulation();
            WriteProbe("[net] RunRestart -> 重建房间模拟");
        }

        /// <summary>发送购买请求。</summary>
        public void RequestBuy(int shopItemIndex)
        {
            if (m_Service != null)
            {
                m_Service.Send(new C2SBuyItem { ShopItemIndex = shopItemIndex });
                Debug.Log("[NetClientLogic] 发送购买请求: " + shopItemIndex);
            }
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
