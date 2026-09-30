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
using System.Globalization;
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

        /// <summary>
        /// 表现层视图。**必须指向真正被绑定的那一份**（`GameEntry.SimView`）。
        ///
        /// ⚠ 原先这里是一个**从未被赋值**的自动属性（`{ get; private set; }`，全文件没有任何 `View = …`），
        /// 于是所有 `View != null` 判断恒为 false → 依赖它的两处写入**静默失效**：
        ///   · W-13「追帧期关插值」（`SnapToLatest`）
        ///   · W-17「插值系数由本地时间轴提供」（`ExternalInterpolation`）
        /// 是探针里打印的 `t=-` 把它暴露出来的（"写入静默落空"正是本次审计反复出现的缺陷类型）。
        /// 做成实时访问器后，历史调用点全部自动生效。
        /// </summary>
        public Simulation.SimView View { get { return GameEntry.SimView; } }

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

        // W-06：全员装备 Id（Host 广播 → 所有端据此编译 loadout，不再读本机背包）
        private readonly List<Simulation.PlayerLoadoutIds> m_Loadouts = new List<Simulation.PlayerLoadoutIds>();

        // W-06：是否正在构建**战斗**配置。房间阶段（seed=0）缺装备 Id 属正常时序（广播还没到），
        //       只有战斗阶段缺 Id 才需要大声告警 —— 否则告警会被噪声淹没（实测：一局误报 2 条）。
        private bool m_BuildingBattleSim = false;

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

        /// <summary>发送准备/取消准备请求（顺带重报装备 Id：房间内改过背包的话以此刻为准）。</summary>
        public void SendReady(bool ready)
        {
            if (m_Service != null)
            {
                SendLoadoutSync();   // W-06：准备 = "我改完了"，此刻重报最准
                m_Service.Send(new C2SReadyChange { Ready = ready });
                WriteProbe("[net] 发送准备状态: " + ready);
            }
        }

        /// <summary>
        /// W-06：上报本机装备 Id（只传物品 Id，不传资产引用/字符串名）。
        /// 加入成功时与每次"准备"时各发一次，保证 Host 在开战前收齐。
        /// </summary>
        public void SendLoadoutSync()
        {
            if (m_Service == null || !m_Service.IsConnected || m_MyEntityId < 0)
            {
                return;
            }

            // 注意：本类有属性 `Simulation`，**表达式位置**会遮蔽同名命名空间 → 静态调用必须全限定
            // （类型位置如 `Simulation.PlayerLoadoutIds ids;` 不受影响，编译器按类型解析）
            var mine = EmojiWar.GameMain.Simulation.SimConfigFactory.ReadLocalLoadoutIds(m_MyEntityId);
            var one = new List<Simulation.PlayerLoadoutIds>(1) { mine };
            string wire = EmojiWar.GameMain.Simulation.LoadoutWire.Encode(one);

            m_Service.Send(new C2SLoadoutSync { HandIds = wire });
            WriteProbe("[net] 上报装备 Id: " + wire);
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
            ResetFrameQueue();   // [W-13] 模拟没了，队列里的帧也不再有意义
        }

        /// <summary>房间内切换角色：上报 Host（Host 广播同步全端）。</summary>
        public void RequestChangeCharacter(int characterId)
        {
            if (m_Service != null && m_Service.IsConnected)
            {
                m_Service.Send(new C2SChangeCharacter { CharacterId = characterId });
                WriteProbe("[net] 发送切换角色 char=" + characterId);
            }
        }

        /// <summary>商店阶段"继续"：上报 Host（Host 权威开始下一波并广播 S2CShopContinue）。</summary>
        public void RequestShopContinue()
        {
            if (m_Service != null && m_Service.IsConnected)
            {
                m_Service.Send(new C2SShopContinue());
                WriteProbe("[net] 发送商店继续请求");
            }
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

            // 插值系数不再在此设置：SimView.LateUpdate 检测模拟帧号推进自算插值时间
            // （文档 §2 渲染插值），避免与 NetworkService.Update 的接收推进顺序错位导致位置回退抖动。

            // ---- [W-13] 按本地时间轴消费输入帧队列（原来的"收到即 Tick"已移到这里）----
            ConsumeFrames(Time.unscaledDeltaTime);

            // ---- [W-15] 把本机输入快照推给表现层（只读；表现层据此做即时反馈）----
            PushLocalFeedback();

            // 连接建立后补发加入房间请求（异步连接时序）
            if (!m_JoinSent && m_Service.IsConnected)
            {
                m_JoinSent = true;
                m_JoinConfirmed = false;
                m_JoinSendTime = Time.realtimeSinceStartup;
                m_Service.Send(MakeJoinRoom());
                WriteProbe("[net] 发送 C2SJoinRoom: " + m_PlayerName + " char=" + GetLocalCharacterId()
                    + " configHash=0x" + GetLocalConfigHash().ToString("X16")
                    + " codeHash=0x" + EmojiWar.GameMain.Simulation.SimBuildInfo.CodeHash.ToString("X16"));
                // [W-11b] 握手被拒时，必须能一眼分辨"代码不同源"还是"同源但不同构建" ——
                // 构建标识不参与握手，所以只作诊断输出（Mono=MVID / IL2CPP=buildGUID）。
                WriteProbe("[net] 本端构建标识: " + EmojiWar.GameMain.Simulation.SimBuildInfo.BuildId);
                Debug.Log("[NetClientLogic] 发送加入房间请求: " + m_PlayerName);
            }

            // 加入未确认（可能被连接时序丢弃）：3 秒后重发
            if (m_JoinSent && !m_JoinConfirmed && Time.realtimeSinceStartup - m_JoinSendTime > JoinConfirmTimeout)
            {
                m_JoinSent = false;
                WriteProbe("[net] JoinRoom 3 秒未确认，重发");
                Debug.Log("[NetClientLogic] JoinRoom 未确认，重发");
            }

            // 帧同步一致性探针（每 2 秒记录一次模拟状态 + fps，供双实例对比；在输入节流之前执行）
            m_ProbeTimer -= Time.unscaledDeltaTime;
            if (m_ProbeTimer <= 0f)
            {
                m_ProbeTimer = 2f;
                float fps = m_FpsAccumTime > 0f ? m_FpsAccumFrames / m_FpsAccumTime : 0f;
                m_FpsAccumFrames = 0;
                m_FpsAccumTime = 0f;
                if (Simulation != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[sim] CLIENT frame=").Append(Simulation.FrameIndex)
                      .Append(" wave=").Append(Simulation.WaveIndex)
                      .Append(" players=").Append(Simulation.Players.Count)
                      .Append(" enemies=").Append(Simulation.Enemies.Count)
                      .Append(" bullets=").Append(Simulation.Bullets.Count)
                      .Append(" fps=").Append(fps.ToString("F0"))
                      // [W-13] 消费节奏：队列深度、单渲染帧最多执行几个逻辑帧、饥饿帧数、追帧中
                      .Append(" q=").Append(m_FrameQueue.Count)
                      .Append(" maxStep=").Append(m_MaxStepsObserved)
                      .Append(" starved=").Append(m_StarvedFrames)
                      .Append(" catchUp=").Append(m_CatchingUp ? 1 : 0)
                      .Append(" dropped=").Append(m_FrameQueue.OverflowDroppedCount);
                    foreach (var p in Simulation.Players)
                    {
                        sb.Append(" E").Append(p.EntityId).Append(":(")
                          .Append(p.Position.x.ToString("F2")).Append(",")
                          .Append(p.Position.y.ToString("F2")).Append(")");
                    }
                    WriteProbe(sb.ToString());
                    // [W-13] 消费节奏单列一行（避免 [sim] 行太长；也便于门禁只抓这一行）
                    WriteProbe("[w13] queue=" + m_FrameQueue.Count + " target=" + m_TargetDepth
                        + " avgQ=" + AverageQueueDepth.ToString("F2")
                        + " maxQ=" + m_MaxQueueDepth
                        + " maxStepsPerRenderFrame=" + m_MaxStepsObserved + " steps=" + m_StepsTotal
                        + " starved=" + m_StarvedFrames + " catchingUp=" + (m_CatchingUp ? 1 : 0)
                        + " dup=" + m_FrameQueue.DuplicateCount + " overflowDrop=" + m_FrameQueue.OverflowDroppedCount
                        + " gaps=" + m_InputGapEvents
                        // [W-17] 插值基准验收：本机渲染位置的"方向翻转"次数应为 0
                        + " t=" + (View != null ? View.InterpolationFactor.ToString("F2") : "-")
                        + " reversals=" + m_RenderReversals + "/" + m_RenderMotionSamples);
                    // 注意：**不**在这里 ResetDepthWindow() —— avgQ 是累计均值（见 AverageQueueDepth 的说明），
                    // 按窗口重置会被一次 GC 停顿造成的突发污染成"缓冲失控"的假象（实测踩过）。
                    // W-01：逻辑帧耗时与消费节奏（客户端侧 E1 抖动验收的关键数字）
                    // 注意：本类有属性 `Simulation`，表达式位置会遮蔽同名命名空间，故必须全限定。
                    WriteProbe("[perf] CLIENT " + EmojiWar.GameMain.Simulation.SimPerf.Describe());
                }
            }
            else
            {
                m_FpsAccumFrames++;
                m_FpsAccumTime += Time.unscaledDeltaTime;
            }

            // ---- [W-12] 每渲染帧都**采样**（发送仍按 tick 节流）----
            // 原实现把"采样"和"发送"绑在同一个 30Hz 节流里：于是两次发送之间的**边沿输入被丢弃**
            // （`GetKeyDown` 只在按下那一帧为 true，而那一帧可能正好落在节流窗口内）。
            // 现在：每帧采样并累积边沿位，发送成功后清空 —— "按下"一定会被送到房主一次。
            SampleLocalIntent();

            m_InputSendTimer -= Time.deltaTime;
            if (m_InputSendTimer > 0f)
            {
                return;
            }
            m_InputSendTimer = LockstepSim.TickInterval;

            // [W-12] 验收钩子：`-stopsendinginputs <startSec> <durSec>` —— 在指定时间窗内**停止上行输入**，
            // 用来可复现地验证房主的"输入源断了 → 托管"路径（不需要真的拔网线）。
            EnsureInputHooksParsed();
            if (s_StopSendDuration > 0f)
            {
                float t = Time.realtimeSinceStartup;
                if (t >= s_StopSendStart && t < s_StopSendStart + s_StopSendDuration)
                {
                    if (!m_StopSendLogged)
                    {
                        m_StopSendLogged = true;
                        WriteProbe("[net] [W-12] 钩子：停止上行输入 " + s_StopSendDuration.ToString("F1")
                            + " 秒（验证房主托管路径）");
                    }
                    return;
                }
                if (m_StopSendLogged && t >= s_StopSendStart + s_StopSendDuration)
                {
                    m_StopSendLogged = false;
                    WriteProbe("[net] [W-12] 钩子：恢复上行输入");
                }
            }

            // 复用输入消息对象（30Hz 上行，避免高频对象分配；Send 内同步序列化后即可复用）
            if (m_InputMsg == null)
            {
                m_InputMsg = new C2SPlayerInput();
            }
            m_InputMsg.InputX = m_SampledInputX;
            m_InputMsg.InputY = m_SampledInputY;
            m_InputMsg.AimX = m_SampledAimX;
            m_InputMsg.AimY = m_SampledAimY;
            m_InputMsg.FirePrimary = m_SampledFirePrimary;
            m_InputMsg.FireSecondary = m_SampledFireSecondary;
            m_InputMsg.Reload = m_SampledReload;

            // [W-12] 带上"这条输入是为哪一帧采样的"（诊断用）与**单调发送序号**（房主判新鲜度用）
            m_InputMsg.FrameIndex = Simulation != null ? Simulation.FrameIndex + 1 : 0;
            m_InputMsg.SendSeq = ++m_InputSendSeq;
            m_InputMsg.EdgeFlags = m_PendingEdges;

            m_Service.Send(m_InputMsg);

            // 发送成功 → 边沿位已送达，清空（**必须在 Send 之后**：Send 失败也要留到下次）
            m_PendingEdges = 0;
            m_SampledReload = false;   // Reload 是"一次性"意图，不能粘到下一帧
        }

        /// <summary>
        /// [W-12] 采样本渲染帧的本地意图，并把**边沿**位或累积到 <see cref="m_PendingEdges"/>。
        /// 每帧都调用；发送时读出累积值并清空。这样"按一下鼠标"不会因为恰好落在节流窗口里而丢失。
        /// </summary>
        private void SampleLocalIntent()
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

            // 无键盘输入时自动转圈（回环验证用；真实联机由 DisableAutoMove 关闭）
            if (m_AutoMoveWhenIdle && inputX == 0f && inputY == 0f)
            {
                // 确定性自动转圈（用本地模拟帧号而非 Time.time，保证输入序列确定）
                // 相位步长取**当前 tick 时长**：转一圈的真实时间与帧率无关（W-10a 切 30Hz 后仍一致）
                float frame = Simulation != null ? Simulation.FrameIndex : Time.frameCount;
                float phase = LockstepSim.TickInterval;
                inputX = Mathf.Cos(frame * phase);
                inputY = Mathf.Sin(frame * phase);
            }
            m_SampledInputX = inputX;
            m_SampledInputY = inputY;

            // 鼠标瞄准（世界坐标）
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 mouseWorld = mainCam.ScreenToWorldPoint(Input.mousePosition);
                m_SampledAimX = mouseWorld.x;
                m_SampledAimY = mouseWorld.y;
            }

            bool fire1 = Input.GetMouseButton(0);
            bool fire2 = Input.GetMouseButton(1);
            // [W-15] 验收钩子：`-autotap <半周期帧数>` —— 用**帧号**产生方波"按下/松开"，
            // 从而在自动化跑测里得到多次**按下沿**（`-autofire` 是一直按住，只有一个沿）。
            // 用帧号而非墙钟：输入值会被房主原样广播，两端拿到的仍是同一份数据，确定性不受影响。
            if (AutoTapHalfPeriod > 0 && Simulation != null)
            {
                fire1 = ((Simulation.FrameIndex / AutoTapHalfPeriod) % 2) == 0;
            }
            if (fire1 && !m_SampledFirePrimary) { m_PendingEdges |= EdgeFire1Down; m_FirePressCount++; }   // [W-15] 计数供即时反馈比边沿
            if (!fire1 && m_SampledFirePrimary) { m_PendingEdges |= EdgeFire1Up; }
            if (fire2 && !m_SampledFireSecondary) { m_PendingEdges |= EdgeFire2Down; m_Fire2PressCount++; } // [W-15]
            if (!fire2 && m_SampledFireSecondary) { m_PendingEdges |= EdgeFire2Up; }
            m_SampledFirePrimary = fire1;
            m_SampledFireSecondary = fire2;

            if (Input.GetKeyDown(KeyCode.R)) { m_SampledReload = true; }
        }

        // ---- [W-12] 输入采样状态（每渲染帧更新；发送时读出）----
        private const byte EdgeFire1Down = 1 << 0;
        private const byte EdgeFire1Up = 1 << 1;
        private const byte EdgeFire2Down = 1 << 2;
        private const byte EdgeFire2Up = 1 << 3;

        private byte m_PendingEdges = 0;          // 本发送窗口累积的边沿位（发送后清空）
        private int m_FirePressCount = 0;         // [W-15] 主手"按下"累计次数（单调递增 → 表现层比边沿）
        private int m_Fire2PressCount = 0;        // [W-15] 副手同上
        /// <summary>[W-15] `-autotap <半周期帧数>` 验收钩子（0 = 关闭）。</summary>
        private static int AutoTapHalfPeriod
        {
            get
            {
                EnsureInputHooksParsed();
                return s_AutoTapHalfPeriod;
            }
        }
        private static int s_AutoTapHalfPeriod = 0;
        private int m_InputSendSeq = 0;           // [W-12] 单调递增的输入发送序号（跨战斗重建也不归零）
        private float m_SampledInputX = 0f;
        private float m_SampledInputY = 0f;
        private float m_SampledAimX = 0f;
        private float m_SampledAimY = 0f;
        private bool m_SampledFirePrimary = false;
        private bool m_SampledFireSecondary = false;
        private bool m_SampledReload = false;

        private float m_ProbeTimer = 2f;
        private int m_FpsAccumFrames = 0;
        private float m_FpsAccumTime = 0f;
        // ---- [W-16] 验收钩子：-autobuy（商店一开就买第一件）----
        // 用途：让"买到武器 → 两端同帧生效"这条路径可以端到端验证。
        // 买到的武器会改 WeaponDamage/FireRate/BulletSpeed（都已进状态哈希），
        // 所以只要两端生效帧号不同，门禁的"对账一致/不同步"就会立刻报出来。
        private static bool s_AutoBuyParsed = false;
        private static bool s_AutoBuy = false;
        private bool m_AutoBuyDoneThisShop = false;

        private static void EnsureAutoBuyParsed()
        {
            if (s_AutoBuyParsed) { return; }
            s_AutoBuyParsed = true;
            try
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-autobuy") { s_AutoBuy = true; }
                }
            }
            catch (System.Exception) { }
        }

        private float m_LastFrameTime = 0f;                 // 上次接收输入帧时间（渲染插值基准）
        // ---- [W-12] 输入帧缺口统计（用于把"不同步"归因到传输层而不是模拟层）----
        private int m_InputGapFrames = 0;
        private int m_InputGapEvents = 0;

        // ---- [W-13] 逻辑帧队列与消费调度器 ----
        private readonly Simulation.FrameQueue<S2CInputFrame> m_FrameQueue =
            new Simulation.FrameQueue<S2CInputFrame>(32);
        /// <summary>目标缓冲深度（帧）。2 帧 ≈ 2 个 tick 的抖动余量；代价是同量级的输入延迟。</summary>
        private int m_TargetDepth = 2;
        private float m_FrameAccumulator = 0f;      // 本地时间轴累加器
        private bool m_CatchingUp = false;          // 追帧状态（落后太多）
        private int m_StarvedFrames = 0;            // 队列空（无帧可推进）的渲染帧数
        private int m_StepsTotal = 0;               // 累计执行的逻辑帧数
        private int m_MaxStepsObserved = 0;         // 单渲染帧内最多执行了几个逻辑帧（W-13 核心指标）
        private long m_DepthSum = 0;                // 本探针窗口内的队列深度累计（算平均延迟用）
        private int m_DepthSamples = 0;
        private int m_MaxQueueDepth = 0;            // 战斗阶段观察到的最大队列深度（缓冲是否失控）
        /// <summary>[W-13] 深度统计的预热帧数（跳过开战那次 GC 停顿造成的瞬态）。2 秒 @30Hz。</summary>
        private const int DepthWarmupSteps = 60;

        // ---- [W-17] 插值基准与"无回退"验收 ----
        private float m_CurrentStepSeconds = 0f;
        private Vector2 m_LastRenderPos = Vector2.zero;
        private Vector2 m_LastRenderDelta = Vector2.zero;
        private int m_RenderMotionSamples = 0;
        private int m_RenderReversals = 0;

        /// <summary>
        /// [W-17] 用与 `SimView` **同一个公式**算出本机玩家的渲染位置，统计"方向翻转"次数。
        /// 判据来源：平滑前进的路径上，相邻两次位移的点积应 > 0；翻转说明位置在**回退**，
        /// 也就是"插值基准错位"的典型症状（原实现读墙钟，批量推进时 t 反复归零 → 位置回退抖动）。
        /// 探针只输出**计数**而不是每帧坐标，避免刷爆日志。
        /// </summary>
        private void TrackRenderMotion()
        {
            if (Simulation == null) { return; }
            if (m_StepsTotal < DepthWarmupSteps) { return; }   // 预热期（开战瞬移）不计

            var me = Simulation.GetPlayerByEntityId(m_MyEntityId);
            if (me == null) { return; }

            float t = View != null ? View.InterpolationFactor : 1f;
            Vector2 pos = new Vector2(
                me.PrevPosition.x + (me.Position.x - me.PrevPosition.x) * t,
                me.PrevPosition.y + (me.Position.y - me.PrevPosition.y) * t);

            if (m_RenderMotionSamples > 0)
            {
                Vector2 delta = pos - m_LastRenderPos;
                // 只统计"确实在移动"的帧：静止时位移≈0，点积无意义
                if (delta.sqrMagnitude > 1e-8f && m_LastRenderDelta.sqrMagnitude > 1e-8f)
                {
                    if (Vector2.Dot(delta, m_LastRenderDelta) < 0f) { m_RenderReversals++; }
                }
                m_LastRenderDelta = delta;
            }
            m_LastRenderPos = pos;
            m_RenderMotionSamples++;
        }

        /// <summary>[W-17] 本机渲染位置"方向翻转"次数（应为 0）。</summary>
        public int RenderReversals { get { return m_RenderReversals; } }
        /// <summary>[W-17] 参与统计的渲染帧数。</summary>
        public int RenderMotionSamples { get { return m_RenderMotionSamples; } }
        private const int MaxStepsPerRenderFrame = 3;   // 每渲染帧最多推进几个逻辑帧（预算）

        /// <summary>[W-13] 队列深度（探针/HUD 用）。</summary>
        public int FrameQueueDepth { get { return m_FrameQueue.Count; } }
        /// <summary>[W-13] 单渲染帧内最多执行过的逻辑帧数（应 ≤ MaxStepsPerRenderFrame）。</summary>
        public int MaxStepsPerRenderFrameObserved { get { return m_MaxStepsObserved; } }

        // ---- [W-12] 验收钩子：-stopsendinginputs <startSec> <durSec> ----
        private static bool s_InputHooksParsed = false;
        private static float s_StopSendStart = 0f;
        private static float s_StopSendDuration = 0f;
        private bool m_StopSendLogged = false;

        private static void EnsureInputHooksParsed()
        {
            if (s_InputHooksParsed) { return; }
            s_InputHooksParsed = true;
            try
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-stopsendinginputs" && i + 2 < args.Length)
                    {
                        float a, b;
                        if (float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                            && float.TryParse(args[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                        {
                            s_StopSendStart = a;
                            s_StopSendDuration = b;
                        }
                    }
                    else if (args[i] == "-autotap" && i + 1 < args.Length)
                    {
                        int half;
                        if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out half) && half > 0)
                        {
                            s_AutoTapHalfPeriod = half;
                        }
                    }
                }
            }
            catch (System.Exception) { /* 参数解析失败不影响正常流程 */ }
        }

        /// <summary>[W-12] 累计输入帧缺口（帧数）与次数（探针/对账告警归因用）。</summary>
        public int InputGapFrames { get { return m_InputGapFrames; } }
        public int InputGapEvents { get { return m_InputGapEvents; } }
        private C2SPlayerInput m_InputMsg = null;   // 复用的上行输入消息（避免高频分配）
        private float m_InputSendTimer = 0f;        // 上行输入 20Hz 节流计时
        private readonly Dictionary<int, SimIntent> m_InputsCache = new Dictionary<int, SimIntent>(4);   // 复用输入帧字典

        // ---- 定期状态对账（帧哈希环）----
        private const int HashRingSize = 256;                  // 环容量（256 帧 ≈ 12.8 秒回溯窗口，覆盖 TCP 合帧/缓冲延迟）
        private readonly int[] m_HashRingFrame = new int[HashRingSize];    // 环：帧号（int.MinValue=空）
        private readonly long[] m_HashRingValue = new long[HashRingSize];  // 环：该帧状态哈希
        private int m_HashRingHead = 0;                                     // 下一个写入槽
        private int m_CheckFrame = -1;                          // 待校验帧号（-1 无）
        private long m_CheckHash = 0;                           // 待校验 Host 哈希
        private int m_DesyncCount = 0;                          // 累计不同步次数
        private bool m_OutOfSyncWithBattle = false;              // ★ 收到 StateCheck 却仍停在房间模拟 = 已脱节
        private int m_StaleFrameStreak = 0;                      // ★ 连续"帧号倒退"计数（脱节检测用）

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
                m_Service.Send(MakeJoinRoom());
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

                case MsgId.StateCheck:
                    HandleStateCheck(message as S2CStateCheck);
                    break;

                case MsgId.WeaponUpdate:
                    // [W-16] **不再在这里改模拟**：改模拟的时机必须由帧号决定（见 FrameEvent 说明）。
                    //   这里只做 UI/探针；真正的 ApplyWeapon 由随帧携带的 WeaponUpdate 事件在消费该帧时执行。
                    var wu = message as S2CWeaponUpdate;
                    if (wu != null)
                    {
                        WriteProbe("[net] 武器更新通知 -> entity " + wu.EntityId + " " + wu.WeaponName
                            + "（实际生效随输入帧携带）");
                    }
                    break;

                case MsgId.ShopContinue:
                    // [W-13] **不再在这里改模拟**：队列化消费之后，"消息什么时候被处理"与
                    //   "模拟推进到哪一帧"已经不同步，在这里入队会让命令晚若干帧生效（差 = 缓冲深度）。
                    //   命令现在随输入帧携带（S2CInputFrame.ShopContinue），在消费该帧时入队 → 两端同帧。
                    WriteProbe("[net-client] 收到商店继续通知（实际生效随输入帧携带）");
                    break;

                case MsgId.ChangeCharacter:
                    var cc = message as S2CChangeCharacter;
                    if (cc != null && Simulation != null)
                    {
                        // [W-16] **不再在这里改模拟**：换角色会改 MoveSpeed/WeaponId 等，
                        //   直接改会让生效帧号取决于消息到达时刻（房主立刻、客户端延迟）。
                        //   模拟改动由随帧携带的 SetCharacter 事件在消费该帧时执行；
                        //   这里只同步名册与 UI（名册用于"下一局"重建战斗模拟）。
                        // 若切的是本机：同步静态选择值。
                        // 注意：不能再次触发 CharacterDockEvents.Change —— 该事件被 ProcedureRoom
                        // 订阅用于"上行请求"，而本消息正是自己上行请求的 Host 回包（Host 广播给所有
                        // 客户端含发起者）；再次触发会造成"回包→再上行→再回包"无限循环（仅非主机发生，
                        // 主机走本地 SetLocalCharacter 无回包）。UI 标签已在点击卡片时刷新，回包无需再刷。
                        if (cc.EntityId == m_MyEntityId)
                        {
                            Procedure.ProcedureBattle.SelectedCharacterId = cc.CharacterId;
                        }
                        // S1 修复（2026-09-28 实测确认的代码级缺陷）：名册必须跟着角色变更更新。
                        // 以前这里只改模拟、不改 m_Roster，于是开战时 InitializeBattleSimulation
                        // 用**旧 charId** 建战斗模拟，而 Host 用新值 → MoveSpeed/WeaponId/Damage/
                        // FireRate/BulletSpeed 全不同，第 1 帧就分叉（只要房间里有人换过角色，不需要碰背包）。
                        m_Roster[cc.EntityId] = cc.CharacterId;
                        WriteProbe("[net] 角色变更 -> entity " + cc.EntityId + " char=" + cc.CharacterId
                            + "（名册已同步）");
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

                case MsgId.JoinRejected:
                    var jr = message as S2CJoinRejected;
                    string reason = jr != null ? jr.Reason : "未知原因";
                    WriteProbe("[net] 加入被拒绝: " + reason);
                    Debug.LogWarning("[NetClientLogic] 加入被拒绝: " + reason);
                    m_IntentionalLeave = true;
                    m_Joined = false;
                    m_Reconnecting = false;
                    ClearSimulation();
                    UI.RoomEvents.RoomClosed();   // 复用既有链路：ProcedureRoom.OnRoomClosed → 返回多人游戏页
                    break;

                case MsgId.LoadoutBroadcast:        // W-06：全员装备 Id，据此编译 loadout
                    var lb = message as S2CLoadoutBroadcast;
                    if (lb != null)
                    {
                        if (EmojiWar.GameMain.Simulation.LoadoutWire.TryDecode(lb.Loadouts, m_Loadouts))
                        {
                            WriteProbe("[net] 收到全员装备 Id（" + m_Loadouts.Count + " 人）: " + lb.Loadouts);
                        }
                        else
                        {
                            // 解析失败 → 宁可报错并留在房间，也不要带着"半套/空手 loadout"进战斗
                            m_Loadouts.Clear();
                            string err = "[net] ⚠ 全员装备 Id 解析失败，已清空（开战会因 loadout 不一致而分叉）raw=" + lb.Loadouts;
                            WriteProbe(err);
                            Debug.LogError("[NetClientLogic] " + err);
                        }
                    }
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

                        // W-06：立即上报本机装备 Id（只传 Id，不传资产）。
                        // 这样 Host 在开战前一定收齐，不需要靠"延迟开战"来等它。
                        SendLoadoutSync();
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

            // ★ 已确认脱节：冻结本地模拟，不再消费战斗输入帧。
            //   以前会让房间模拟继续 tick，甚至替玩家朝空气开火（2026-09-28 实测 bullets=2~3）。
            if (m_OutOfSyncWithBattle)
            {
                return;
            }

            // 帧号连续性守卫：TCP 保序下帧号应恒为 已入队最大帧号+1。
            //  - 重复/过期帧（<= 已入队最大帧号）：丢弃，防止重复推进导致双端错位。
            //  - 跳帧（> 已入队+1）：战斗阶段记录告警（不同步排查线索）；仍按收到的帧推进（不重放缺口帧输入，
            //    因为缺口意味着确定性已被破坏，重放也无法恢复——交给状态对账检测）。
            //    房间阶段迟到加入的客户端本地帧号落后是常态（自己加入后才建模拟），不告警。
            // ★ [W-13] 这里比较的是**已入队的最大帧号**，不再是本地模拟帧号：
            //   消费被调度器接管后，本地模拟会**有意落后**队列若干帧，拿本地帧号比较会把正常帧全判成过期。
            int lastQueued = m_FrameQueue.LastEnqueuedFrame;
            bool inBattle = Simulation.Seed != 0;
            if (!m_FrameQueue.Enqueue(frame.FrameIndex, frame))
            {
                WriteProbe("[net] 丢弃过期输入帧 recv=" + frame.FrameIndex + " 已入队=" + lastQueued);
                // ★ 脱节检测（比 StateCheck 版本更快，不依赖那 1 秒节奏）：
                //   房主帧号"倒退"= 它重置过模拟（开战 BattleStart，或重开 RunRestart）。
                //   正常房间阶段客户端帧号恒落后于房主（本地从 0 起、房主已跑了 H 帧），
                //   所以连续多次 <= 已入队帧 只可能是"我漏掉了那次重置"。
                if (Simulation.Seed == 0 && ++m_StaleFrameStreak >= 5 && !m_OutOfSyncWithBattle)
                {
                    m_OutOfSyncWithBattle = true;
                    string err = "[net] ⚠ 连续 " + m_StaleFrameStreak + " 帧帧号倒退（recv=" + frame.FrameIndex
                        + " 已入队=" + lastQueued + "）且本地仍是房间模拟（seed=0）→ 漏掉了 S2CBattleStart/RunRestart，已与对局脱节！";
                    WriteProbe(err);
                    Debug.LogError("[NetClientLogic] " + err);
                }
                return;
            }
            m_StaleFrameStreak = 0;
            if (inBattle && lastQueued >= 0 && frame.FrameIndex > lastQueued + 1)
            {
                // [W-12] 缺口：**不掩盖**，并且要能被后续告警归因。
                // 决策是"不做回滚"，所以缺口无法真正补回来（缺口意味着那几帧输入已经不存在了）；
                // 能做且必须做的是：**明确计数 + 归因**，让之后必然出现的"不同步"日志一眼看出根因，
                // 而不是只报一句"不同步"把人引向模拟层去找 bug（这正是 2026-09-28 那次排查的坑）。
                m_InputGapFrames += (frame.FrameIndex - lastQueued - 1);
                m_InputGapEvents++;
                WriteProbe("[net] ⚠ 输入帧缺口 recv=" + frame.FrameIndex + " 已入队=" + lastQueued
                    + " 缺 " + (frame.FrameIndex - lastQueued - 1) + " 帧（累计缺口 " + m_InputGapFrames
                    + " 帧 / " + m_InputGapEvents + " 次）→ 本地帧号将永久落后，对账会持续不一致");
            }

            m_LastFrameTime = Time.realtimeSinceStartup;   // 插值计时基准
        }

        /// <summary>
        /// [W-13] 按**本地时间轴**消费队列里的输入帧（每渲染帧调用）。
        ///
        /// 目标缓冲深度 <see cref="m_TargetDepth"/>（初始 2 帧）：队列比目标深就**轻微加速**消化、
        /// 比目标浅就**轻微减速**（±10% 步长）。这样：
        ///   · 网络抖动造成的"一次到达多帧"被摊平到多个渲染帧 → 不再"卡一下猛冲"；
        ///   · 有约 `T * TickInterval` 的缓冲垫住抖动（代价是同样量级的输入延迟，所以 T 从 2 起）。
        /// 预算：每渲染帧最多 3 帧、最多 4ms，且**只在完整逻辑帧边界检查时间**（与 Host 同款做法）。
        /// 队列空 → 不推进（累加器封顶，避免饥饿后爆发）。
        /// </summary>
        private void ConsumeFrames(float deltaTime)
        {
            if (Simulation == null)
            {
                return;
            }

            if (m_FrameQueue.Count == 0)
            {
                m_FrameAccumulator = 0f;      // 封顶：饥饿期间不累积，防止恢复后一次性爆发
                m_StarvedFrames++;
                // [W-17] 饥饿时**必须把插值停在最新逻辑帧**（t=1），不能让它掉到 0：
                //   否则渲染位置会从"上一逻辑帧→当前逻辑帧"的 0.9 处**往回缩**到上一帧位置，
                //   表现为"卡一下、往回弹一点"。实测：注入 30ms 抖动时饥饿 387 帧，
                //   由此产生 54 次方向翻转（0.83%）；停住不回缩后降到 0。
                if (View != null)
                {
                    View.ExternalInterpolation = true;
                    View.InterpolationFactor = 1f;
                }
                return;
            }

            // ---- 步长：**比例控制器**（把深度拉回目标）----
            // 为什么不是固定的 ±10%：到达率与消费率**平均相等**（都是逻辑帧率），所以任何突发都会被
            // 永久沉淀成额外延迟 —— ±10% 的净排出率（≈3 帧/秒）常常被下面的"累加器封顶"抵消掉，
            // 深度就一路涨上去（实测：目标 2 帧，实际稳定在 6~8 帧 = 200~270ms 额外延迟）。
            // 改成与"深度误差"成正比：误差越大排得越快（最多快 43%），误差为负时轻微放慢以回填缓冲。
            int depthErr = m_FrameQueue.Count - m_TargetDepth;
            float mult = 1f;
            if (depthErr > 0) { mult = 1f - Mathf.Min(0.30f, depthErr * 0.05f); }
            else if (depthErr < 0) { mult = 1f + Mathf.Min(0.05f, -depthErr * 0.02f); }
            float step = LockstepSim.TickInterval * mult;

            // 追帧状态：落后太多时进入（表现层关插值、只写最终位置，见 SimView.SnapToLatest）
            bool catchingUp = m_FrameQueue.Count > m_TargetDepth * 2;
            if (catchingUp != m_CatchingUp)
            {
                m_CatchingUp = catchingUp;
                if (View != null) { View.SnapToLatest = catchingUp; }
                WriteProbe("[net] [W-13] 追帧状态 " + (catchingUp ? "进入" : "退出")
                    + "（队列深度 " + m_FrameQueue.Count + "，目标 " + m_TargetDepth + "）");
            }

            m_FrameAccumulator += deltaTime;
            float cap = step * 3f;
            if (m_FrameAccumulator > cap) { m_FrameAccumulator = cap; }

            int stepped = 0;
            float budgetEnd = Time.realtimeSinceStartup + 0.004f;
            while (m_FrameAccumulator >= step && m_FrameQueue.Count > 0 && stepped < MaxStepsPerRenderFrame)
            {
                m_FrameAccumulator -= step;
                S2CInputFrame frame;
                if (!m_FrameQueue.TryDequeue(out frame)) { break; }
                StepOneLogicalFrame(frame);
                stepped++;
                if (m_FrameQueue.Count == 0) { break; }
                if (Time.realtimeSinceStartup >= budgetEnd) { break; }   // 只在完整帧边界检查预算
            }

            if (stepped > m_MaxStepsObserved) { m_MaxStepsObserved = stepped; }
            m_StepsTotal += stepped;

            // [W-17] 插值系数由**本地时间轴**给出：t = 累加器 / 本次步长 ∈ [0,1)
            // 语义 = "从上一逻辑帧走到当前逻辑帧的进度"。
            //
            // ⚠ 基准步长 `m_CurrentStepSeconds` **只在真正推进过逻辑帧时更新**（stepped > 0）：
            //   比例控制器每渲染帧都会重算 step，而 `t = acc / step` —— 如果 step 在没推进帧的时候
            //   变大（深度掉到目标以下 → mult 1.05），t 就会**变小**，渲染位置沿着
            //   "上一帧→当前帧"的线段**往回缩**。这就是残余方向翻转的来源（实测 24/6879）。
            //   锁定基准后，t 在两次 tick 之间单调增长，推进时换到新的一对 prev/cur（位置继续向前）。
            if (stepped > 0) { m_CurrentStepSeconds = step; }
            float interpStep = m_CurrentStepSeconds > 0f ? m_CurrentStepSeconds : step;
            if (View != null)
            {
                View.ExternalInterpolation = true;
                View.InterpolationFactor = interpStep > 0f ? Mathf.Clamp01(m_FrameAccumulator / interpStep) : 1f;
            }
            TrackRenderMotion();
            // [W-13] 预热期（开战那次 GC 停顿）不计入深度统计，见 DepthWarmupSteps 的说明
            if (m_StepsTotal >= DepthWarmupSteps)
            {
                m_DepthSum += m_FrameQueue.Count;
                m_DepthSamples++;
            }
            // [W-13] 预热期不计入深度统计：开战那一刻有一次**主动 `GC.Collect()`**（ProcedureBattle）
            // 加场景加载，渲染循环会停 ~200ms，期间帧照常到达 → 队列一口气堆到十几二十帧再排空。
            // 那是**一次性瞬态**，把它算进"稳态水位/最大深度"会让门禁指标随 GC 时机乱跳
            // （实测同机场景 avgQ 在 2.48 ~ 4.47 之间抖、maxQ 顶到 20）。
            if (m_StepsTotal >= DepthWarmupSteps && m_FrameQueue.Count > m_MaxQueueDepth)
            {
                m_MaxQueueDepth = m_FrameQueue.Count;
            }
        }

        /// <summary>
        /// [W-13] **累计**平均队列深度（自开战/重建模拟起，= 缓冲引入的平均额外延迟，单位=帧）。
        /// 刻意不做"每探针窗口重置"：窗口均值会被**一次瞬时突发**污染 ——
        /// 启动期的一次 GC 停顿（`ProcedureBattle` 里主动 `GC.Collect()`）会让十几帧一起到达，
        /// 那 2 秒窗口的均值就飙到 4~5 帧，看上去像"缓冲失控"，其实几帧后就排空了。
        /// 累计均值 + 最大值一起看，才能区分"稳态偏差"与"瞬时抖动"。
        /// </summary>
        public float AverageQueueDepth
        {
            get { return m_DepthSamples > 0 ? (float)m_DepthSum / m_DepthSamples : 0f; }
        }

        private void ResetDepthWindow()
        {
            m_DepthSum = 0;
            m_DepthSamples = 0;
        }

        /// <summary>[W-13] 推进**一个**逻辑帧：把该帧的输入塞进缓存并 Tick，随后记录哈希、做对账。</summary>
        private void StepOneLogicalFrame(S2CInputFrame frame)
        {
            if (Simulation == null || frame == null) { return; }

            // [W-16] 随帧携带的确定性事件：**在 Tick 之前**应用 → 本帧生效 → 与房主同帧。
            if (frame.Events != null)
            {
                for (int i = 0; i < frame.Events.Count; i++)
                {
                    Simulation.FrameEvent e = frame.Events[i];
                    FrameEventApplier.Apply(Simulation, e, "client");
                    WriteProbe("[net] 帧 " + frame.FrameIndex + " 应用事件 kind=" + e.Kind
                        + " a0=" + e.Arg0 + " a1=" + e.Arg1);
                }
            }

            // 复用输入字典（避免高频分配）
            m_InputsCache.Clear();
            for (int i = 0; i < frame.Count; i++)
            {
                int entityId = frame.EntityIds[i];
                m_InputsCache[entityId] = new SimIntent
                {
                    MoveX = frame.InputXs[i],
                    MoveY = frame.InputYs[i],
                    AimX = frame.AimXs[i],
                    AimY = frame.AimYs[i],
                    FirePrimary = frame.FirePrimaries[i],
                    FireSecondary = frame.FireSecondaries[i],
                    Reload = frame.Reloads[i],
                    // [W-12] 托管标志随帧到达 → 两端同值 → 进哈希一致
                    Managed = frame.Managed != null && i < frame.Managed.Length && frame.Managed[i],
                };
            }

            Simulation.Tick(m_InputsCache);

            // [W-16] 验收钩子：商店一开就买第一件（触发"武器更新"帧事件路径）
            EnsureAutoBuyParsed();
            if (s_AutoBuy)
            {
                if (Simulation.ShopOpen && !m_AutoBuyDoneThisShop)
                {
                    m_AutoBuyDoneThisShop = true;
                    WriteProbe("[net] [W-16] 钩子：商店已开，发送购买请求（index=0）");
                    RequestBuy(0);
                }
                else if (!Simulation.ShopOpen)
                {
                    m_AutoBuyDoneThisShop = false;
                }
            }

            // 记录本帧状态哈希到环形缓冲（定期对账用：StateCheck 到达时按帧号精确回查）
            RecordFrameHash(Simulation.FrameIndex);

            // 对账帧若恰好落在本 tick：从环中取该帧哈希对比
            if (m_CheckFrame >= 0 && Simulation.FrameIndex >= m_CheckFrame)
            {
                TryVerifyFrame(m_CheckFrame, m_CheckHash);
                m_CheckFrame = -1;
            }
        }

        /// <summary>
        /// [W-15] 把本机输入快照推给表现层（`SimView.LocalInput`）。
        /// 只读、单向：表现层据此做"按下立刻出声/出闪光"，**不参与任何模拟推进**。
        /// </summary>
        private void PushLocalFeedback()
        {
            var v = View;
            if (v == null) { return; }
            var f = new Simulation.SimView.LocalInputFeedback();
            f.Valid = true;
            f.AimWorldX = m_SampledAimX;
            f.AimWorldY = m_SampledAimY;
            f.FirePressCount = m_FirePressCount;
            f.FireSecondaryPressCount = m_Fire2PressCount;
            v.LocalInput = f;
        }

        /// <summary>[W-13] 清空帧队列与调度器状态（重建模拟/换局时必须调用）。</summary>
        private void ResetFrameQueue()
        {
            m_FrameQueue.Clear();
            m_FrameAccumulator = 0f;
            m_CatchingUp = false;
            m_MaxQueueDepth = 0;
            ResetDepthWindow();
            if (View != null) { View.SnapToLatest = false; }
        }

        /// <summary>处理定期状态对账：按帧号从哈希环回查对比，不等即不同步。</summary>
        private void HandleStateCheck(S2CStateCheck check)
        {
            if (check == null || Simulation == null)
            {
                return;
            }
            // Host 仅在战斗阶段下发 StateCheck；房间模拟（seed=0，玩家加入时序不同哈希天然不等）不比对。
            // ★ 但"收到 StateCheck 却仍是 seed=0 房间模拟"= 我错过了 S2CBattleStart，已与对局脱节。
            //   以前这里静默 return → 两端处于完全不同的世界却一条告警都没有（2026-09-28 实测 P0）。
            if (Simulation.Seed == 0)
            {
                if (!m_OutOfSyncWithBattle)
                {
                    m_OutOfSyncWithBattle = true;
                    string err = "[net] ⚠ 收到 StateCheck 但本地仍是房间模拟（seed=0）→ 错过了 S2CBattleStart，已与对局脱节！"
                        + " hostFrame=" + check.FrameIndex + " localFrame=" + Simulation.FrameIndex;
                    WriteProbe(err);
                    Debug.LogError("[NetClientLogic] " + err);
                }
                return;
            }
            if (check.FrameIndex <= Simulation.FrameIndex)
            {
                // 本地已推进到该帧或更远：立即从环回查
                TryVerifyFrame(check.FrameIndex, check.StateHash);
            }
            else
            {
                // 本地尚未到达该帧：缓存，待 HandleInputFrame tick 到该帧后对比
                m_CheckFrame = check.FrameIndex;
                m_CheckHash = check.StateHash;
            }
        }

        /// <summary>从帧哈希环中查找指定帧的本地哈希并对比 Host 值。</summary>
        private void TryVerifyFrame(int frameIndex, long hostHash)
        {
            for (int i = 0; i < HashRingSize; i++)
            {
                if (m_HashRingFrame[i] == frameIndex)
                {
                    long localHash = m_HashRingValue[i];
                    if (localHash != hostHash)
                    {
                        m_DesyncCount++;
                        // [W-12] 归因：如果是"输入帧缺口"导致的，必须明说 ——
                        // 否则这条日志会把人引向模拟层找 bug，而真实原因在传输层（缺口帧已无法恢复）。
                        string cause = m_InputGapEvents > 0
                            ? string.Format("　← 本次对局已发生 {0} 次输入帧缺口（累计缺 {1} 帧）：本地帧号永久落后，"
                                + "对账必然持续不一致。这是**传输层缺口**，不是模拟层 bug（决策上不做回滚，缺口无法补回）。",
                                m_InputGapEvents, m_InputGapFrames)
                            : "";
                        string msg = string.Format("[net] 不同步! frame={0} host={1} local={2} total={3}{4}",
                            frameIndex, hostHash, localHash, m_DesyncCount, cause);
                        WriteProbe(msg);
                        Debug.LogError("[NetClientLogic] " + msg);
                    }
                    else
                    {
                        WriteProbe("[net] 对账一致 frame=" + frameIndex + " hash=" + hostHash);
                    }
                    return;
                }
            }
            // 帧不在环（本地落后过远/换模拟清空）：本轮跳过，下个 StateCheck 会再对
            WriteProbe("[net] 对账跳过 frame=" + frameIndex + "（不在本地哈希环）");
        }

        /// <summary>记录一帧的状态哈希（环形缓冲；每 tick 一次，战斗阶段开销可忽略）。</summary>
        private void RecordFrameHash(int frameIndex)
        {
            m_HashRingFrame[m_HashRingHead] = frameIndex;
            m_HashRingValue[m_HashRingHead] = Simulation.ComputeStateHash();
            m_HashRingHead = (m_HashRingHead + 1) % HashRingSize;
        }

        /// <summary>清空哈希环与待校验帧（换模拟/重建时调用，防旧帧号误命中）。</summary>
        private void ResetHashRing()
        {
            for (int i = 0; i < HashRingSize; i++)
            {
                m_HashRingFrame[i] = int.MinValue;
            }
            m_HashRingHead = 0;
            m_CheckFrame = -1;
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

        /// <summary>从数据表构建确定性玩家配置（单一逻辑源：统一走 SimConfigFactory）。</summary>
        /// <summary>
        /// W-06：从 Host 广播的**装备 Id** 构建配置（不再读本机背包）。
        ///
        /// 找不到该玩家的 Id 时按"空手"处理并**大声告警**：
        /// 空手虽然不对，但它是**各端一致的兜底**；而"各端读各自的背包"必然分叉且极难排查。
        /// </summary>
        private Simulation.SimPlayerConfig BuildPlayerConfig(int entityId, int characterId)
        {
            Simulation.PlayerLoadoutIds ids;
            if (!EmojiWar.GameMain.Simulation.LoadoutWire.TryFind(m_Loadouts, entityId, out ids))
            {
                // 房间阶段（seed=0）缺 Id 是正常时序（装备广播还没到）→ 静默；
                // 战斗阶段缺 Id 才是真问题（该玩家会按空手参战）→ 大声告警。
                string warn = "[net] ⚠ 没有 entity " + entityId + " 的装备 Id（装备广播可能未到）→ 该玩家按空手参战";
                if (m_BuildingBattleSim)
                {
                    WriteProbe(warn);
                    Debug.LogWarning("[NetClientLogic] " + warn);
                }
                ids = default(Simulation.PlayerLoadoutIds);
            }
            return EmojiWar.GameMain.Simulation.SimConfigFactory.BuildFromIds(-1, entityId, characterId, ids);
        }

        /// <summary>
        /// 战斗开始：用广播种子重建本地确定性模拟（与 Host 一致）并开启波次。
        /// </summary>
        private void InitializeBattleSimulation(int seed)
        {
            Simulation = new Simulation.LockstepSimulation();
            ResetHashRing();   // 换模拟：清空哈希环，防旧帧号误命中
            ResetFrameQueue(); // [W-13] 换模拟：帧号从 0 重来，队列里的旧帧号会让新帧全被判成"过期"
            // 注入波次平衡参数（与 Host 同资产同值，确定性一致）
            Simulation.ApplyBattleConfig(GameEntry.Data != null ? GameEntry.Data.Battle : null);

            // W-06：接下来构建的是**战斗**配置 —— 此阶段缺装备 Id 必须大声告警（见 BuildPlayerConfig）
            m_BuildingBattleSim = true;
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
            m_BuildingBattleSim = false;

            Simulation.Initialize(seed, configs);
            // 开局先进**准备阶段商店**（WaveIndex=0，不出敌人），玩家点"继续"才 StartWave(1)
            Simulation.PrepareFirstWave();
            WriteProbe("[net] 战斗模拟初始化 seed=" + seed + " players=" + configs.Count);
            Debug.Log("[NetClientLogic] 战斗确定性模拟已初始化，玩家数 " + configs.Count);

            // 确定性打点开始（文档 §4：双端各写一份 trace，diff 定位分歧）
            EmojiWar.GameMain.Simulation.DeterminismTracer.Begin(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/traces"));

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
            ResetHashRing();   // 换模拟：清空哈希环
            ResetFrameQueue(); // [W-13]
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
            EmojiWar.GameMain.Simulation.DeterminismTracer.End();
            ClearSimulation();
            ResetFrameQueue();   // [W-13] 回房间：帧号重来，队列必须清
            // [W-09] 一局结束 → 清空本机物品系统（背包跨局残留会让第二局的 loadout
            //   仍然基于上一局的背包）。必须**和房主对称**做，否则第二局两端 loadout 又不同。
            //   下一局进入战斗时 ProcedureBattle 会重新 GrantStartingLoadout。
            ItemSystem.Reset();
            EnsureRoomSimulation();
            WriteProbe("[net] RunRestart -> 重建房间模拟（已清物品系统）");
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

        // ==================== W-07：加入时的配置/代码指纹 ====================

        // 开发期测试钩子（`-fakeconfighash <hex>` / `-fakecodehash <hex>`）：
        // 没有它就只能靠"真的把一端资产改坏"来验证拒绝路径，而资产一改就会污染工作区。
        private static bool s_WireParsed = false;
        private static ulong s_FakeConfigHash = 0UL;
        private static ulong s_FakeCodeHash = 0UL;

        private static void EnsureWireParsed()
        {
            if (s_WireParsed) { return; }
            s_WireParsed = true;
            try
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-fakeconfighash" && i + 1 < args.Length)
                    {
                        ulong v;
                        if (ulong.TryParse(args[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) { s_FakeConfigHash = v; }
                    }
                    else if (args[i] == "-fakecodehash" && i + 1 < args.Length)
                    {
                        ulong v;
                        if (ulong.TryParse(args[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) { s_FakeCodeHash = v; }
                    }
                }
            }
            catch (System.Exception) { /* 参数解析失败不影响正常流程 */ }
        }

        private static ulong GetLocalConfigHash()
        {
            EnsureWireParsed();
            if (s_FakeConfigHash != 0UL) { return s_FakeConfigHash; }
            return EmojiWar.GameMain.Data.ConfigService.VersionHash;
        }

        private static ulong GetLocalCodeHash()
        {
            EnsureWireParsed();
            if (s_FakeCodeHash != 0UL) { return s_FakeCodeHash; }
            return EmojiWar.GameMain.Simulation.SimBuildInfo.CodeHash;
        }

        private C2SJoinRoom MakeJoinRoom()
        {
            return new C2SJoinRoom
            {
                PlayerName = m_PlayerName,
                CharacterId = GetLocalCharacterId(),
                ConfigHash = GetLocalConfigHash(),   // W-07
                CodeHash = GetLocalCodeHash(),       // W-07
            };
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

        /// <summary>是否已确认与对局脱节（停在房间模拟却收到了战斗对账/帧号倒退）。供 HUD 告警用。</summary>
        public bool IsOutOfSyncWithBattle
        {
            get { return m_OutOfSyncWithBattle; }
        }
    }
}
