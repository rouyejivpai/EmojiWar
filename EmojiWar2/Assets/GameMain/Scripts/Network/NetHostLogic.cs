//------------------------------------------------------------
// EmojiWar GameMain - Host 端逻辑（确定性帧同步 / Lockstep）
// 职责：
//   1. 房间管理：玩家加入/离开/准备/玩家列表（不变）
//   2. 输入收集器：每逻辑 tick 收齐所有玩家（含本地 session0）的输入意图，
//      广播 S2CInputFrame，本地也推进同一份 LockstepSimulation
//   3. 掉线托管：某玩家未上报输入 → 该 tick 用空输入（全端一致）
// 战斗结果由确定性模拟产生，网络只传输入意图，不再传位置/HP。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using LockstepSim = EmojiWar.GameMain.Simulation.LockstepSimulation;
using SimIntent = EmojiWar.GameMain.Simulation.PlayerIntent;

namespace EmojiWar.GameMain.Network
{
    /// <summary>
    /// Host 端：房间管理 + 输入收集广播 + 本地确定性模拟。
    /// </summary>
    public class NetHostLogic : MonoBehaviour
    {
        // 房间玩家状态（sessionId → 状态）
        private sealed class PlayerState
        {
            public int SessionId;
            public int EntityId;
            public string PlayerName = "";
            public bool Ready = false;
            public int CharacterId = 1;
        }

        private readonly Dictionary<int, PlayerState> m_Players = new Dictionary<int, PlayerState>();

        // 最近收到的输入意图（sessionId → input；掉线玩家缺省）
        private readonly Dictionary<int, C2SPlayerInput> m_LatestInputs = new Dictionary<int, C2SPlayerInput>();

        private NetworkService m_Service = null;
        private int m_NextEntityId = 1000;

        /// <summary>确定性模拟（Host 本地也作为一端参与推演）。</summary>
        public Simulation.LockstepSimulation Simulation { get; private set; }

        /// <summary>战斗是否已开始（tick 循环是否运行）。</summary>
        public bool BattleRunning { get; private set; }

        // tick 循环（20Hz）
        private float m_TickAccumulator = 0f;

        [Header("波次配置")]
        [SerializeField]
        private int m_EnemiesPerWave = 3;

        [SerializeField]
        private float m_SpawnRadius = 8f;

        private bool m_BattleStartBroadcasted = false;
        private bool m_LocalAutoMove = true;   // Host 本地无输入时自动转圈（回环测试用；真实联机由 DisableLocalAutoMove 关闭）

        /// <summary>房间内全部准备后触发（Host 本地切流程用）。</summary>
        public static event System.Action OnBattleStartRequested;

        private NetworkService Service { get { return m_Service; } }

        private void Awake()
        {
            m_Service = GameEntry.NetworkService;
        }

