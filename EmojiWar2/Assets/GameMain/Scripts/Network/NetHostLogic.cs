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

        // W-06：各客户端上报的装备 Id（sessionId → ids）。房主本机（session 0）不入表，
        //       需要时直接读本机 ItemSystem（见 GetLoadoutIds）。
        private readonly Dictionary<int, Simulation.PlayerLoadoutIds> m_Loadouts =
            new Dictionary<int, Simulation.PlayerLoadoutIds>();

        // W-06：广播用的有序列表（复用，避免开局时每帧分配）
        private readonly List<Simulation.PlayerLoadoutIds> m_LoadoutBroadcastList =
            new List<Simulation.PlayerLoadoutIds>();

        // 最近收到的输入意图（sessionId → input；掉线玩家缺省）
        private readonly Dictionary<int, C2SPlayerInput> m_LatestInputs = new Dictionary<int, C2SPlayerInput>();

        // ---- [W-12] 输入新鲜度与托管（宿主侧） ----
        // `m_LastInputFrame[session]` = 上一次**消费**到的客户端**发送序号**（`C2SPlayerInput.SendSeq`）；
        // `m_InputStaleFrames[session]` = 连续多少帧没有收到"更新的"输入。
        // 用发送序号而不是"客户端帧号"：客户端在开战/回房间时会重建模拟、帧号归零，
        // 而这里的"上次消费值"还是旧的大值 → 之后每条输入都被判成过期 → 玩家被**永久托管**
        // （实测症状：客户端角色整场停在原点，而两端哈希一致所以不报不同步）。
        private readonly Dictionary<int, int> m_LastInputFrame = new Dictionary<int, int>();
        private readonly Dictionary<int, int> m_InputStaleFrames = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> m_ManagedPlayers = new Dictionary<int, bool>();

        private const float ManagedTimeoutSeconds = 0.5f;

        /// <summary>
        /// [W-12] 连续多少帧没有新输入就判定为"托管"。用**秒**表达（0.5s）再按当前帧率量化 ——
        /// 与 W-10a 同口径：这样切帧率不会改变"断线多久算掉线"。
        /// ⚠ 必须用**完全限定名**：本类的实例属性 `Simulation` 会**遮蔽**同名命名空间
        /// `EmojiWar.GameMain.Simulation`，写成 `Simulation.CastResolver` 会被解析成属性访问
        /// （静态字段初始化器里直接编译不过：CS0236）。
        /// </summary>
        private static readonly int ManagedTimeoutFrames =
            EmojiWar.GameMain.Simulation.CastResolver.FramesOf(ManagedTimeoutSeconds);

        /// <summary>[W-12] 读/写某会话的托管标志；返回是否发生了**状态翻转**（用于只在翻转时打日志）。</summary>
        private bool SetManaged(int sessionId, bool value)
        {
            bool old;
            if (!m_ManagedPlayers.TryGetValue(sessionId, out old)) { old = false; }
            m_ManagedPlayers[sessionId] = value;
            return old != value;
        }

        private bool IsManaged(int sessionId)
        {
            bool v;
            return m_ManagedPlayers.TryGetValue(sessionId, out v) && v;
        }

        /// <summary>当前处于托管的玩家数（HUD/探针用）。</summary>
        public int ManagedPlayerCount
        {
            get
            {
                int n = 0;
                foreach (var kv in m_ManagedPlayers) { if (kv.Value) { n++; } }
                return n;
            }
        }

        private NetworkService m_Service = null;
        private int m_NextEntityId = 1000;

        /// <summary>确定性模拟（Host 本地也作为一端参与推演）。</summary>
        public Simulation.LockstepSimulation Simulation { get; private set; }

        /// <summary>战斗是否已开始（tick 循环是否运行）。</summary>
        public bool BattleRunning { get; private set; }

        // tick 循环（20Hz）
        private float m_TickAccumulator = 0f;

        // 定期状态对账计数（每 20 tick = 1 秒广播一次状态哈希）
        private int m_StateCheckCounter = 0;

        [Header("联机")]
        [SerializeField]
        private int m_ExpectedPlayers = 1;   // ★ 期待人数：未达标前即使全部已准备也不开战（防"一人开战"）

        [Header("波次配置")]
        [SerializeField]
        private int m_EnemiesPerWave = 3;

        [SerializeField]
        private float m_SpawnRadius = 8f;

        private bool m_BattleStartBroadcasted = false;
        private bool m_LocalAutoMove = true;   // Host 本地无输入时自动转圈（回环测试用；真实联机由 DisableLocalAutoMove 关闭）
        private bool m_AutoFire = false;        // -autofire：自动按住左键（验证无限释放/连发）

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
                m_LastInputFrame.Clear();      // [W-12]
                m_InputStaleFrames.Clear();
                m_ManagedPlayers.Clear();
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

        /// <summary>Host 本机切换角色（不走网络；直接走同一处理链，广播给客户端）。</summary>
        public void SetLocalCharacter(int characterId)
        {
            if (m_Players.TryGetValue(0, out var state) && state.CharacterId != characterId)
            {
                HandleChangeCharacter(0, new C2SChangeCharacter { CharacterId = characterId });
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

        /// <summary>设置期望人数（联机测试/房主设置用；下限 1 = 保持单人可玩）。</summary>
        public void SetExpectedPlayers(int n)
        {
            m_ExpectedPlayers = Mathf.Max(1, n);
            WriteProbe("[net-host] 期望人数设为 " + m_ExpectedPlayers);
        }

        /// <summary>当前期望人数（下限 1）。</summary>
        public int ExpectedPlayers { get { return Mathf.Max(1, m_ExpectedPlayers); } }

        /// <summary>房主名（session 0 玩家；用于房间发现广播）。</summary>
        public string GetHostName()
        {
            return m_Players.TryGetValue(0, out var s) ? s.PlayerName : "房主";
        }

        /// <summary>关闭 Host 本地空闲自动移动（正式联机）。</summary>
        public void DisableLocalAutoMove()
        {
            m_LocalAutoMove = false;
        }

        /// <summary>开启自动开火（-autofire 自动化验证用：模拟持续按住左键）。</summary>
        public void EnableAutoFire()
        {
            m_AutoFire = true;
        }

        /// <summary>
        /// 每帧：驱动 20Hz 逻辑 tick。
        /// </summary>
        private void Update()
        {
            // 局域网房间发现：Host 处于"房间阶段"（未开战/非战斗中）时应答扫描请求。
            // 开战（BattleStart 后）不再应答——战斗中不可再加入新玩家。
            // 非 Host / 无模拟时停止监听。
            bool inRoomPhase = m_Service != null && m_Service.Mode == NetMode.Host
                && BattleRunning && !m_BattleStartBroadcasted;
            if (inRoomPhase)
            {
                RoomDiscovery.TickAdvertiser(true, GetHostName(), m_Players.Count, NetworkService.DefaultPort);
            }
            else
            {
                RoomDiscovery.TickAdvertiser(false, null, 0, NetworkService.DefaultPort);
            }

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

            // [W-17] 插值系数改由**本地时间轴**提供（与客户端同一机制）：
            // t = 距下一次 tick 的剩余时间 / tick 步长 ∈ [0,1)。
            // 宿主原先也走 SimView 的"墙钟自算"，那条路在"一帧内跑多个 tick"时会反复把 t 归零
            // （本文件上面的注释就是在解释这个现象）→ 两端现在共用同一条基准，语义一致。
            if (GameEntry.SimView != null)
            {
                GameEntry.SimView.ExternalInterpolation = true;
                GameEntry.SimView.InterpolationFactor = Mathf.Clamp01(m_TickAccumulator / LockstepSim.TickInterval);
            }

            // [W-15] 本机即时反馈：把输入快照推给表现层（房主同样有 D 帧延迟，也需要即时反馈）
            PushLocalFeedback();

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
                      .Append(" fps=").Append(fps.ToString("F0"))
                      .Append(" managed=").Append(ManagedPlayerCount)   // [W-12] 托管人数
                      .Append(" D=").Append(m_HostInputDelayFrames)     // [W-14] 房主输入延迟（帧）
                      .Append(" clientLag=").Append(m_ClientLagMax);
                    foreach (var p in Simulation.Players)
                    {
                        sb.Append(" E").Append(p.EntityId).Append(":(")
                          .Append(p.Position.x.ToString("F2")).Append(",")
                          .Append(p.Position.y.ToString("F2")).Append(")");
                        // [W-12] 每个玩家的托管状态（与模拟状态同源，可与客户端逐项对照）
                        if (p.Managed) { sb.Append("[托管]"); }
                    }
                    WriteProbe(sb.ToString());
                    // W-01：逻辑帧耗时与消费节奏（H1/E1 的验收数字，构建版 exe 也能读）
                    // 注意：本类有属性 `Simulation`，表达式位置会遮蔽同名命名空间，故必须全限定。
                    WriteProbe("[perf] HOST " + EmojiWar.GameMain.Simulation.SimPerf.Describe());
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
                    Managed = new bool[m_Players.Count],
                };
            }
            var frame = m_InputFrame;
            if (Simulation == null)
            {
                return;   // 防御：ResetRoom 等切换窗口期 BattleRunning 仍 true 但模拟已置空
            }
            frame.FrameIndex = Simulation.FrameIndex + 1;
            frame.Count = m_Players.Count;

            // [W-16] 把本帧要执行的确定性事件**随这一帧**携带，并立刻应用到自己的模拟 ——
            // 于是两端都在**同一个帧号**的帧首应用它（客户端在消费该帧时应用，见 StepOneLogicalFrame）。
            // ⚠ 输入帧对象是**复用**的，所以每帧都必须显式清空/重填，否则上一帧的事件会一直带着。
            if (frame.Events == null) { frame.Events = new List<Simulation.FrameEvent>(4); }
            frame.Events.Clear();
            for (int i = 0; i < m_PendingFrameEvents.Count; i++)
            {
                Simulation.FrameEvent e = m_PendingFrameEvents[i];
                frame.Events.Add(e);
                FrameEventApplier.Apply(Simulation, e, "host");
                WriteProbe("[net-host] 帧 " + frame.FrameIndex + " 携带事件 kind=" + e.Kind
                    + " a0=" + e.Arg0 + " a1=" + e.Arg1);
            }
            m_PendingFrameEvents.Clear();

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
                    // ---- [W-14] 房主自己的输入也要延迟 D 帧 ----
                    // 问题：房主打包输入帧时同一 tick 就生效（延迟 ≈ 0 帧），而客户端从按下到生效
                    // 要经过 RTT/2 + 1 帧 → 两端**手感不一样**（报告 D1/D2）。
                    // 做法：把"本帧采样到的本地意图"存进按帧号索引的历史，实际用的是 **D 帧之前** 那一条。
                    // D 由**观测到的客户端滞后**决定（见 UpdateInputDelayFromLag）：客户端输入里带着
                    // "它采样时的本地帧号"，`装配帧号 - 采样帧号` 就是"客户端的输入要等几帧才被我应用"，
                    // 这正是房主该等的帧数 —— 不需要心跳测量，这个数字本身就是端到端的。
                    SimIntent sampled = ReadLocalInput();
                    m_LocalIntentHistory[frame.FrameIndex] = sampled;

                    int useFrame = frame.FrameIndex - m_HostInputDelayFrames;
                    SimIntent delayed;
                    if (m_LocalIntentHistory.TryGetValue(useFrame, out delayed)) { intent = delayed; }
                    else { intent = SimIntent.Empty; }   // 历史不足（刚开局）：空输入，避免"提前"生效

                    // 历史裁剪（只保留最近 D+4 帧）
                    PruneLocalIntentHistory(frame.FrameIndex - m_HostInputDelayFrames - 4);
                }
                else if (m_LatestInputs.TryGetValue(sessionId, out var clientInput))
                {
                    // ---- [W-12] 输入新鲜度判定 ----
                    // 原实现是"最近一次到达的输入无限沿用"，且注释写着"掉线托管：空输入" ——
                    // 实际只有"**从未**收到过输入"才会走 else 分支，真掉线时反而会**永远沿用最后一个输入**，
                    // 表现为"玩家卡在按住开火/一直在走"。现在按"这条输入是否比我上次消费的更新"来判。
                    int lastConsumed;
                    if (!m_LastInputFrame.TryGetValue(sessionId, out lastConsumed)) { lastConsumed = -1; }

                    bool fresh = clientInput.SendSeq > lastConsumed;
                    if (fresh)
                    {
                        m_LastInputFrame[sessionId] = clientInput.SendSeq;
                        m_InputStaleFrames[sessionId] = 0;
                        // [W-14] 用这条新鲜输入的滞后驱动"房主自身输入延迟 D"
                        UpdateInputDelayFromLag(frame.FrameIndex, clientInput.FrameIndex);
                        if (SetManaged(sessionId, false))
                        {
                            WriteProbe("[net-host] 玩家 " + sessionId + " 输入恢复，退出托管（entity=" + state.EntityId + "）");
                        }
                    }
                    else
                    {
                        int stale;
                        m_InputStaleFrames.TryGetValue(sessionId, out stale);
                        stale++;
                        m_InputStaleFrames[sessionId] = stale;
                    }

                    int staleNow;
                    m_InputStaleFrames.TryGetValue(sessionId, out staleNow);
                    bool managed = staleNow > ManagedTimeoutFrames;

                    if (managed)
                    {
                        // 断线超时 → 空输入代打（**不再沿用**玩家的按键，否则会"幽灵开火/幽灵走位"）
                        intent = SimIntent.Empty;
                        if (SetManaged(sessionId, true))
                        {
                            WriteProbe("[net-host] 玩家 " + sessionId + " 进入托管（连续 " + staleNow
                                + " 帧无新输入 ≥ " + ManagedTimeoutSeconds.ToString("F2") + "s / "
                                + ManagedTimeoutFrames + " 帧）；entity=" + state.EntityId);
                            Debug.LogWarning("[NetHostLogic] 玩家 " + sessionId + " 进入托管（输入源已断）");
                        }
                    }
                    else if (staleNow > 0)
                    {
                        // 短暂缺失（抖动/丢包重传）：**沿用上一帧的"按住"状态，但边沿位清零**。
                        // 保持"按住"是有意的（不然连发会被抖没），清边沿是为了不让"上一次的按下"
                        // 被重复计入未来若干帧（那会让单次点击变成连点）。
                        intent = new SimIntent
                        {
                            MoveX = clientInput.InputX,
                            MoveY = clientInput.InputY,
                            AimX = clientInput.AimX,
                            AimY = clientInput.AimY,
                            FirePrimary = clientInput.FirePrimary,
                            FireSecondary = clientInput.FireSecondary,
                            Reload = false,
                        };
                    }
                    else
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
                }
                else
                {
                    // 从未收到过输入（刚加入/输入源从未建立）：空输入
                    intent = SimIntent.Empty;
                }

                // [W-12] 托管标志随帧广播 → 两端同值 → 可以安全进状态哈希
                intent.Managed = IsManaged(sessionId);

                // 填充广播帧（以实体 ID 标识，客户端据此匹配本地模拟玩家）
                frame.EntityIds[index] = state.EntityId;
                frame.InputXs[index] = intent.MoveX;
                frame.InputYs[index] = intent.MoveY;
                frame.AimXs[index] = intent.AimX;
                frame.AimYs[index] = intent.AimY;
                frame.FirePrimaries[index] = intent.FirePrimary;
                frame.FireSecondaries[index] = intent.FireSecondary;
                frame.Reloads[index] = intent.Reload;
                frame.Managed[index] = intent.Managed;   // [W-12]
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
                // W-03 / W-16：连同**进入该帧时的状态哈希**与**本帧的帧事件**一起写。
                // 此处仍在 Simulation.Tick 之前 —— 与 ReplayPlayer 里 beforeHash/应用事件 的口径一致，
                // 两处必须保持"Tick 前算哈希、Tick 前应用事件"这个约定。
                EmojiWar.GameMain.Simulation.ReplayRecorder.RecordFrame(
                    frame.FrameIndex, Simulation.ComputeStateHash(),
                    frame.Events, entityList, intentList);
            }

            // 本地推进模拟（复用输入字典）
            if (Simulation != null)
            {
                Simulation.Tick(m_InputsCache);

                // 定期状态对账：每 20 tick（1 秒）广播一次本端确定性状态哈希。
                // 客户端在同一逻辑帧算本地哈希对比，不等即不同步（TCP 保序保证客户端处理 StateCheck 前
                // 必已处理同帧的 InputFrame 并 tick 到该帧，故 FrameIndex 恒等）。
                // 仅战斗开始后启用：房间阶段各端玩家加入（S2CSpawnEntity）时序不同，哈希天然不等，
                // 不是不同步；BattleStart 后全端同 seed + 同玩家集重建，模拟同构才可对账。
                m_StateCheckCounter++;
                if (m_StateCheckCounter >= 20 && m_BattleStartBroadcasted)
                {
                    m_StateCheckCounter = 0;
                    if (m_Service != null && m_Service.Mode == NetMode.Host)
                    {
                        m_Service.BroadcastToClients(new S2CStateCheck
                        {
                            FrameIndex = Simulation.FrameIndex,
                            StateHash = Simulation.ComputeStateHash(),
                        });
                    }
                }
            }
        }

        /// <summary>读取 Host 本地玩家输入意图（WASD + 鼠标瞄准 + 左键射击；无输入时自动转圈供测试）。
        /// 弹药已取消（无限释放）：R 键装弹采集已移除，Reload 字段恒 false（模拟层忽略）。</summary>
        private Simulation.PlayerIntent ReadLocalInput()
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

            if (m_LocalAutoMove && inputX == 0f && inputY == 0f)
            {
                // 确定性自动转圈（用模拟帧号而非 Time.time，保证各端输入序列一致 —— 帧同步要求）
                // 相位步长取**当前 tick 时长**：这样"转一圈的真实时间"与帧率无关（W-10a 切 30Hz 后仍然一致）
                float frame = Simulation != null ? Simulation.FrameIndex : Time.frameCount;
                float phase = EmojiWar.GameMain.Simulation.LockstepSimulation.TickInterval;
                inputX = Mathf.Cos(frame * phase);
                inputY = Mathf.Sin(frame * phase);
            }

            // 自动开火（-autofire 验证用）：无人操作时按住左键，验证无限释放；
            // 同时按住右键以验证**副武器**逻辑（主/副各自独立冷却）。
            bool fire = Input.GetMouseButton(0);
            bool fireSecondary = Input.GetMouseButton(1);
            if (m_AutoFire)
            {
                if (!fire) { fire = true; }
                if (!fireSecondary) { fireSecondary = true; }
            }

            // 鼠标瞄准（世界坐标方向）
            Vector3 mouseWorld = Vector3.zero;
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mouseWorld = mainCam.ScreenToWorldPoint(Input.mousePosition);
            }
            Vector2 aim = new Vector2(mouseWorld.x, mouseWorld.y);

            // [W-15] 记录按下沿（供表现层即时反馈比边沿）与本机鼠标世界坐标
            if (fire && !m_LocalPrevFire) { m_LocalFirePressCount++; }
            if (fireSecondary && !m_LocalPrevFire2) { m_LocalFire2PressCount++; }
            m_LocalPrevFire = fire;
            m_LocalPrevFire2 = fireSecondary;
            m_LocalAimWorldX = aim.x;
            m_LocalAimWorldY = aim.y;

            return new Simulation.PlayerIntent
            {
                MoveX = inputX,
                MoveY = inputY,
                AimX = aim.x,
                AimY = aim.y,
                FirePrimary = fire,
                FireSecondary = fireSecondary,
                Reload = false,
            };
        }

        // ---- [W-15] 本机即时反馈：按下沿计数 + 鼠标世界坐标（表现层只读）----
        private int m_LocalFirePressCount = 0;
        private int m_LocalFire2PressCount = 0;
        private bool m_LocalPrevFire = false;
        private bool m_LocalPrevFire2 = false;
        private float m_LocalAimWorldX = 0f;
        private float m_LocalAimWorldY = 0f;

        /// <summary>
        /// [W-15] 把本机输入快照推给表现层。**房主也有"点击到生效"的延迟**（W-14 让房主自己也等 D 帧），
        /// 所以即时反馈对房主同样必要 —— 而且它让"本地按下"和"权威效果"的帧差可被观测：
        /// 按下瞬间出声，权威子弹在 D 帧（+ 客户端还要加上网络与缓冲）之后才出现。
        /// </summary>
        private void PushLocalFeedback()
        {
            var v = GameEntry.SimView;
            if (v == null) { return; }
            var f = new Simulation.SimView.LocalInputFeedback();
            f.Valid = true;
            f.AimWorldX = m_LocalAimWorldX;
            f.AimWorldY = m_LocalAimWorldY;
            f.FirePressCount = m_LocalFirePressCount;
            f.FireSecondaryPressCount = m_LocalFire2PressCount;
            v.LocalInput = f;
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
                        // [W-12] 只**收下**这条输入。
                        // ⚠ 绝不能在这里更新"已消费序号"账本：账本记录的是"装配输入帧时**消费**到哪一条"，
                        //   如果在到达时就更新，装配循环里的 `SendSeq > lastConsumed` 会**恒为 false**
                        //   → 每一帧都被判成"无新输入" → 玩家被永久托管（实测：客户端角色冻结在原点）。
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

                case MsgId.ChangeCharacterReq:
                    HandleChangeCharacter(sessionId, message as C2SChangeCharacter);
                    break;

                case MsgId.ShopContinueReq:
                    RequestShopContinue();          // 客户端点"继续"→ Host 权威推进并广播
                    break;

                case MsgId.LoadoutSync:             // W-06：客户端上报自己的装备 Id
                    HandleLoadoutSync(sessionId, message as C2SLoadoutSync);
                    break;
            }
        }

        /// <summary>
        /// 商店阶段"继续"：Host 权威开始下一波，并广播各端做同样的确定性推进。
        /// （修复：此前商店靠 ShopDuration 计时自动开下一波，导致波间商店阶段错误刷敌人。）
        /// </summary>
        public void RequestShopContinue()
        {
            if (Simulation == null || !Simulation.ShopOpen) { return; }

            // W-03 / 报告 A9：**入队**而不是直接改模拟。
            // 直接调 RequestNextWave() 会让指令绕过帧管线 —— 后果：录像只记输入流，回放复现不了
            // （实测回放分歧恰好在第 501 帧 = 录像里第一个敌人生成的帧，因为回放一直停在商店）。
            // 现在指令在**下一帧首**按入队顺序消费，录像把它一起记下 → "输入流 + 指令流"才完整。
            //
            // ★ [W-13/W-16] 但"入队"还不够：这条命令必须**随输入帧携带**才会落在两端同一个帧号上。
            //   以前它靠"TCP 流内顺序"隐式对齐（S2CShopContinue 夹在帧 H 与 H+1 之间，客户端
            //   收到即 tick 完第 H 帧 → 命令正好在第 H+1 帧生效）。客户端消费被抖动缓冲接管后，
            //   消息处理与 tick 不再同步 → 命令会落在**缓冲区深度那么多个帧之后**（实测第 721 帧起
            //   持续不同步，缓冲区 2 帧 → 差 2 帧）。
            //   所以改成：房主只**登记事件**，装配输入帧时把它写进**这一帧**并同时应用到自己的模拟。
            QueueFrameEvent(EmojiWar.GameMain.Simulation.FrameEventKinds.ShopContinue, 0, 0);

            if (m_Service != null) { m_Service.BroadcastToClients(new S2CShopContinue()); }
            WriteProbe("[net-host] 商店继续事件已登记（将随下一帧输入帧携带，两端同帧生效）wave=" + Simulation.WaveIndex);
        }

        // ---- [W-14] 房主自身输入延迟 D（帧）与客户端滞后观测 ----
        /// <summary>按帧号保存"房主本帧采样到的意图"，实际生效的是 D 帧之前那一条。</summary>
        private readonly Dictionary<int, SimIntent> m_LocalIntentHistory = new Dictionary<int, SimIntent>();
        /// <summary>当前生效的房主输入延迟（帧）。由观测到的客户端滞后驱动，夹在 [2,6]。</summary>
        private int m_HostInputDelayFrames = 2;
        /// <summary>观测到的客户端滞后上界（帧，带每帧 1 帧的衰减，避免一次抖动把 D 永久抬高）。</summary>
        private int m_ClientLagMax = 0;
        private int m_ClientLagSamples = 0;
        private const int InputDelayMin = 2;
        private const int InputDelayMax = 6;

        /// <summary>[W-14] 用"客户端输入滞后"驱动 D：客户端采样帧 f 的输入会在装配帧 F 被应用，
        /// `F - f` 就是"客户端要等几帧"；房主等同样多，两端手感才一致。</summary>
        private void UpdateInputDelayFromLag(int assemblyFrame, int clientSampledFrame)
        {
            int lag = assemblyFrame - clientSampledFrame;
            if (lag < 0) { lag = 0; }
            if (lag > InputDelayMax + 4) { lag = InputDelayMax + 4; }   // 异常值不参与
            m_ClientLagSamples++;
            if (lag > m_ClientLagMax)
            {
                m_ClientLagMax = lag;
            }
            else if (m_ClientLagMax > 0)
            {
                m_ClientLagMax--;   // 每帧衰减 1 → 网络恢复后 D 会自动降回来
            }

            int want = m_ClientLagMax;
            if (want < InputDelayMin) { want = InputDelayMin; }
            if (want > InputDelayMax) { want = InputDelayMax; }
            if (want != m_HostInputDelayFrames)
            {
                m_HostInputDelayFrames = want;
                WriteProbe("[w14] 房主输入延迟 D=" + want + " 帧（观测客户端滞后上界 "
                    + m_ClientLagMax + " 帧，样本 " + m_ClientLagSamples + "）");
            }
        }

        private void PruneLocalIntentHistory(int olderThanFrame)
        {
            if (m_LocalIntentHistory.Count <= 8) { return; }
            m_ScratchFrames.Clear();
            foreach (var kv in m_LocalIntentHistory) { if (kv.Key < olderThanFrame) { m_ScratchFrames.Add(kv.Key); } }
            for (int i = 0; i < m_ScratchFrames.Count; i++) { m_LocalIntentHistory.Remove(m_ScratchFrames[i]); }
        }

        private readonly List<int> m_ScratchFrames = new List<int>();

        /// <summary>[W-14] 当前房主输入延迟（帧）——探针/验收用。</summary>
        public int HostInputDelayFrames { get { return m_HostInputDelayFrames; } }

        // [W-16] 待随帧携带的确定性事件（帧事件批）。**所有改变模拟的带外指令都走这里**，
        // 不允许在消息处理里直接改模拟 —— 那会让生效帧号取决于"消息什么时候到"。
        private readonly List<Simulation.FrameEvent> m_PendingFrameEvents = new List<Simulation.FrameEvent>();

        /// <summary>[W-16] 登记一条随帧携带的确定性事件。</summary>
        private void QueueFrameEvent(byte kind, int arg0, int arg1)
        {
            m_PendingFrameEvents.Add(new Simulation.FrameEvent { Kind = kind, Arg0 = arg0, Arg1 = arg1 });
        }

        /// <summary>
        /// 处理房间内切换角色：更新玩家状态 + 广播 S2CChangeCharacter（各端同步模拟）。
        /// 仅房间阶段生效（战斗开始后禁止）。
        /// </summary>
        private void HandleChangeCharacter(int sessionId, C2SChangeCharacter change)
        {
            if (change == null || !m_Players.TryGetValue(sessionId, out var state))
            {
                return;
            }
            if (m_BattleStartBroadcasted)
            {
                WriteProbe("[net-host] ChangeCharacter ignored (battle already started)");
                return;
            }
            if (change.CharacterId <= 0)
            {
                return;
            }

            state.CharacterId = change.CharacterId;

            // [W-16] 改为**随帧携带**：直接 `Simulation.ApplyCharacter` 会让"何时生效"取决于
            // 消息处理时刻 —— 房主立刻生效、客户端等网络+缓冲后才生效，中间那几帧两端角色不同
            // （MoveSpeed/WeaponId 等都会跟着变 → 分叉）。现在统一在帧首应用，两端同帧。
            QueueFrameEvent(EmojiWar.GameMain.Simulation.FrameEventKinds.SetCharacter, state.EntityId, state.CharacterId);

            // 广播：所有端（含客户端）同步更新
            m_Service.BroadcastToClients(new S2CChangeCharacter
            {
                EntityId = state.EntityId,
                CharacterId = state.CharacterId,
            });

            // 玩家列表（emoji/名字/准备状态）随角色变化一起刷新（本机 + 客户端 Cell 更新）
            BroadcastPlayerList();

            WriteProbe("[net-host] 玩家 " + sessionId + " 切换角色 -> " + state.CharacterId);
        }

        /// <summary>主动广播一次当前玩家列表（RoomForm 打开后调用，让本机/客户端立即可见已有玩家）。</summary>
        public void BroadcastPlayerListNow()
        {
            BroadcastPlayerList();
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

            // ★ 以前是 `m_Players.Count >= 1 && AllReady()` —— 房间里只有房主一人时该条件即为真，
            //   房主一准备就立刻开战，后加入的客户端永远收不到 S2CBattleStart（2026-09-28 实测 P0：
            //   两端两个世界：Host wave=1/enemies=3，Client wave=0/enemies=0，且零告警）。
            // W-06：开战前提还包括"所有玩家的装备 Id 已上报"，而它可能晚于"准备齐"到齐，
            //       所以判断收敛到可重入的 TryStartBattle（HandleLoadoutSync 到达时也会调一次）。
            if (m_Players.Count >= ExpectedPlayers && AllReady())
            {
                TryStartBattle();
            }
        }

        /// <summary>
        /// W-06：真正开战（幂等）。由 HandleReadyChange 与 HandleLoadoutSync 共同触发。
        ///
        /// **必须先广播装备 Id、再广播 BattleStart**：客户端收到 BattleStart 时就会用它建战斗模拟，
        /// TCP 保序保证客户端此时已拿到全部 Id。
        /// </summary>
        private void TryStartBattle()
        {
            if (m_BattleStartBroadcasted) { return; }
            if (m_Players.Count < ExpectedPlayers || !AllReady()) { return; }

            if (!AllLoadoutsReported())
            {
                WriteProbe("[net-host] 等待客户端上报装备 Id（尚未齐备），暂不开战");
                return;
            }

            // --- 原开战逻辑（下列裸块只是保持原有缩进层次，无其他含义）---
            {
                m_BattleStartBroadcasted = true;
                int seed = UnityEngine.Random.Range(0, 100000);

                // [W-12] 开战前清空输入新鲜度账本：房间阶段的"上次消费序号/缺帧计数/托管标志"
                //   对战斗模拟没有意义，带过去只会制造"开局就被判过期"的假象（防御性清空）。
                m_LastInputFrame.Clear();
                m_InputStaleFrames.Clear();
                m_ManagedPlayers.Clear();
                m_LocalIntentHistory.Clear();   // [W-14]
                m_ClientLagMax = 0;

                BroadcastLoadouts();   // ★ 必须在 BattleStart 之前（客户端建模拟时就要用）
                m_Service.BroadcastToClients(new S2CBattleStart { Seed = seed });
                Debug.Log("[NetHostLogic] 全部玩家已准备，广播战斗开始 seed=" + seed);
                WriteProbe("[net-host] 全部准备，广播 BattleStart seed=" + seed);

                // 战斗开始：用新种子重建确定性模拟（所有端一致），然后进**准备阶段商店**
                // （WaveIndex=0，不出敌人）；玩家点"继续"才 StartWave(1)
                InitializeSimulation(seed);
                Simulation.PrepareFirstWave();

                // 输入流录像开始（文档 §3：初始状态 + 输入流）
                try
                {
                    var configs = new List<Simulation.SimPlayerConfig>();
                    foreach (var kv in m_Players)
                    {
                        configs.Add(BuildPlayerConfig(kv.Value));
                    }
                    // W-03：录像头改为**自包含** —— 数值配置 + 战斗参数 + 装备 Id 表 + 逐帧哈希，
                    // 否则录像根本无法复现（旧格式不写 loadout，而 loadout 决定弹道/mana）。
                    var loadoutList = new List<Simulation.PlayerLoadoutIds>();
                    foreach (var kv in m_Players)
                    {
                        var idsForRecord = GetLoadoutIds(kv.Key, kv.Value.EntityId);
                        idsForRecord.EntityId = kv.Value.EntityId;
                        loadoutList.Add(idsForRecord);
                    }
                    loadoutList.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

                    EmojiWar.GameMain.Simulation.ReplayRecorder.Begin(
                        seed, EmojiWar.GameMain.Simulation.SimBuildInfo.Describe,
                        EmojiWar.GameMain.Data.ConfigService.VersionHash,
                        Simulation, configs,
                        EmojiWar.GameMain.Simulation.LoadoutWire.Encode(loadoutList),
                        System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/replays"));
                    WriteProbe("[net-host] 输入流录像开始 seed=" + seed
                        + " loadouts=" + EmojiWar.GameMain.Simulation.LoadoutWire.Encode(loadoutList));

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
            // 注入波次平衡参数（BattleConfigSO；双端同资产同值 → 确定性一致）
            Simulation.ApplyBattleConfig(GameEntry.Data != null ? GameEntry.Data.Battle : null);

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

        /// <summary>
        /// W-06：从**玩家上报的装备 Id** 构建确定性玩家配置（联机路径唯一入口）。
        /// 不再读本机背包 —— 那正是报告 A10 的分叉源（Host 用自己的杖为**所有**玩家编译）。
        /// </summary>
        private Simulation.SimPlayerConfig BuildPlayerConfig(PlayerState p)
        {
            return EmojiWar.GameMain.Simulation.SimConfigFactory.BuildFromIds(
                p.SessionId, p.EntityId, p.CharacterId, GetLoadoutIds(p.SessionId, p.EntityId));
        }

        /// <summary>取某玩家的装备 Id。房主本机（session 0）直接读本机；其余取上报值（缺失 = 空手）。</summary>
        private Simulation.PlayerLoadoutIds GetLoadoutIds(int sessionId, int entityId)
        {
            if (sessionId == 0)
            {
                return EmojiWar.GameMain.Simulation.SimConfigFactory.ReadLocalLoadoutIds(entityId);
            }
            Simulation.PlayerLoadoutIds ids;
            return m_Loadouts.TryGetValue(sessionId, out ids) ? ids : default(Simulation.PlayerLoadoutIds);
        }

        /// <summary>
        /// 是否所有客户端都已上报装备 Id。
        /// 未齐不开战：否则该玩家会用"空手 loadout"参战，而它自己用真实 loadout → 静默分叉。
        /// </summary>
        private bool AllLoadoutsReported()
        {
            foreach (var kv in m_Players)
            {
                if (kv.Key == 0) { continue; }                  // 房主本机随时可读，不需要上报
                if (!m_Loadouts.ContainsKey(kv.Key)) { return false; }
            }
            return true;
        }

        /// <summary>W-06：收客户端上报的装备 Id 并缓存，随后立即转发给所有端。</summary>
        private void HandleLoadoutSync(int sessionId, C2SLoadoutSync msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.HandIds)) { return; }

            // 客户端发的是"含自己 entityId 的单条"（复用同一套线格式）
            var one = new List<Simulation.PlayerLoadoutIds>(1);
            if (!EmojiWar.GameMain.Simulation.LoadoutWire.TryDecode(msg.HandIds, one) || one.Count != 1)
            {
                WriteProbe("[net-host] ⚠ 装备 Id 解析失败，忽略 session=" + sessionId + " raw=" + msg.HandIds);
                return;
            }

            PlayerState state;
            if (!m_Players.TryGetValue(sessionId, out state))
            {
                WriteProbe("[net-host] ⚠ 收到未加入玩家的装备 Id，忽略 session=" + sessionId);
                return;
            }

            Simulation.PlayerLoadoutIds ids = one[0];
            if (ids.EntityId != state.EntityId)
            {
                WriteProbe("[net-host] 装备 Id 的 entity 不匹配（msg=" + ids.EntityId
                    + " expect=" + state.EntityId + "）→ 以 Host 分配为准");
            }
            ids.EntityId = state.EntityId;      // 以 Host 的分配为准
            m_Loadouts[sessionId] = ids;

            WriteProbe(string.Format(
                "[net-host] 收到装备 Id session={0} entity={1} 左杖#{2}({3}槽) 右杖#{4}({5}槽)",
                sessionId, ids.EntityId, ids.WandLeftItemId,
                ids.LeftSpellItemIds != null ? ids.LeftSpellItemIds.Length : 0,
                ids.WandRightItemId, ids.RightSpellItemIds != null ? ids.RightSpellItemIds.Length : 0));

            BroadcastLoadouts();   // 立即转发，让其它端与后来加入者都拿到

            // 装备 Id 可能晚于"准备齐"到齐 → 这里再触发一次开战判断（TryStartBattle 幂等）
            TryStartBattle();
        }

        /// <summary>
        /// W-06：把全员的装备 Id 广播给所有客户端。
        /// 按 EntityId 升序（跨端顺序一致），房主本机读实时值（背包可能还在改）。
        /// </summary>
        private void BroadcastLoadouts()
        {
            if (m_Service == null) { return; }

            m_LoadoutBroadcastList.Clear();
            foreach (var kv in m_Players)
            {
                var ids = GetLoadoutIds(kv.Key, kv.Value.EntityId);
                ids.EntityId = kv.Value.EntityId;    // 房主本机读取时 EntityId 已正确；此处兜底
                m_LoadoutBroadcastList.Add(ids);
            }
            m_LoadoutBroadcastList.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            var msgOut = new S2CLoadoutBroadcast
            {
                Loadouts = EmojiWar.GameMain.Simulation.LoadoutWire.Encode(m_LoadoutBroadcastList),
            };
            m_Service.BroadcastToClients(msgOut);
            WriteProbe("[net-host] 广播装备 Id: " + msgOut.Loadouts);
        }

        /// <summary>回到房间（一局结束后）：重置准备状态与模拟，等待下一局。</summary>
        public void ResetRoom()
        {
            // 输入流录像结束（强制 flush，防尾帧丢失）+ 打点结束
            EmojiWar.GameMain.Simulation.ReplayRecorder.End();
            EmojiWar.GameMain.Simulation.DeterminismTracer.End();

            // [W-09] 一局结束 → 清空物品系统（背包/手部容器跨局残留 = 第二局 loadout 污染）。
            //   客户端在 HandleRunRestart 里对称执行；下一局进入战斗时重新 GrantStartingLoadout。
            ItemSystem.Reset();

            m_BattleStartBroadcasted = false;
            BattleRunning = false;
            Simulation = null;
            m_LatestInputs.Clear();
            m_LastInputFrame.Clear();      // [W-12]
            m_InputStaleFrames.Clear();
            m_ManagedPlayers.Clear();
            m_LocalIntentHistory.Clear();  // [W-14]
            m_ClientLagMax = 0;
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

        /// <summary>广播房间玩家列表（"名字:准备:角色ID;..."，角色 ID 供 UI 显示角色 emoji）。</summary>
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
                sb.Append(p.PlayerName).Append(':').Append(p.Ready ? "1" : "0")
                  .Append(':').Append(p.CharacterId);
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

                // [W-16] 改为**随帧携带**：购买那一刻直接改模拟会让两端生效帧号不同
                // （这三个字段都进了状态哈希 → 会被对账抓到）。事件只带 Id，两端各自查表
                // 构造完全相同的配置（口径同 W-06：协议传 Id，不传解算后的数值）。
                QueueFrameEvent(EmojiWar.GameMain.Simulation.FrameEventKinds.WeaponUpdate, state.EntityId, weapon.Id);

                m_Service.BroadcastToClients(new S2CWeaponUpdate
                {
                    EntityId = state.EntityId,
                    WeaponId = weapon.Id,
                    WeaponName = weapon.WeaponName,
                    Damage = weapon.Damage,
                    FireRate = weapon.FireRate,
                    BulletSpeed = weapon.BulletSpeed,
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

            // ★ [W-07] 配置/代码握手：不一致就**明确拒绝**，而不是放进对局里"不同步"。
            //   配置哈希只覆盖配置资产；代码指纹覆盖"这份逻辑代码是哪一次编译产生的" + 逻辑帧率。
            //   两者任一不一致，对局 100% 会漂移，且现象是"玩家看到不同步但两端日志都正常"——
            //   正是本次审计反复踩的坑，所以在最早的入口一刀切掉。
            ulong myConfig = EmojiWar.GameMain.Data.ConfigService.VersionHash;
            ulong myCode = EmojiWar.GameMain.Simulation.SimBuildInfo.CodeHash;

            if (join.ConfigHash != myConfig)
            {
                string cfgReason = "配置版本不一致：房主 0x" + myConfig.ToString("X16")
                    + " / 你 0x" + join.ConfigHash.ToString("X16")
                    + "（两端游戏版本或配置资产不同，无法开始对局）";
                m_Service.SendToClient(sessionId, new S2CJoinRejected { Reason = cfgReason });
                WriteProbe("[net-host] 拒绝 " + sessionId + " 加入：配置哈希不一致 host=0x" + myConfig.ToString("X16")
                    + " client=0x" + join.ConfigHash.ToString("X16"));
                Debug.LogWarning("[NetHostLogic] 拒绝加入（配置不一致）: " + cfgReason);
                return;
            }

            if (join.CodeHash != myCode)
            {
                string codeReason = "游戏版本不一致：房主 0x" + myCode.ToString("X16")
                    + " / 你 0x" + join.CodeHash.ToString("X16")
                    + "（两端不是同一次构建，逻辑代码不同，无法开始对局）";
                m_Service.SendToClient(sessionId, new S2CJoinRejected { Reason = codeReason });
                WriteProbe("[net-host] 拒绝 " + sessionId + " 加入：代码指纹不一致 host=0x" + myCode.ToString("X16")
                    + " client=0x" + join.CodeHash.ToString("X16"));
                Debug.LogWarning("[NetHostLogic] 拒绝加入（代码不一致）: " + codeReason);
                return;
            }

            // ★ 对局已开始后拒绝中途加入。
            //   以前这里只查人数上限，于是新客户端被分配实体、收到 spawn，
            //   却永远收不到 S2CBattleStart（只在 HandleReadyChange 里一次性发出）→ 它停在 seed=0 房间模拟，
            //   而房主照常广播战斗输入帧 → 两端两个世界且零告警（2026-09-28 实测 P0）。
            if (m_BattleStartBroadcasted)
            {
                m_Service.SendToClient(sessionId, new S2CJoinRejected { Reason = "对局已开始，无法加入" });
                WriteProbe("[net-host] 拒绝 " + sessionId + " 中途加入（对局已开始）");
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

            // W-06：新加入者需要立刻拿到"现有玩家的装备 Id"（它自己也还没上报，稍后会补一次广播）
            BroadcastLoadouts();

            Debug.Log(string.Format("[NetHostLogic] 玩家 {0}({1}) 加入，生成实体 {2}",
                join.PlayerName, sessionId, state.EntityId));
        }

        private void HandleLeave(int sessionId)
        {
            if (m_Players.Remove(sessionId, out var state))
            {
                m_LatestInputs.Remove(sessionId);
                m_LastInputFrame.Remove(sessionId);       // [W-12]
                m_InputStaleFrames.Remove(sessionId);
                m_ManagedPlayers.Remove(sessionId);
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

        /// <summary>当前模拟中最近玩家的位置（敌人生成锚点用，保留接口）。
        /// [W-04] 模拟层坐标是 `SimVec2`（不引用 UnityEngine）→ 出模拟层时显式转换。</summary>
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
                    return new Vector2(p.Position.x, p.Position.y);
                }
            }
            return new Vector2(Simulation.Players[0].Position.x, Simulation.Players[0].Position.y);
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