        /// <summary>绑定网络服务（测试/多实例场景使用）。</summary>
        public void Bind(NetworkService service)
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage -= OnClientMessage;
                m_Service.OnModeChanged -= OnModeChanged;
                m_Service.OnClientDisconnected -= OnClientDisconnected;
            }
            m_Service = service;
            if (m_Service != null)
            {
                m_Service.OnClientMessage += OnClientMessage;
                m_Service.OnModeChanged += OnModeChanged;
                m_Service.OnClientDisconnected += OnClientDisconnected;
            }
        }

        private void OnClientDisconnected(int sessionId)
        {
            Debug.Log("[NetHostLogic] 客户端 " + sessionId + " 断开");
            HandleLeave(sessionId);
        }

        private void OnEnable()
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage += OnClientMessage;
                m_Service.OnModeChanged += OnModeChanged;
                m_Service.OnClientDisconnected += OnClientDisconnected;
            }
        }

        private void OnDisable()
        {
            if (m_Service != null)
            {
                m_Service.OnClientMessage -= OnClientMessage;
                m_Service.OnModeChanged -= OnModeChanged;
                m_Service.OnClientDisconnected -= OnClientDisconnected;
            }
        }

        private void OnModeChanged(NetMode mode)
        {
            if (mode == NetMode.Host)
            {
                m_Players.Clear();
                m_LatestInputs.Clear();
                Simulation = null;
                BattleRunning = false;
                m_BattleStartBroadcasted = false;
                Debug.Log("[NetHostLogic] Host 模式就绪，等待玩家加入");
            }
        }

        /// <summary>Host 本机加入（作为 1 号玩家，session 0，不走网络连接）。</summary>
        public void JoinLocal(string playerName, int characterId = 1)
        {
            var state = new PlayerState
            {
                SessionId = 0,
                EntityId = m_NextEntityId++,
                PlayerName = playerName,
                CharacterId = characterId,
            };
            m_Players[0] = state;
            Debug.Log("[NetHostLogic] 房主 " + playerName + " 本机加入，实体 " + state.EntityId);

            // 房间阶段模拟：确保存在并加入本机玩家（移动由模拟同步）
            EnsureRoomSimulation();
            AddPlayerToSimulation(state);

            // 广播生成（供其他客户端看到房主）
            var spawn = new S2CSpawnEntity
            {
                EntityId = state.EntityId,
                Type = 0,
                Team = 1,
                X = 0f,
                Y = 0f,
                CharacterId = state.CharacterId,
            };
            m_Service?.BroadcastToClients(spawn);

            BroadcastPlayerList();
        }

        /// <summary>房间阶段确定性模拟（seed=0，无敌人；战斗开始后重建并开波）。</summary>
        private void EnsureRoomSimulation()
        {
            if (Simulation == null)
            {
                Simulation = new Simulation.LockstepSimulation();
                Simulation.Initialize(0, null);
                BattleRunning = true;
                m_TickAccumulator = 0f;
                WriteProbe("[net-host] 房间模拟已启动（seed=0）");
            }
            BindSimView();
        }

        /// <summary>绑定本地表现层到 Host 模拟（本机实体 ID = session0 实体）。</summary>
        private void BindSimView()
        {
            if (Simulation == null || GameEntry.SimView == null)
            {
                return;
            }
            GameEntry.SimView.SetSimulation(Simulation, GetLocalEntityId());
        }

        /// <summary>把玩家加入本地模拟（幂等）。</summary>
        private void AddPlayerToSimulation(PlayerState state)
        {
            if (Simulation == null)
            {
                return;
            }
            Simulation.AddPlayer(BuildPlayerConfig(state));
            WriteProbe("[net-host] 模拟加入玩家 entity=" + state.EntityId + " char=" + state.CharacterId);
        }

        /// <summary>Host 本机设置准备状态（不走网络）。</summary>
        public void SetLocalReady(bool ready)
        {
            if (m_Players.TryGetValue(0, out var state))
            {
                HandleReadyChange(0, new C2SReadyChange { Ready = ready });
            }
        }

        /// <summary>Host 本机（session 0）的网络实体 ID。</summary>
        public int GetLocalEntityId()
        {
            return m_Players.TryGetValue(0, out var s) ? s.EntityId : -1;
        }

        /// <summary>Host 本机当前准备状态。</summary>
        public bool IsLocalReady
        {
            get { return m_Players.TryGetValue(0, out var s) && s.Ready; }
        }

        /// <summary>当前房间玩家数（诊断）。</summary>
        public int PlayerCount { get { return m_Players.Count; } }

        /// <summary>关闭 Host 本地空闲自动移动（正式联机）。</summary>
        public void DisableLocalAutoMove()
        {
            m_LocalAutoMove = false;
        }

        /// <summary>
        /// 每帧：驱动 20Hz 逻辑 tick。
        /// </summary>
        private void Update()
        {
            if (m_Service == null || m_Service.Mode != NetMode.Host || !BattleRunning)
            {
                return;
            }

            // 批量推进用时间预算（文档 §2）：正常 4ms；累积帧多（追帧/卡顿恢复）放宽到 12ms；
            // 帧数上限兜底（一次最多 60 帧），预算只能在完整逻辑帧边界检查。
            m_TickAccumulator += Time.deltaTime;
            float budgetMs = (m_TickAccumulator >= LockstepSim.TickInterval * 8f) ? 12f : 4f;
            float budgetEnd = Time.realtimeSinceStartup + budgetMs * 0.001f;
            int framesThisFrame = 0;
            while (m_TickAccumulator >= LockstepSim.TickInterval && framesThisFrame < 60)
            {
                m_TickAccumulator -= LockstepSim.TickInterval;
                HostTick();
                framesThisFrame++;
                // 预算只在完整逻辑帧边界检查（文档 §2：中途截断 = 破坏确定性）
                if (Time.realtimeSinceStartup >= budgetEnd && m_TickAccumulator >= LockstepSim.TickInterval)
                {
                    break;
                }
            }

            // 推进循环结束后不再设置插值系数：SimView.LateUpdate 检测模拟帧号推进自算插值时间
            // （文档 §2 渲染插值），避免跨组件执行顺序导致的基准错位/位置回退抖动。

            // 帧同步一致性探针（每 2 秒记录一次模拟状态 + fps，供双实例对比）
            m_ProbeTimer -= Time.deltaTime;
            if (m_ProbeTimer <= 0f)
            {
                m_ProbeTimer = 2f;
                float fps = m_FpsAccumTime > 0f ? m_FpsAccumFrames / m_FpsAccumTime : 0f;
                m_FpsAccumFrames = 0;
                m_FpsAccumTime = 0f;
                if (Simulation != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[sim] HOST frame=").Append(Simulation.FrameIndex)
                      .Append(" wave=").Append(Simulation.WaveIndex)
                      .Append(" players=").Append(Simulation.Players.Count)
                      .Append(" enemies=").Append(Simulation.Enemies.Count)
                      .Append(" bullets=").Append(Simulation.Bullets.Count)
                      .Append(" fps=").Append(fps.ToString("F0"));
                    foreach (var p in Simulation.Players)
                    {
                        sb.Append(" E").Append(p.EntityId).Append(":(")
                          .Append(p.Position.x.ToString("F2")).Append(",")
                          .Append(p.Position.y.ToString("F2")).Append(")");
                    }
                    WriteProbe(sb.ToString());
                }
            }
            else
            {
                // 累积 fps（渲染帧率）
                m_FpsAccumFrames++;
                m_FpsAccumTime += Time.unscaledDeltaTime;
            }
        }

        private float m_ProbeTimer = 2f;
        private int m_FpsAccumFrames = 0;
        private float m_FpsAccumTime = 0f;

        // 复用输入帧与输入字典（HostTick 20Hz 每 tick 调用，避免高频分配）
        private S2CInputFrame m_InputFrame = null;
        private readonly Dictionary<int, SimIntent> m_InputsCache = new Dictionary<int, SimIntent>(4);
        private List<PlayerState> m_SortedPlayers = null;   // 有序玩家列表（确定性遍历）

        /// <summary>
        /// 一个逻辑 tick：收集全部玩家意图 → 广播输入帧 → 本地推进模拟。
        /// </summary>
        private void HostTick()
        {
            // 复用输入帧（数组按玩家数惰性扩容）
            if (m_InputFrame == null || m_InputFrame.EntityIds == null || m_InputFrame.EntityIds.Length < m_Players.Count)
            {
                m_InputFrame = new S2CInputFrame
                {
                    EntityIds = new int[m_Players.Count],
                    InputXs = new float[m_Players.Count],
                    InputYs = new float[m_Players.Count],
                    AimXs = new float[m_Players.Count],
                    AimYs = new float[m_Players.Count],
                    FirePrimaries = new bool[m_Players.Count],
                    FireSecondaries = new bool[m_Players.Count],
                    Reloads = new bool[m_Players.Count],
                };
            }
            var frame = m_InputFrame;
            frame.FrameIndex = Simulation.FrameIndex + 1;
            frame.Count = m_Players.Count;

            m_InputsCache.Clear();

            // 按 EntityId 升序遍历（确定性：输入帧数组顺序跨端一致，文档 §9 全序原则）
            if (m_SortedPlayers == null || m_SortedPlayers.Capacity < m_Players.Count)
            {
                m_SortedPlayers = new List<PlayerState>(m_Players.Count);
            }
            m_SortedPlayers.Clear();
            foreach (var kv in m_Players)
            {
                m_SortedPlayers.Add(kv.Value);
            }
            m_SortedPlayers.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            int index = 0;
            foreach (var state in m_SortedPlayers)
            {
                int sessionId = state.SessionId;
                SimIntent intent;

                if (sessionId == 0)
                {
                    intent = ReadLocalInput();
                }
                else if (m_LatestInputs.TryGetValue(sessionId, out var clientInput))
                {
                    intent = new SimIntent
                    {
                        MoveX = clientInput.InputX,
                        MoveY = clientInput.InputY,
                        AimX = clientInput.AimX,
                        AimY = clientInput.AimY,
                        FirePrimary = clientInput.FirePrimary,
                        FireSecondary = clientInput.FireSecondary,
                        Reload = clientInput.Reload,
                    };
                }
                else
                {
                    // 掉线托管：空输入（与客户端缺失输入时的规则一致）
                    intent = SimIntent.Empty;
                }

                // 填充广播帧（以实体 ID 标识，客户端据此匹配本地模拟玩家）
                frame.EntityIds[index] = state.EntityId;
                frame.InputXs[index] = intent.MoveX;
                frame.InputYs[index] = intent.MoveY;
                frame.AimXs[index] = intent.AimX;
                frame.AimYs[index] = intent.AimY;
                frame.FirePrimaries[index] = intent.FirePrimary;
                frame.FireSecondaries[index] = intent.FireSecondary;
                frame.Reloads[index] = intent.Reload;
                index++;

                m_InputsCache[state.EntityId] = intent;
            }

            // 广播输入帧（客户端据此推进同一份模拟）
            if (m_Service != null)
            {
                m_Service.BroadcastToClients(frame);
            }

            // 输入流录像（文档 §3：记录每帧输入，供重放/不同步 diff）
            if (EmojiWar.GameMain.Simulation.ReplayRecorder.IsRecording && Simulation != null)
            {
                var entityList = new List<int>(frame.Count);
                var intentList = new List<SimIntent>(frame.Count);
                for (int i = 0; i < frame.Count; i++)
                {
                    entityList.Add(frame.EntityIds[i]);
                    intentList.Add(m_InputsCache[frame.EntityIds[i]]);
                }
                EmojiWar.GameMain.Simulation.ReplayRecorder.RecordFrame(frame.FrameIndex, entityList, intentList);
            }

            // 本地推进模拟（复用输入字典）
            if (Simulation != null)
            {
                Simulation.Tick(m_InputsCache);
            }
        }

        /// <summary>读取 Host 本地玩家输入意图（WASD + 鼠标瞄准 + 左键射击 + R 装弹；无输入时自动转圈供测试）。</summary>
        private Simulation.PlayerIntent ReadLocalInput()
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

            if (m_LocalAutoMove && inputX == 0f && inputY == 0f)
            {
                // 确定性自动转圈（用模拟帧号而非 Time.time，保证各端输入序列一致 —— 帧同步要求）
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
            Vector2 aim = new Vector2(mouseWorld.x, mouseWorld.y);

            return new Simulation.PlayerIntent
            {
                MoveX = inputX,
                MoveY = inputY,
                AimX = aim.x,
                AimY = aim.y,
                FirePrimary = Input.GetMouseButton(0),
                FireSecondary = Input.GetMouseButton(1),
                Reload = Input.GetKeyDown(KeyCode.R),
            };
        }

        // ==================== 消息处理 ====================

        private void OnClientMessage(int sessionId, NetMessage message)
        {
            switch (message.Id)
            {
                case MsgId.JoinRoom:
                    HandleJoin(sessionId, message as C2SJoinRoom);
                    break;

                case MsgId.PlayerInput:
                    var input = message as C2SPlayerInput;
                    if (input != null)
                    {
                        m_LatestInputs[sessionId] = input;
                    }
                    break;

                case MsgId.BuyItem:
                    HandleBuyItem(sessionId, message as C2SBuyItem);
                    break;

                case MsgId.LeaveRoom:
                    HandleLeave(sessionId);
                    break;

                case MsgId.ReadyChange:
                    HandleReadyChange(sessionId, message as C2SReadyChange);
                    break;
            }
        }

        /// <summary>
        /// 处理准备/取消准备：更新状态 → 广播玩家列表 → 检测全部准备后广播战斗开始。
        /// </summary>
        private void HandleReadyChange(int sessionId, C2SReadyChange ready)
        {
            WriteProbe("[net-host] HandleReadyChange session=" + sessionId + " ready=" + (ready != null ? ready.Ready.ToString() : "null-msg"));
            if (ready == null || !m_Players.TryGetValue(sessionId, out var state))
            {
                WriteProbe("[net-host] HandleReadyChange ignored (no player state)");
                return;
            }

            if (m_BattleStartBroadcasted)
            {
                WriteProbe("[net-host] HandleReadyChange ignored (battle already started)");
                return;
            }

            state.Ready = ready.Ready;
            Debug.Log("[NetHostLogic] 玩家 " + sessionId + "(" + state.PlayerName + ") 准备=" + state.Ready);
            BroadcastPlayerList();

            if (m_Players.Count >= 1 && AllReady())
            {
                m_BattleStartBroadcasted = true;
                int seed = UnityEngine.Random.Range(0, 100000);
                m_Service.BroadcastToClients(new S2CBattleStart { Seed = seed });
                Debug.Log("[NetHostLogic] 全部玩家已准备，广播战斗开始 seed=" + seed);
                WriteProbe("[net-host] 全部准备，广播 BattleStart seed=" + seed);

                // 战斗开始：用新种子重建确定性模拟（所有端一致）并开启波次
                InitializeSimulation(seed);
                Simulation.StartWave(1);

                // 输入流录像开始（文档 §3：初始状态 + 输入流）
                try
                {
                    var configs = new List<Simulation.SimPlayerConfig>();
                    foreach (var kv in m_Players)
                    {
                        configs.Add(BuildPlayerConfig(kv.Value));
                    }
                    EmojiWar.GameMain.Simulation.ReplayRecorder.Begin(seed, "0.3.0-20260827", configs,
                        System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/replays"));
                    WriteProbe("[net-host] 输入流录像开始 seed=" + seed);

                    // 确定性打点开始（文档 §4：不同步 diff 用）
                    EmojiWar.GameMain.Simulation.DeterminismTracer.Begin(
                        System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/traces"));
                }
                catch (System.Exception e)
                {
                    WriteProbe("[net-host] 录像/打点开始异常: " + e.Message);
                }

                OnBattleStartRequested?.Invoke();
            }
        }

        /// <summary>
        /// 初始化确定性模拟（Host 侧）：构建玩家配置（角色/武器参数来自数据表）。
        /// </summary>
        private void InitializeSimulation(int seed)
        {
            Simulation = new Simulation.LockstepSimulation();
            var configs = new List<Simulation.SimPlayerConfig>();

            foreach (var kv in m_Players)
            {
                var p = kv.Value;
                configs.Add(BuildPlayerConfig(p));
            }

            Simulation.Initialize(seed, configs);
            BattleRunning = true;
            m_TickAccumulator = 0f;
            BindSimView();
            WriteProbe("[net-host] 模拟初始化 seed=" + seed + " players=" + configs.Count);
            Debug.Log("[NetHostLogic] 确定性模拟已初始化，玩家数 " + configs.Count);
        }

        /// <summary>从角色/武器数据表构建确定性玩家配置（单一逻辑源：统一走 SimConfigFactory）。</summary>
        private Simulation.SimPlayerConfig BuildPlayerConfig(PlayerState p)
        {
            return EmojiWar.GameMain.Simulation.SimConfigFactory.Build(p.SessionId, p.EntityId, p.CharacterId);
        }

        /// <summary>回到房间（一局结束后）：重置准备状态与模拟，等待下一局。</summary>
        public void ResetRoom()
        {
            // 输入流录像结束（强制 flush，防尾帧丢失）+ 打点结束
            EmojiWar.GameMain.Simulation.ReplayRecorder.End();
            EmojiWar.GameMain.Simulation.DeterminismTracer.End();

            m_BattleStartBroadcasted = false;
            BattleRunning = false;
            Simulation = null;
            m_LatestInputs.Clear();
            m_TickAccumulator = 0f;
            foreach (var p in m_Players.Values)
            {
                p.Ready = false;
            }
            // 恢复房间阶段模拟（seed=0，仅玩家移动），重新加入所有玩家
            EnsureRoomSimulation();
            foreach (var p in m_Players.Values)
            {
                AddPlayerToSimulation(p);
            }
            // 通知客户端重置模拟（回房间阶段）
            if (m_Service != null)
            {
                m_Service.BroadcastToClients(new S2CRunRestart { Seed = 0 });
            }
            BroadcastPlayerList();
            Debug.Log("[NetHostLogic] 房间已重置，等待下一局");
        }

        private bool AllReady()
        {
            foreach (var p in m_Players.Values)
            {
                if (!p.Ready)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>广播房间玩家列表（"名字:准备;..."）。</summary>
        private void BroadcastPlayerList()
        {
            if (m_Service == null)
            {
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var p in m_Players.Values)
            {
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(p.PlayerName).Append(':').Append(p.Ready ? "1" : "0");
            }

            var msg = new S2CPlayerList { Count = m_Players.Count, Players = sb.ToString() };
            m_Service.BroadcastToClients(msg);
            UI.RoomEvents.PlayerListUpdated(msg.Players);
            WriteProbe("[net-host] 广播玩家列表: " + msg.Players);
        }

        /// <summary>
        /// 处理购买请求（商店购买：武器/Mod）。
        /// 确定性规则：商品由模拟确定性生成（同种子），Host 按索引解析后：
        ///   - 武器：更新玩家模拟武器并广播 S2CWeaponUpdate（所有端一致）
        ///   - Mod：暂存背包（Phase 简化）
        /// </summary>
        private void HandleBuyItem(int sessionId, C2SBuyItem buy)
        {
            if (buy == null || Simulation == null || !m_Players.TryGetValue(sessionId, out var state))
            {
                return;
            }

            string offer = Simulation.ShopItems;
            if (string.IsNullOrEmpty(offer))
            {
                Debug.Log("[NetHostLogic] 商店未开放，忽略购买请求");
                return;
            }

            string[] entries = offer.Split(';');
            if (buy.ShopItemIndex < 0 || buy.ShopItemIndex >= entries.Length)
            {
                Debug.Log("[NetHostLogic] 无效商品索引 " + buy.ShopItemIndex);
                return;
            }

            string[] kv = entries[buy.ShopItemIndex].Split(':');
            if (kv.Length < 3)
            {
                return;
            }

            int type = 0, id = 0;
            if (!int.TryParse(kv[0], out type) || !int.TryParse(kv[1], out id))
            {
                return;
            }

            Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 购买 type=" + type + " id=" + id);

            if (type == 0)
            {
                // 武器：更新模拟 + 广播（各端调用 ApplyWeapon 保持确定性）
                var weapon = GameEntry.Data != null ? GameEntry.Data.GetWeapon(id) : null;
                if (weapon == null)
                {
                    return;
                }

                var cfg = new Simulation.SimPlayerConfig
                {
                    WeaponId = weapon.Id,
                    WeaponName = weapon.WeaponName,
                    WeaponIcon = weapon.Icon,
                    WeaponDamage = weapon.Damage,
                    FireRate = weapon.FireRate,
                    MaxAmmo = weapon.MaxAmmo,
                    ReloadTime = weapon.ReloadTime,
                    BulletSpeed = weapon.BulletSpeed,
                    Spread = weapon.Spread,
                };
                Simulation.ApplyWeapon(state.EntityId, cfg);

                m_Service.BroadcastToClients(new S2CWeaponUpdate
                {
                    EntityId = state.EntityId,
                    WeaponId = weapon.Id,
                    WeaponName = weapon.WeaponName,
                    WeaponIcon = weapon.Icon,
                    Damage = weapon.Damage,
                    FireRate = weapon.FireRate,
                    MaxAmmo = weapon.MaxAmmo,
                    ReloadTime = weapon.ReloadTime,
                    BulletSpeed = weapon.BulletSpeed,
                    Spread = weapon.Spread,
                });
                WriteProbe("[net-host] 购买武器 " + weapon.WeaponName + " -> entity " + state.EntityId);
            }
            else
            {
                // Mod：简化确认（背包/装备后续接入模拟）
                Debug.Log("[NetHostLogic] 购买 Mod " + id + "（暂存背包，后续接入模拟）");
            }

            m_Service.SendToClient(sessionId, new S2CShopOffer { Count = 0, Items = "BUY_OK" });
        }

        private void HandleJoin(int sessionId, C2SJoinRoom join)
        {
            if (join == null)
            {
                return;
            }

            if (m_Players.Count >= 4)
            {
                Debug.Log("[NetHostLogic] 房间已满，拒绝 " + join.PlayerName);
                return;
            }

            // 幂等：同一连接重复加入时，先移除旧实体并广播删除
            if (m_Players.TryGetValue(sessionId, out var oldState))
            {
                m_Players.Remove(sessionId);
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = oldState.EntityId });
                Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 重复加入，移除旧实体 " + oldState.EntityId);
            }

            var state = new PlayerState
            {
                SessionId = sessionId,
                EntityId = m_NextEntityId++,
                PlayerName = join.PlayerName ?? "玩家",
                CharacterId = join.CharacterId > 0 ? join.CharacterId : 1,
            };
            m_Players[sessionId] = state;

            // 房间阶段模拟：加入新玩家（移动由模拟同步）
            EnsureRoomSimulation();
            AddPlayerToSimulation(state);

            // 先告知新加入者自己的实体 ID，再广播 spawn（避免客户端先渲染自己）
            m_Service.SendToClient(sessionId, new S2CMyEntity { EntityId = state.EntityId });

            var joined = new S2CPlayerJoined { PlayerId = sessionId, PlayerName = join.PlayerName };
            m_Service.BroadcastToClients(joined);

            var spawn = new S2CSpawnEntity
            {
                EntityId = state.EntityId,
                Type = 0,
                Team = 1,
                X = 0f,
                Y = 0f,
                CharacterId = state.CharacterId,
            };
            m_Service.BroadcastToClients(spawn);

            // 广播已有玩家实体给新加入者（含房主），保证晚进客户端的可见性
            foreach (var kv in m_Players)
            {
                if (kv.Key == sessionId)
                {
                    continue;
                }
                var p = kv.Value;
                m_Service.SendToClient(sessionId, new S2CSpawnEntity
                {
                    EntityId = p.EntityId,
                    Type = 0,
                    Team = 1,
                    X = 0f,
                    Y = 0f,
                    CharacterId = p.CharacterId,
                });
            }

            var room = new S2CRoomState { RoomId = "ROOM-001", PlayerCount = m_Players.Count };
            m_Service.BroadcastToClients(room);

            BroadcastPlayerList();

            Debug.Log(string.Format("[NetHostLogic] 玩家 {0}({1}) 加入，生成实体 {2}",
                join.PlayerName, sessionId, state.EntityId));
        }

        private void HandleLeave(int sessionId)
        {
            if (m_Players.Remove(sessionId, out var state))
            {
                m_LatestInputs.Remove(sessionId);
                if (Simulation != null)
                {
                    Simulation.RemovePlayer(state.EntityId);
                }
                m_Service.BroadcastToClients(new S2CPlayerLeft { PlayerId = sessionId });
                m_Service.BroadcastToClients(new S2CRemoveEntity { EntityId = state.EntityId });
                Debug.Log("[NetHostLogic] 玩家 " + sessionId + " 离开，实体 " + state.EntityId + " 移除，广播已发送");
                BroadcastPlayerList();
            }
        }

        /// <summary>当前模拟中最近玩家的位置（敌人生成锚点用，保留接口）。</summary>
        public Vector2 GetSimulationAnchorPosition()
        {
            if (Simulation == null || Simulation.Players.Count == 0)
            {
                return Vector2.zero;
            }
            foreach (var p in Simulation.Players)
            {
                if (p.Alive)
                {
                    return p.Position;
                }
            }
            return Simulation.Players[0].Position;
        }

        private void OnDestroy()
        {
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
    }
}
