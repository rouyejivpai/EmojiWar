//------------------------------------------------------------
// EmojiWar GameMain - 确定性帧同步模拟核心（Lockstep）
// 所有端（Host + Clienti）以相同输入序列 + 固定 tick 推进同一份模拟：
//   - 固定 tick 20Hz（0.05i），不使用 Time.deltaTime / UnityEngine.Random
//   - 输入只含"意图"（方向/瞄准/射击/装弹），不含位置结果
//   - 玩家移动、子弹、敌人 AI、波次、商店全部确定性计算
// 结果天然一致，网络只负责收集输入并广播输入帧。
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>玩家输入意图（纯意图，不含位置）。</summary>
    public struct PlayerIntent
    {
        public float MoveX;
        public float MoveY;
        public float AimX;
        public float AimY;
        public bool FirePrimary;
        public bool FireSecondary;
        public bool Reload;

        /// <summary>
        /// [W-12] 该玩家本帧是否处于**托管**（房主判定其输入源已断，用空输入代打）。
        /// 由房主写入广播帧、客户端照收 → **两端同值**，因此可以安全进状态哈希。
        /// </summary>
        public bool Managed;

        public static PlayerIntent Empty { get { return new PlayerIntent { AimX = 1f }; } }
    }

    /// <summary>玩家模拟配置（初始化时从角色/武器数据表注入）。</summary>
    public sealed class SimPlayerConfig
    {
        public int SessionId;
        public int EntityId;
        public int CharacterId;
        public SimVec2 StartPosition = SimVec2.zero;
        public float MoveSpeed = 5f;

        // 主武器参数（来自 WeaponSO，确定性）
        public int WeaponId = 1;                 // 武器数据表 ID（HUD/图标用）
        public string WeaponName = "水滴枪";      // 武器名（HUD 用，避免查表）
        public float WeaponDamage = 10f;
        public float FireRate = 3f;          // 每秒射击次数
        public int MaxAmmo = 30;
        public float ReloadTime = 2f;
        public float BulletSpeed = 15f;

        // 两只手的施法程序（决策：法术槽属于"手"，两手各自一套；右手无效=没有副武器）
        // 由 LoadoutCompiler 从"手部法杖 + 该手法术槽"编译（纯值类型，可跨端复现）。
        public EmojiWar.GameMain.Items.CastProgram PrimaryProgram = EmojiWar.GameMain.Items.CastProgram.Empty;
        public EmojiWar.GameMain.Items.CastProgram SecondaryProgram = EmojiWar.GameMain.Items.CastProgram.Empty;
    }

    /// <summary>模拟玩家运行时状态。</summary>
    public sealed class SimPlayer
    {
        public int SessionId;
        public int EntityId;
        public int CharacterId;
        public SimVec2 Position;
        public SimVec2 PrevPosition;   // 上一逻辑帧位置（渲染插值用）
        public float MoveSpeed;
        public float Hp = 100f;
        public bool Alive = true;

        /// <summary>[W-12] 托管中（输入源已断，由房主用空输入代打）。进状态哈希（W-08 策略表已登记）。</summary>
        public bool Managed;

        // 武器运行时状态（确定性）
        public int WeaponId = 1;
        public string WeaponName = "水滴枪";
        public float WeaponDamage;
        public float FireRate;
        public int MaxAmmo;
        public int Ammo;
        public float ReloadTime;
        public float BulletSpeed;
        public float FireCooldown;
        public float ReloadTimer;
        public bool IsReloading;

        // 两只手的施法状态（魔力/节奏/游标；各自独立）
        public EmojiWar.GameMain.Items.CastProgram PrimaryProgram = EmojiWar.GameMain.Items.CastProgram.Empty;
        public EmojiWar.GameMain.Items.CastProgram SecondaryProgram = EmojiWar.GameMain.Items.CastProgram.Empty;
        public EmojiWar.GameMain.Items.CastRuntimeState PrimaryCast;
        public EmojiWar.GameMain.Items.CastRuntimeState SecondaryCast;
        /// <summary>是否有副武器（右手装了法杖；空手 → 右键不开火）。</summary>
        public bool HaiSecondary { get { return SecondaryProgram.IsValid; } }
    }

    /// <summary>模拟敌人。</summary>
    public sealed class SimEnemy
    {
        public int EntityId;
        public SimVec2 Position;
        public SimVec2 PrevPosition;   // 上一逻辑帧位置（渲染插值用）
        public float Hp;
        public float Speed;
        public bool Alive = true;
        public float ContactCooldown;   // 接触攻击冷却
    }

    /// <summary>模拟子弹。</summary>
    public sealed class SimBullet
    {
        public int EntityId;
        public SimVec2 Position;
        public SimVec2 PrevPosition;   // 上一逻辑帧位置（渲染插值用）
        public SimVec2 Direction;
        public float Speed;
        public float Damage;
        public int OwnerSession;
        public float Lifetime;
        public float Radius;           // D33：由弹道剖面决定；0 = 用 SpellSystemConfig 兜底（不再写死）
        /// <summary>S4：子弹标签（从 `CastShot.Tags` 带下来），供标签过滤型被动使用。</summary>
        public Data.SpellTag Tags;
        public bool Alive = true;
    }

    /// <summary>
    /// 确定性模拟世界：Tick 推进。
    /// </summary>
    public sealed class LockstepSimulation
    {
        /// <summary>
        /// 固定逻辑 tick 间隔。**[W-10a] 已从 20Hz（0.05i）切到 30Hz（1/30 i）**。
        /// 这是全工程唯一的帧率定义：`CastResolver.TickSeconds` 直接引用它，
        /// 而所有"按帧"配置在 W-10a 里已改成**毫秒**并在加载期量化 —— 所以改这一行
        /// **不会**再静默改变任何配置的真实时长（那正是切帧率最容易踩的坑）。
        /// 代码指纹（`SimBuildInfo`）把它算进去了：帧率不同的两端会在握手阶段被明确拒绝。
        /// </summary>
        public const float TickInterval = 1f / 30f;

        public const int MaxPlayers = 4;
        private const float BulletHitRadius = 0.5f;
        private const float EnemyContactRadius = 0.6f;
        private const float EnemyContactDamage = 10f;
        private const float EnemyContactInterval = 1.0f;

        // ---- 波次平衡参数（默认值=历史硬编码；战斗开始前由网络层从 BattleConfigSO 注入，
        //      双端同资产同值 → 确定性保持）----
        public int EnemiesPerWaveBase = 3;          // 第 1 波敌人数量
        public int EnemiesPerWaveGrowth = 2;        // 每波敌人数量递增
        public float SpawnRadius = 8f;              // 敌人生成半径（环绕玩家）
        public float EnemySpawnInterval = 0.5f;     // 敌人分批出生间隔
        public float EnemyBaseHp = 30f;             // 第 1 波敌人 HP
        public float EnemyHpPerWave = 5f;           // 每波 HP 递增
        public float EnemyBaseSpeed = 2.5f;         // 第 1 波敌人移速
        public float EnemySpeedPerWave = 0.3f;      // 每波移速递增
        public float ShopDuration = 8f;             // 波间商店时长
        public int ShopItemCount = 3;               // 商店商品数
        public int WeaponPrice = 80;                // 武器价格
        public int ModPrice = 60;                   // Mod 价格

        // 与 BattleConfigSO 同款做法：战斗开始前由网络层/工厂注入；双端同资产同值 → 确定性保持。
        /// <summary>法术系统总配置（D31）：上限与弹道兜底一律从配置读（P11 配置驱动）</summary>
        public ISimSpellConfig spellConfig;

        /// <summary>从 ISimSpellConfig 注入法术系统上限/兜底（无 SO 时用结构兜底）。</summary>
        public void ApplySpellConfig(ISimSpellConfig cfg)
        {
            spellConfig = cfg;
        }

        /// <summary>从 BattleConfigSO 注入波次参数（无 SO 时保持默认值）。</summary>
        public void ApplyBattleConfig(ISimBattleConfig cfg)
        {
            if (cfg == null)
            {
                return;
            }
            EnemiesPerWaveBase = cfg.EnemiesPerWaveBase;
            EnemiesPerWaveGrowth = cfg.EnemiesPerWaveGrowth;
            SpawnRadius = cfg.SpawnRadius;
            EnemySpawnInterval = cfg.EnemySpawnInterval;
            EnemyBaseHp = cfg.EnemyBaseHp;
            EnemyHpPerWave = cfg.EnemyHpPerWave;
            EnemyBaseSpeed = cfg.EnemyBaseSpeed;
            EnemySpeedPerWave = cfg.EnemySpeedPerWave;
            ShopDuration = cfg.ShopDuration;
            ShopItemCount = cfg.ShopItemCount;
            WeaponPrice = cfg.WeaponPrice;
            ModPrice = cfg.ModPrice;
        }

        public int FrameIndex { get; private set; }
        public int Seed { get; private set; }
        public int WaveIndex { get; private set; }
        public bool ShopOpen { get; private set; }
        public bool BattleOver { get; private set; }

        /// <summary>
        /// 波次是否已启动（仅战斗开始后为 true）。
        /// 房间阶段（seed=0 房间模拟）未调用 StartWave，此标志为 false：
        /// UpdateWave 不会自动开波/生成敌人/开商店，模拟仅驱动玩家移动。
        /// 修复：此前房间模拟会在无敌人时自动 ShopOpen→StartWave(1)，导致"停在房间却自动开战"。
        /// </summary>
        public bool WaveStarted { get; private set; }

        private SimRandom m_Rng;
        private readonly List<SimPlayer> m_Players = new List<SimPlayer>();
        private readonly List<SimEnemy> m_Enemies = new List<SimEnemy>();
        private readonly List<SimBullet> m_Bullets = new List<SimBullet>();

        /// <summary>
        /// S4：施法外部事件总线（命中/击杀 → 被动触发）。
        /// **必须是实例字段**（不能做成静态）：回环/联机对拍会在同一进程里跑两个模拟，
        /// 静态队列会让两个模拟互相偷事件 → 非确定性。
        /// </summary>
        private readonly CastEventBus m_CastEvents = new CastEventBus();

        /// <summary>S4：外部事件总线（诊断/探针用）。</summary>
        public CastEventBus CastEvents { get { return m_CastEvents; } }

        /// <summary>按 SessionId 找玩家实体 Id（S4 击杀事件携带"击杀者"用；找不到返回 0）。</summary>
        private int PlayerEntityBySession(int SessionId)
        {
            for (int i = 0; i < m_Players.Count; i++)
            {
                if (m_Players[i].SessionId == SessionId) { return m_Players[i].EntityId; }
            }
            return 0;
        }
        private int m_NextEntityId = 1000;

        // 波次状态（确定性）
        private int m_EnemiesToSpawn;
        private float m_SpawnTimer;
        private float m_ShopTimer;
        private bool m_ShopOfferReady;

        // 商店商品（确定性生成，各端一致）
        private readonly List<string> m_ShopItems = new List<string>();

        /// <summary>当前波次敌人数量（测试/诊断）。</summary>
        public int EnemiesPerWaveCount { get; private set; }

        /// <summary>当前商店商品（"type:id:price;..."；空表示商店未开）。</summary>
        public string ShopItems
        {
            get
            {
                if (m_ShopItems.Count == 0)
                {
                    return string.Empty;
                }
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < m_ShopItems.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(';');
                    }
                    sb.Append(m_ShopItems[i]);
                }
                return sb.ToString();
            }
        }

        // ---- 事件（供表现层/流程订阅）----
        /// <summary>波次开始（参数：波次号）。</summary>
        public event Action<int> OnWaveChanged;
        /// <summary>商店开放（参数：波次号）。</summary>
        public event Action<int> OnShopOpened;
        /// <summary>战斗结束（全部玩家死亡）。</summary>
        public event Action OnBattleEnded;
        /// <summary>玩家 HP 变化（参数：SessionId, hp）。</summary>
        public event Action<int, float> OnPlayerHpChanged;
        /// <summary>敌人被击杀（参数：entityId）。</summary>
        public event Action<int> OnEnemyKilled;

        // ==================== 确定性指令队列（W-03 / 报告 A9）====================
        //
        // 为什么需要它（2026-09-28 由 W-03 回放实证）：
        //   原先"商店继续"由 Host 在**消息处理里直接调** `RequestNextWave()`，
        //   即绕过帧管线改模拟。后果有两个：
        //     ① 录像只记输入流 → 回放无法复现（实测：分歧恰好在第 501 帧 = 录像里第一个
        //        敌人生成的帧，因为回放一直停在商店里不生成敌人）；
        //     ② 指令生效的时刻取决于"消息到达/UI 点击的渲染帧"，而不取决于逻辑帧号。
        //   现在统一为：**指令入队 → 在帧首（FrameIndex++ 之后、玩家逻辑之前）按入队顺序消费**。
        //   于是"入队顺序 + 帧号"成为确定性的一部分，录像把它一起记下来即可完整复现。
        //
        // 仍未走帧管线的变更（W-16 待办，已知限制）：
        //   `ApplyWeapon`（商店买武器）/ `ApplyCharacter`（房间换角色）/ `DebugKillAllEnemies`。
        //   它们目前不影响本项目的回放测试（测试里不发生购买），但会让"买过武器的对局"回放分叉。

        /// <summary>确定性指令种类。</summary>
        public enum SimCommandKind
        {
            None = 0,
            NextWave = 1,        // 商店"继续"→ 开下一波
        }

        /// <summary>一条确定性指令（纯值，可进录像）。</summary>
        public struct SimCommand
        {
            public SimCommandKind Kind;
            public int Arg0;
            public int Arg1;
        }

        private readonly List<SimCommand> m_PendingCommands = new List<SimCommand>();

        /// <summary>
        /// 入队一条确定性指令（**唯一**允许的"从外部改模拟"的入口）。
        /// 不要直接调 `RequestNextWave` 等会改变模拟状态的公开方法 —— 那样录像记不到、回放必分叉。
        /// </summary>
        public void EnqueueCommand(SimCommandKind kind, int arg0 = 0, int arg1 = 0)
        {
            m_PendingCommands.Add(new SimCommand { Kind = kind, Arg0 = arg0, Arg1 = arg1 });
        }

        /// <summary>当前待消费指令（录像在 Tick **之前**读取，用于写入该帧记录）。</summary>
        public IReadOnlyList<SimCommand> PendingCommands { get { return m_PendingCommands; } }

        /// <summary>帧首消费指令：按入队顺序（顺序本身就是确定性的一部分）。</summary>
        private void ApplyPendingCommands()
        {
            if (m_PendingCommands.Count == 0) { return; }

            for (int i = 0; i < m_PendingCommands.Count; i++)
            {
                SimCommand c = m_PendingCommands[i];
                switch (c.Kind)
                {
                    case SimCommandKind.NextWave:
                        RequestNextWave();
                        break;
                    default:
                        break;
                }
            }
            m_PendingCommands.Clear();
        }

        public IReadOnlyList<SimPlayer> Players { get { return m_Players; } }

        /// <summary>
        /// 探针用：描述各玩家的主/副武器配置与冷却（验证"副武器在逻辑上存在"）。
        /// 纯读，不参与模拟。
        /// </summary>
        public string DescribeWeapons()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < m_Players.Count; i++)
            {
                var p = m_Players[i];
                if (sb.Length > 0) { sb.Append(" | "); }
                sb.Append("E").Append(p.EntityId)
                  .Append(" primary#").Append(p.WeaponId);
                if (p.PrimaryProgram.IsValid)
                {
                    sb.Append(" prog=").Append(p.PrimaryProgram.Dump())
                      .Append(" mana=").Append(p.PrimaryCast.Mana.ToString("F1"))
                      .Append(" cursor=").Append(p.PrimaryCast.Cursor);
                }
                if (p.SecondaryProgram.IsValid)
                {
                    sb.Append(" Secondary=").Append(p.SecondaryProgram.Dump())
                      .Append(" mana=").Append(p.SecondaryCast.Mana.ToString("F1"))
                      .Append(" cursor=").Append(p.SecondaryCast.Cursor);
                }
                else
                {
                    sb.Append(" Secondary=none(空手)");
                }
            }
            return sb.ToString();
        }
        public IReadOnlyList<SimEnemy> Enemies { get { return m_Enemies; } }
        public IReadOnlyList<SimBullet> Bullets { get { return m_Bullets; } }

        /// <summary>
        /// 初始化模拟（战斗开始/重开时调用）。
        /// </summary>
        public void Initialize(int seed, IList<SimPlayerConfig> playerConfigs)
        {
            Seed = seed;
            m_Rng = new SimRandom((uint)seed);
            FrameIndex = 0;
            WaveIndex = 0;
            ShopOpen = false;
            BattleOver = false;
            WaveStarted = false;   // 房间阶段不自动开波（战斗开始经 StartWave 置位）
            m_EnemiesToSpawn = 0;
            m_SpawnTimer = 0f;
            m_ShopTimer = 0f;
            m_ShopOfferReady = false;

            m_Players.Clear();
            m_Enemies.Clear();
            m_Bullets.Clear();
            m_ShopItems.Clear();

            if (playerConfigs != null)
            {
                foreach (var cfg in playerConfigs)
                {
                    var player = new SimPlayer
                    {
                        SessionId = cfg.SessionId,
                        EntityId = cfg.EntityId,
                        CharacterId = cfg.CharacterId,
                        Position = cfg.StartPosition,
                        PrevPosition = cfg.StartPosition,
                        MoveSpeed = cfg.MoveSpeed,
                        WeaponId = cfg.WeaponId,
                        WeaponName = cfg.WeaponName,
                        WeaponDamage = cfg.WeaponDamage,
                        FireRate = cfg.FireRate,
                        MaxAmmo = cfg.MaxAmmo,
                        Ammo = cfg.MaxAmmo,
                        ReloadTime = cfg.ReloadTime,
                        BulletSpeed = cfg.BulletSpeed,
                        FireCooldown = 0f,
                        PrimaryProgram = cfg.PrimaryProgram,
                        SecondaryProgram = cfg.SecondaryProgram,
                        PrimaryCast = EmojiWar.GameMain.Items.CastRuntimeState.For(cfg.PrimaryProgram),
                        SecondaryCast = EmojiWar.GameMain.Items.CastRuntimeState.For(cfg.SecondaryProgram),
                    };
                    m_Players.Add(player);
                }
                // 按 EntityId 升序排序，保证多端顺序一致（文档 §9 有序容器）
                m_Players.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
            }
        }

        /// <summary>
        /// 增量加入玩家（房间阶段玩家加入时调用；与 Initialize 共用配置构建）。
        /// 按 EntityId 升序插入，保证多端 m_Players 顺序一致（文档 §9：有序容器，杜绝遍历顺序分歧）。
        /// </summary>
        public void AddPlayer(SimPlayerConfig cfg)
        {
            if (cfg == null)
            {
                return;
            }
            foreach (var p in m_Players)
            {
                if (p.EntityId == cfg.EntityId)
                {
                    return;    // 已存在（幂等）
                }
            }
            var player = new SimPlayer
            {
                SessionId = cfg.SessionId,
                EntityId = cfg.EntityId,
                CharacterId = cfg.CharacterId,
                Position = cfg.StartPosition,
                PrevPosition = cfg.StartPosition,
                MoveSpeed = cfg.MoveSpeed,
                WeaponId = cfg.WeaponId,
                WeaponName = cfg.WeaponName,
                WeaponDamage = cfg.WeaponDamage,
                FireRate = cfg.FireRate,
                MaxAmmo = cfg.MaxAmmo,
                Ammo = cfg.MaxAmmo,
                ReloadTime = cfg.ReloadTime,
                BulletSpeed = cfg.BulletSpeed,
                FireCooldown = 0f,
                PrimaryProgram = cfg.PrimaryProgram,
                SecondaryProgram = cfg.SecondaryProgram,
                PrimaryCast = EmojiWar.GameMain.Items.CastRuntimeState.For(cfg.PrimaryProgram),
                SecondaryCast = EmojiWar.GameMain.Items.CastRuntimeState.For(cfg.SecondaryProgram),
            };
            // 按 EntityId 升序插入（有序容器，跨端遍历顺序一致）
            int insertIndex = 0;
            while (insertIndex < m_Players.Count && m_Players[insertIndex].EntityId < player.EntityId)
            {
                insertIndex++;
            }
            m_Players.Insert(insertIndex, player);
        }

        /// <summary>移除玩家（玩家离开房间/战斗时调用）。</summary>
        public void RemovePlayer(int entityId)
        {
            for (int i = m_Players.Count - 1; i >= 0; i--)
            {
                if (m_Players[i].EntityId == entityId)
                {
                    m_Players.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 推进一个逻辑 tick（20Hz）。
        /// </summary>
        /// <param name="inputs">entityId → 输入意图；缺省玩家视为空输入（掉线托管）。
        /// 注意：以实体 ID 为 key（客户端只知道 EntityId，不知道 SessionId）。</param>
        public void Tick(Dictionary<int, PlayerIntent> inputs)
        {
            if (BattleOver)
            {
                return;
            }

            // W-01：逻辑帧耗时统计（Profiler 标记 + 环形样本）
            SimPerf.BeginTick();

            FrameIndex++;

            // 确定性打点：帧开始（文档 §4）
            DeterminismTracer.RecordInt(DeterminismTracer.Check.FrameStart, FrameIndex, FrameIndex);

            // W-03：帧首消费确定性指令（商店继续等）。必须在玩家逻辑之前、且顺序 = 入队顺序。
            ApplyPendingCommands();

            // 0. 记录上一帧位置（渲染插值基准；确定性：所有端同帧同值）
            for (int i = 0; i < m_Players.Count; i++)
            {
                m_Players[i].PrevPosition = m_Players[i].Position;
            }
            for (int i = 0; i < m_Enemies.Count; i++)
            {
                m_Enemies[i].PrevPosition = m_Enemies[i].Position;
            }
            for (int i = 0; i < m_Bullets.Count; i++)
            {
                m_Bullets[i].PrevPosition = m_Bullets[i].Position;
            }

            // 1. 玩家：应用输入 → 移动/瞄准/射击
            // 注：已取消弹药设定（2026-09-02 需求）：武器无限释放，无需装弹，
            //     射击只受射速冷却（FireCooldown）限制，Reload 输入被忽略（字段保留协议兼容）。
            foreach (var player in m_Players)
            {
                if (!player.Alive)
                {
                    continue;
                }

                PlayerIntent intent = PlayerIntent.Empty;
                if (inputs != null && inputs.TryGetValue(player.EntityId, out var i))
                {
                    intent = i;
                }

                // [W-12] 托管标志是模拟状态的一部分（两端同值，进哈希）
                player.Managed = intent.Managed;

                // 移动（固定 dt）
                SimVec2 moveDir = new SimVec2(intent.MoveX, intent.MoveY);
                // [W-11] 距离/归一化一律走 SimMath：Unity 的 `sqrMagnitude`/`normalized`
                // 内部是 `x*x+y*y`，会被收缩成 FMA（实测就是它让 `bullets` 段跨后端不同）
                if (SimMath.SqrMagnitude(moveDir) > 1f)
                {
                    moveDir = SimMath.Normalized(moveDir);
                }
                // [W-11] 位置积分走 SimMath：`v += d * i * k` 直接写会被编译器收缩成 FMA，
                // 与 Mono 差 1 ulp，且**跨帧累积** → 实测同类写法已在 DelayCarry 上造成跨后端分叉。
                player.Position = SimMath.AddScaled2(player.Position, moveDir, player.MoveSpeed, TickInterval);

                // 射击冷却（主/副武器各自独立；有施法程序时冷却由 Caitstate 管理）
                player.FireCooldown -= TickInterval;

                // 瞄准语义：AimX/AimY 是"鼠标世界坐标目标点"（绝对位置），
                // 发射方向必须 = 目标点 − 玩家位置（相对方向）再归一化。
                SimVec2 aimTarget = new SimVec2(intent.AimX, intent.AimY);
                SimVec2 aim = aimTarget - player.Position;
                if (SimMath.SqrMagnitude(aim) < 0.001f)
                {
                    aim = SimVec2.right;
                }
                // [W-11] ★ 实测的最后一个跨后端分叉点：`aim.Normalize()` 内部的 `x*x+y*y`
                //   被 IL2CPP 收缩成 FMA → 子弹方向差 1 ulp → `bullets` 段从第 53 帧起不同。
                aim = SimMath.Normalized(aim);

                if (player.PrimaryProgram.IsValid || player.SecondaryProgram.IsValid)
                {
                    // 法杖编程式施法：一次发射跑完整条序列（Q1）+ 跨帧延迟队列（Q3）+ 三修正 + Q2 中止口径
                    CastResolver.Tick(player.PrimaryProgram, ref player.PrimaryCast, TickInterval, intent.FirePrimary,
                        m_CastPlanPrimary, spellConfig, m_CastEvents, player.SessionId);
                    SpawnCastPlan(player, aim, m_CastPlanPrimary);

                    if (player.SecondaryProgram.IsValid)
                    {
                        CastResolver.Tick(player.SecondaryProgram, ref player.SecondaryCast, TickInterval, intent.FireSecondary,
                            m_CastPlanSecondary, spellConfig, m_CastEvents, player.SessionId);
                        SpawnCastPlan(player, aim, m_CastPlanSecondary);
                    }
                }
                else if (intent.FirePrimary && player.FireCooldown <= 0f)
                {
                    // 兼容路径：没有法杖程序时按武器标量发射（旧数据/离线兜底）
                    SpawnPlayerBullet(player, aim, player.BulletSpeed, player.WeaponDamage);
                    player.FireCooldown = 1f / SimMath.Max(0.01f, player.FireRate);
                }
            }

            // 2. 子弹：飞行 + 命中
            // [W-20] 命中检测前先建**敌人宽相位网格**：现状是"每颗子弹线性扫全部敌人"，
            //   200 子弹 × 200 敌人 ≈ 4 万次距离测试 —— 实测占整个 tick 的 ~95%
            //   （200 敌人无子弹 avg=0.053ms；200 敌人 + 200 子弹 avg=1.05ms）。
            //   ★ 语义必须完全等价：见 BuildEnemyGrid / TryHitEnemy 的说明。
            BuildEnemyGrid();

            foreach (var bullet in m_Bullets)
            {
                if (!bullet.Alive)
                {
                    continue;
                }

                // [W-11] 同位置积分：弹道推进也必须防 FMA 收缩（Direction/speed 都进状态哈希）
                bullet.Position = SimMath.AddScaled2(bullet.Position, bullet.Direction, bullet.Speed, TickInterval);
                bullet.Lifetime -= TickInterval;
                if (bullet.Lifetime <= 0f)
                {
                    bullet.Alive = false;
                    continue;
                }

                float hitRadius = bullet.Radius > 0f ? bullet.Radius : BulletHitRadius;
                TryHitEnemy(bullet, hitRadius);
            }


        // 3. 敌人 AI：朝最近玩家移动 + 接触伤害
            foreach (var enemy in m_Enemies)
            {
                if (!enemy.Alive)
                {
                    continue;
                }

                enemy.ContactCooldown -= TickInterval;

                SimPlayer target = GetNearestPlayer(enemy.Position);
                if (target != null)
                {
                    SimVec2 toTarget = target.Position - enemy.Position;
                    // [W-11] 敌人追击/接触判定：距离与归一化都走 SimMath（阈值判定是"离散决策"，
                    // 1 ulp 之差可能让"是否接触"翻转）
                    if (SimMath.SqrMagnitude(toTarget) > 0.01f)
                    {
                        enemy.Position = SimMath.AddScaled2(enemy.Position, SimMath.Normalized(toTarget), enemy.Speed, TickInterval);
                    }

                    if (SimMath.Magnitude(toTarget) <= EnemyContactRadius && enemy.ContactCooldown <= 0f)
                    {
                        target.Hp = SimMath.Max(0f, target.Hp - EnemyContactDamage);
                        OnPlayerHpChanged?.Invoke(target.SessionId, target.Hp);
                        // W-10（报告 B2）：玩家 HP 变化以前**没有打点** —— 而"伤害/命中判定不一致"
                        // 是最常见的分叉，恰好没有可 diff 的检查点（只能等敌人死亡或帧末哈希）。
                        // ⚠ 必须记 **EntityId** 而不是 SessionId：SessionId 是纯元数据
                        //   （Host 传真实 ieiiion、Client 一律 -1 —— 见 ComputeStateHash 的既有约定），
                        //   记进 trace 会让两端**恒误报分歧**。实测踩过：PlayerHp frame=524
                        //   HP 位模式完全相同，只有第一个 int 是 1 vs -1。
                        DeterminismTracer.RecordInt2(DeterminismTracer.Check.PlayerHp,
                            target.EntityId, BitConverter.SingleToInt32Bits(target.Hp), FrameIndex);
                        enemy.ContactCooldown = EnemyContactInterval;
                    }
                }
            }

            // 4. 波次推进（确定性）
            UpdateWave();

            // 5. 清理死亡实体（反向遍历手动移除，避免 RemoveAll lambda 每 tick 闭包分配）
            for (int i = m_Bullets.Count - 1; i >= 0; i--)
            {
                if (!m_Bullets[i].Alive)
                {
                    m_Bullets.RemoveAt(i);
                }
            }
            for (int i = m_Enemies.Count - 1; i >= 0; i--)
            {
                if (!m_Enemies[i].Alive)
                {
                    m_Enemies.RemoveAt(i);
                }
            }

            // 6. 战斗结束检测
            if (AllPlayersDead())
            {
                BattleOver = true;
                // W-10：BattleEnd 检查点（此前定义了却从未写入）
                DeterminismTracer.RecordInt(DeterminismTracer.Check.BattleEnd, FrameIndex, FrameIndex);
                OnBattleEnded?.Invoke();
            }

            // W-01：逻辑帧耗时统计结束
            SimPerf.EndTick();
        }

        /// <summary>
        /// 生成一发玩家子弹（主/副武器共用；纯确定性，无随机）。
        /// PrevPosition = Position：新实体不插值，避免从原点拖出重影。
        /// </summary>
        // 施法计划复用缓冲（避免每帧 GC）。
        // [W-09] 改为**实例字段**：原来是 `static readonly`，但**内容每 tick 被改写** —— 两个模拟
        //   实例（房间模拟 + 战斗模拟，或同进程两局）会共用同一组缓冲，是"共享可变状态"这一类
        //   非确定性来源；`CastPlan` 本身已是复用容器，改成实例**不增加每帧分配**。
        private readonly CastPlan m_CastPlanPrimary = new CastPlan();
        private readonly CastPlan m_CastPlanSecondary = new CastPlan();

        /// <summary>把一次施法计划落成子弹（同帧多发按扇形展开；确定性，无随机）。</summary>
        private void SpawnCastPlan(SimPlayer player, SimVec2 aim, CastPlan plan)
        {
            if (plan == null || plan.Shots.Count == 0) { return; }
            for (int i = 0; i < plan.Shots.Count; i++)
            {
                var shot = plan.Shots[i];
                SimVec2 dir = aim;
                if (shot.GroupCount > 1 && shot.SpreadDeg > 0.01f)
                {
                    float t = shot.GroupCount > 1 ? (shot.GroupIndex / (float)(shot.GroupCount - 1)) : 0.5f;
                    float angle = SimMath.Lerp(-shot.SpreadDeg * 0.5f, shot.SpreadDeg * 0.5f, t) * SimMath.Deg2Rad;
                    float cs = SimMath.Cos(angle);
                    float sn = SimMath.Sin(angle);
                    // [W-11] 旋转是 `x*cs - y*sn` / `x*sn + y*cs`：两个乘减/乘加都会被收缩成 FMA
                    // （实测同形状的 `delay - frames*TickSeconds` 已经造成过跨后端分叉）
                    dir = SimMath.Rotate(aim.x, aim.y, cs, sn);
                }
                SpawnPlayerBullet(player, dir, shot.speed, shot.Damage, shot.Lifetime, shot.Radius, shot.Tags);
            }
        }

        /// <summary>生成一发玩家子弹（主/副武器共用；纯确定性，无随机）。</summary>
        private void SpawnPlayerBullet(SimPlayer player, SimVec2 aim, float speed, float damage,
            float lifetime = 0f, float radius = 0f, Data.SpellTag tags = Data.SpellTag.None)
        {
            float life = lifetime > 0f
                ? lifetime
                : (spellConfig != null ? spellConfig.DefaultBulletLifetime : 3f);
            float rad = radius > 0f
                ? radius
                : (spellConfig != null ? spellConfig.DefaultBulletRadius : 0.2f);

            m_Bullets.Add(new SimBullet
            {
                EntityId = m_NextEntityId++,
                Position = player.Position,
                PrevPosition = player.Position,
                Direction = aim,
                Speed = speed,
                Damage = damage,
                OwnerSession = player.SessionId,
                Lifetime = life,
                Radius = rad,
                Tags = tags,
            });

            // 确定性打点：子弹生成（弹道方向位模式，跨端可比）
            // W-10/H2：改用三参重载 —— 原来 `new[]{...}` 数组字面量**在调用前就分配了**，
            // 即使打点关闭也在分配（报告 H2 第 2 条：每次生成子弹一个 int[3]）。
            DeterminismTracer.RecordInt3(DeterminismTracer.Check.BulletSpawn,
                m_NextEntityId - 1,
                BitConverter.SingleToInt32Bits(aim.x),
                BitConverter.SingleToInt32Bits(aim.y),
                FrameIndex);
        }

        /// <summary>
        /// 稳定字符串哈希（FNV-1a 32bit；与 `Items.CastBuffDef.HashKey` 同款）。
        /// 替代 `string.GetHashCode()` —— 后者跨进程/跨运行时不保证一致，会让 trace 出现假分歧。
        /// </summary>
        private static int StableStringHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (!string.IsNullOrEmpty(s))
                {
                    for (int i = 0; i < s.Length; i++)
                    {
                        h ^= s[i];
                        h *= 16777619u;
                    }
                }
                return (int)h;
            }
        }

        /// <summary>波次状态机（确定性：按 tick 计数，不用协程）。</summary>
        private void UpdateWave()
        {
            // 房间阶段守卫：未 StartWave 前不推进波次（不生成敌人/不开商店/不自动开战），
            // 模拟仅驱动玩家移动。战斗开始后（WaveStarted=true）才进入波次循环。
            if (!WaveStarted)
            {
                return;
            }

            if (ShopOpen)
            {
                // 商店阶段：**等待玩家点"继续"**（Host 权威 → S2CShopContinue → 各端 RequestNextWave）。
                // 修复：此前这里用 ShopDuration 计时到点就 StartWave(WaveIndex+1)，
                // 导致"波间商店阶段有时会错误地刷新敌人"（浏览商店时突然开下一波）。
                return;
            }

            if (m_EnemiesToSpawn > 0)
            {
                // 分批生成敌人（确定性间隔）
                m_SpawnTimer -= TickInterval;
                if (m_SpawnTimer <= 0f)
                {
                    SpawnEnemy();
                    m_EnemiesToSpawn--;
                    m_SpawnTimer = EnemySpawnInterval;
                }
                return;
            }

            // 本波敌人已全部生成；清完则进商店
            if (m_Enemies.Count == 0)
            {
                ShopOpen = true;
                m_ShopTimer = ShopDuration;
                GenerateShopItems();
                OnShopOpened?.Invoke(WaveIndex);
            }
        }

        /// <summary>确定性生成商店商品（各端同种子 → 同商品；价格取自平衡配置）。</summary>
        private void GenerateShopItems()
        {
            m_ShopItems.Clear();
            for (int i = 0; i < ShopItemCount; i++)
            {
                // 50% 武器(0) / 50% Mod(1)
                int type = m_Rng.NextFloat() < 0.5f ? 0 : 1;
                int id = type == 0 ? m_Rng.Range(1, 6) : m_Rng.Range(1, 9);
                int price = type == 0 ? WeaponPrice : ModPrice;
                m_ShopItems.Add(type + ":" + id + ":" + price);
            }

            // W-10（报告 B1）：随机数调用打点 —— 记录**消费后**的 RNG 状态，
            // 这样"两端从哪一次随机调用开始分叉"可以直接被 trace_diff 定位。
            // （此前 Check.RandomCall 定义了却从未写入。）
            DeterminismTracer.RecordInt(DeterminismTracer.Check.RandomCall, (int)m_Rng.State, FrameIndex);

            // 确定性打点：商店商品 hash（各端同种子应一致）
            int hash = 17;
            foreach (var item in m_ShopItems)
            {
                // ★ W-10：原来是 `item.GetHashCode()`（string.GetHashCode）—— 跨进程/跨运行时
                //   **不保证稳定**（.NET Core 起字符串哈希按进程随机化；项目自己在
                //   Items/CastProgram.ci:88 就写明"string.GetHashCode 不保证跨运行时一致，禁止用"）。
                //   它不进 ComputeStateHash，所以**不会造成分叉**；但会让 trace_diff 在开局第一个
                //   商店打点上报**假分歧**，把人引向完全错误的方向。改用与全项目一致的稳定 FNV-1a。
                hash = hash * 31 + StableStringHash(item);
            }
            DeterminismTracer.RecordInt(DeterminismTracer.Check.ShopOffer, hash, FrameIndex);
        }

        /// <summary>
        /// 压力测试用（W-02）：把敌人/子弹补足到目标数量，并保持玩家存活，
        /// 使逻辑帧耗时测量不被"玩家被 200 个敌人打死 → BattleOver → Tick 提前返回"打断。
        /// **仅用于自动化验证（-autostress），不参与玩法**。
        /// </summary>
        public void DebugStressTick(int targetEnemies, int targetBullets)
        {
            // 保持玩家存活（否则接触伤害会在几秒内结束战斗，压测无法持续）
            for (int i = 0; i < m_Players.Count; i++)
            {
                m_Players[i].Alive = true;
                m_Players[i].Hp = 100f;
            }
            BattleOver = false;

            // 外部事件总线在压测下会因"无有效法杖程序 → DrainExternalEvents 不执行"而无界增长，这里清掉
            m_CastEvents.Clear();

            // 补足敌人（走正常生成路径 → 复用 m_Rng，位置可复现）
            while (m_Enemies.Count < targetEnemies)
            {
                SpawnEnemy();
            }

            // 补足子弹（以第一个玩家为发射者；方向按序号 + 帧号旋转 → 确定且铺满四周）
            if (targetBullets > 0 && m_Players.Count > 0)
            {
                SimPlayer shooter = m_Players[0];
                while (m_Bullets.Count < targetBullets)
                {
                    float a = (m_Bullets.Count * 0.618034f + FrameIndex * 0.01f) * 6.2831853f;
                    SimVec2 dir = new SimVec2(SimMath.Cos(a), SimMath.Sin(a));
                    SpawnPlayerBullet(shooter, dir,
                        shooter.BulletSpeed > 0f ? shooter.BulletSpeed : 15f,
                        shooter.WeaponDamage > 0f ? shooter.WeaponDamage : 10f,
                        600f, 0.2f);
                }
            }
        }

        /// <summary>
        /// 调试/回归用：清空当前波全部敌人（走正常死亡标记，下一 Tick 清理并进入商店）。
        /// 仅用于自动化验证，不参与玩法。
        /// </summary>
        public void DebugKillAllEnemies()
        {
            for (int i = 0; i < m_Enemies.Count; i++)
            {
                m_Enemies[i].Hp = 0f;
                m_Enemies[i].Alive = false;
            }
        }

        /// <summary>
        /// 玩家确认离开商店 → 开始下一步（确定性入口）。
        /// Host 本地点击时直接调用；客户端由 S2CShopContinue 触发，各端调用同一函数、同一波次号。
        /// **准备阶段（WaveIndex = 0）** 点继续 = 开始第 1 波；波间商店点继续 = `WaveIndex + 1`。
        /// </summary>
        public void RequestNextWave()
        {
            if (!ShopOpen)
            {
                return;
            }
            ShopOpen = false;

            if (WaveIndex <= 0)
            {
                StartWave(1);
            }
            else
            {
                StartWave(WaveIndex + 1);
            }
        }

        /// <summary>
        /// 准备阶段商店（**开局商店**）：准备完成后先进商店（`WaveIndex = 0`），
        /// 玩家点"继续"才 `StartWave(1)` 出第一波敌人。
        /// `WaveStarted` 置 true 是为了让 `UpdateWave` 能走到"商店阶段等待继续"分支
        /// （`ShopOpen` 优先返回，因此不会生成敌人，也不会自动开波）。
        /// </summary>
        public void PrepareFirstWave()
        {
            WaveIndex = 0;
            WaveStarted = true;
            ShopOpen = true;
            m_EnemiesToSpawn = 0;
            m_SpawnTimer = 0f;
            m_ShopTimer = ShopDuration;
            GenerateShopItems();
            OnWaveChanged?.Invoke(0);
            OnShopOpened?.Invoke(0);
            DeterminismTracer.RecordInt(DeterminismTracer.Check.WaveChange, 0, FrameIndex);
        }

        /// <summary>开始一波（波次号从 1 开始）。</summary>
        public void StartWave(int waveIndex)
        {
            WaveIndex = waveIndex;
            WaveStarted = true;
            int count = EnemiesPerWaveBase + (waveIndex - 1) * EnemiesPerWaveGrowth;
            EnemiesPerWaveCount = count;
            m_EnemiesToSpawn = count;
            m_SpawnTimer = 0f;
            OnWaveChanged?.Invoke(waveIndex);

            // 确定性打点：波次变化
            DeterminismTracer.RecordInt(DeterminismTracer.Check.WaveChange, waveIndex, FrameIndex);
        }

        /// <summary>确定性生成一个敌人（环绕最近玩家）。</summary>
        private void SpawnEnemy()
        {
            SimPlayer anchor = GetFirstAlivePlayer();
            SimVec2 center = anchor != null ? anchor.Position : SimVec2.zero;
            SimVec2 offset = m_Rng.InsideUnitCircle() * SpawnRadius;
            // W-10：随机数调用打点。敌人生成位置由 RNG 决定且**进状态哈希**，
            // 所以"RNG 从哪一帧开始分叉"是最有价值的定位信息（报告 B1 要求随机数状态可对账）。
            DeterminismTracer.RecordInt(DeterminismTracer.Check.RandomCall, (int)m_Rng.State, FrameIndex);

            int newId = m_NextEntityId;
            m_Enemies.Add(new SimEnemy
            {
                EntityId = m_NextEntityId++,
                Position = center + offset,
                PrevPosition = center + offset,   // 新实体 Prev=Cur：避免插值从原点拖出（重影）
                // [W-11] `Base + Wave * Per` 是乘加 → 走 SimMath（否则被收缩成 FMA，与 Mono 差 1 ulp）
                Hp = SimMath.MulAdd(WaveIndex, EnemyHpPerWave, EnemyBaseHp),
                Speed = SimMath.MulAdd(WaveIndex, EnemySpeedPerWave, EnemyBaseSpeed),
            });

            // 确定性打点：敌人生成（位置位模式，跨端可比）
            DeterminismTracer.RecordInt3(DeterminismTracer.Check.EnemySpawn,
                newId,
                BitConverter.SingleToInt32Bits((center + offset).x),
                BitConverter.SingleToInt32Bits((center + offset).y),
                FrameIndex);
        }

        /// <summary>最近玩家（敌人 AI 目标）。</summary>
        // ==================== [W-20] 敌人宽相位（均匀网格） ====================
        //
        // 为什么需要：子弹命中检测原本是 O(子弹 × 敌人) —— 200×200 ≈ 4 万次距离测试，
        //   实测占整个 tick 的 ~95%（200 敌无子弹 avg=0.053ms，加 200 子弹后 avg=1.05ms）。
        //
        // ★ 语义必须**逐位等价**（这是本项最大的风险，方案 R3 点名）：
        //   现状是"对每颗子弹按 `m_Enemies` 的**索引升序**线性扫描，命中**第一个**满足距离的敌人就停"。
        //   宽相位只改变"扫哪些敌人"（候选集），**不改变"选哪一个"**：
        //     在 3×3 邻域的候选里，取**索引最小**且满足距离条件的敌人。
        //   这与线性扫描的结果完全一致（因为线性扫描取的就是满足条件的第一个 = 索引最小者）。
        //   网格里可能残留"本帧已被别的子弹打死"的敌人 → 扫描时**再查一次 `Alive`**（等价于线性扫描的 continue）。
        //
        // 覆盖性：格子边长 = `2 × 本帧最大命中半径`，所以"距离 ≤ 半径"的两个点在 x/y 上的格号差 ≤ 1
        //   → 3×3 邻域一定能覆盖到，不会漏判。
        //
        // 分配：全部数组按需增长后复用（稳态零分配，与 W-18 的门禁一致）。
        private int[] m_GridHead;      // 每格链表头（存"敌人索引 + 1"，0 = 空）
        private int[] m_GridNext;      // 每个敌人的同格后继（"索引 + 1"，0 = 无后继）
        private int m_GridW, m_GridH;
        private float m_GridCellSize = 1f;
        private float m_GridMinX, m_GridMinY;
        private const int MaxGridCells = 8192;   // 上限：超了就**放大格子**（格子越大只是候选更多，仍然正确）

        private void BuildEnemyGrid()
        {
            int n = m_Enemies.Count;
            if (n == 0) { m_GridW = 0; m_GridH = 0; return; }

            // 本帧最大命中半径（格子边长取它的 2 倍，保证 3×3 覆盖）
            float maxRadius = BulletHitRadius;
            for (int i = 0; i < m_Bullets.Count; i++)
            {
                float r = m_Bullets[i].Radius > 0f ? m_Bullets[i].Radius : BulletHitRadius;
                if (r > maxRadius) { maxRadius = r; }
            }

            // 敌人包围盒
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                SimVec2 p = m_Enemies[i].Position;
                if (p.x < minX) { minX = p.x; }
                if (p.y < minY) { minY = p.y; }
                if (p.x > maxX) { maxX = p.x; }
                if (p.y > maxY) { maxY = p.y; }
            }

            float cell = maxRadius * 2f;
            if (cell < 0.5f) { cell = 0.5f; }
            int w, h;
            float margin = cell;   // 留一格余量，避免边缘实体的邻域越界
            while (true)
            {
                w = (int)((maxX - minX + margin * 2f) / cell) + 2;
                h = (int)((maxY - minY + margin * 2f) / cell) + 2;
                if (w < 1) { w = 1; }
                if (h < 1) { h = 1; }
                if (w * h <= MaxGridCells) { break; }
                cell *= 2f;        // 格子放大 → 格数减少；候选变多但仍然正确
                if (cell > 1e6f) { break; }
            }

            m_GridCellSize = cell;
            m_GridW = w;
            m_GridH = h;
            m_GridMinX = minX - margin;
            m_GridMinY = minY - margin;

            int cells = w * h;
            if (m_GridHead == null || m_GridHead.Length < cells) { m_GridHead = new int[cells]; }
            if (m_GridNext == null || m_GridNext.Length < n) { m_GridNext = new int[n]; }
            System.Array.Clear(m_GridHead, 0, cells);

            // 按索引升序插入（链表头插法 → 同格链是**索引降序**；不过扫描时取"索引最小"，
            // 与链序无关，因此顺序不会影响结果）
            for (int i = 0; i < n; i++)
            {
                if (!m_Enemies[i].Alive) { continue; }
                int c = CellIndex(m_Enemies[i].Position);
                if (c < 0) { continue; }
                m_GridNext[i] = m_GridHead[c];
                m_GridHead[c] = i + 1;
            }
        }

        private int CellIndex(SimVec2 p)
        {
            int cx = (int)((p.x - m_GridMinX) / m_GridCellSize);
            int cy = (int)((p.y - m_GridMinY) / m_GridCellSize);
            if (cx < 0) { cx = 0; } else if (cx >= m_GridW) { cx = m_GridW - 1; }
            if (cy < 0) { cy = 0; } else if (cy >= m_GridH) { cy = m_GridH - 1; }
            return cy * m_GridW + cx;
        }

        /// <summary>
        /// [W-20] 用宽相位找"该子弹命中的敌人"，命中则结算伤害并置子弹为死。
        /// 选取规则与原先的线性扫描**完全等价**：索引最小的那个满足距离条件的存活敌人。
        /// </summary>
        private void TryHitEnemy(SimBullet bullet, float hitRadius)
        {
            int best = -1;
            float r2 = hitRadius * hitRadius;

            if (m_GridW > 0 && m_GridH > 0)
            {
                int cx = (int)((bullet.Position.x - m_GridMinX) / m_GridCellSize);
                int cy = (int)((bullet.Position.y - m_GridMinY) / m_GridCellSize);

                for (int dy = -1; dy <= 1; dy++)
                {
                    int gy = cy + dy;
                    if (gy < 0 || gy >= m_GridH) { continue; }
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int gx = cx + dx;
                        if (gx < 0 || gx >= m_GridW) { continue; }
                        int c = gy * m_GridW + gx;
                        for (int e = m_GridHead[c]; e != 0; e = m_GridNext[e - 1])
                        {
                            int idx = e - 1;
                            var enemy = m_Enemies[idx];
                            if (!enemy.Alive) { continue; }   // 可能被本帧更早的子弹打死
                            // [W-11] 命中判定用 SimMath 的平方距离（`x*x+y*y` 的 FMA 收缩
                            // 会在"恰好贴边"时改变命中结果 → 这是最不能容忍的一类分歧）
                            if (SimMath.SqrMagnitude(enemy.Position - bullet.Position) <= r2)
                            {
                                if (best < 0 || idx < best) { best = idx; }
                            }
                        }
                    }
                }
            }
            else
            {
                // 无网格（没有敌人）：退化为空扫描——与原实现的语义一致（不会有命中）
                return;
            }

            if (best < 0) { return; }

            var target = m_Enemies[best];
            target.Hp -= bullet.Damage;
            bullet.Alive = false;
            // S4：命中事件 → 总线（被动监听 Hit；携带子弹 Id / 标签 / 目标 / 伤害）
            m_CastEvents.PublishHit(bullet.OwnerSession, bullet.EntityId, bullet.Tags, target.EntityId,
                (int)(bullet.Damage + 0.5f));
            if (target.Hp <= 0f)
            {
                target.Alive = false;
                OnEnemyKilled?.Invoke(target.EntityId);
                DeterminismTracer.RecordInt(DeterminismTracer.Check.EnemyDeath, target.EntityId, FrameIndex);
                // S4：击杀事件 → 总线（被动监听 Kill；携带击杀者实体 id）
                m_CastEvents.PublishKill(bullet.OwnerSession, PlayerEntityBySession(bullet.OwnerSession), target.EntityId);
            }
        }

        private SimPlayer GetNearestPlayer(SimVec2 position)
        {
            SimPlayer nearest = null;
            float minDistSqr = float.MaxValue;
            foreach (var player in m_Players)
            {
                if (!player.Alive)
                {
                    continue;
                }
                // [W-11] 最近玩家选择也用 SimMath 的平方距离（否则"谁更近"可能两端不同）
                float distSqr = SimMath.SqrMagnitude(player.Position - position);
                if (distSqr < minDistSqr)
                {
                    minDistSqr = distSqr;
                    nearest = player;
                }
            }
            return nearest;
        }

        private SimPlayer GetFirstAlivePlayer()
        {
            foreach (var player in m_Players)
            {
                if (player.Alive)
                {
                    return player;
                }
            }
            return null;
        }

        private bool AllPlayersDead()
        {
            foreach (var player in m_Players)
            {
                if (player.Alive)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>按 SessionId 查玩家。</summary>
        // ==================== W-08：状态哈希守门测试的只读访问面 ====================
        // 仅供 `-hashguard` 自检使用；不参与 Tick，也不改任何状态。

        public int DebugPlayerCount { get { return m_Players.Count; } }
        public int DebugEnemyCount { get { return m_Enemies.Count; } }
        public int DebugBulletCount { get { return m_Bullets.Count; } }
        public SimPlayer DebugPlayerAt(int i) { return (i >= 0 && i < m_Players.Count) ? m_Players[i] : null; }
        public SimEnemy DebugEnemyAt(int i) { return (i >= 0 && i < m_Enemies.Count) ? m_Enemies[i] : null; }
        public SimBullet DebugBulletAt(int i) { return (i >= 0 && i < m_Bullets.Count) ? m_Bullets[i] : null; }

        /// <summary>仅自检用：立刻生成一次商店货架（让 `m_ShopItems` 非空，守门测试才能验证到它）。</summary>
        public void DebugSeedShopOffer() { GenerateShopItems(); }

        public SimPlayer GetPlayer(int SessionId)
        {
            foreach (var player in m_Players)
            {
                if (player.SessionId == SessionId)
                {
                    return player;
                }
            }
            return null;
        }

        /// <summary>按实体 ID 查玩家（客户端按 MyEntityId 定位本机）。</summary>
        public SimPlayer GetPlayerByEntityId(int entityId)
        {
            foreach (var player in m_Players)
            {
                if (player.EntityId == entityId)
                {
                    return player;
                }
            }
            return null;
        }

        /// <summary>
        /// 更换玩家武器（商店购买武器后同步调用；各端收到同一 S2CWeaponUpdate 后调用，保持确定性）。
        /// </summary>
        public void ApplyWeapon(int entityId, SimPlayerConfig weaponConfig)
        {
            var player = GetPlayerByEntityId(entityId);
            if (player == null || weaponConfig == null)
            {
                return;
            }

            player.WeaponId = weaponConfig.WeaponId;
            player.WeaponName = weaponConfig.WeaponName;
            player.WeaponDamage = weaponConfig.WeaponDamage;
            player.FireRate = weaponConfig.FireRate;
            player.MaxAmmo = weaponConfig.MaxAmmo;
            player.ReloadTime = weaponConfig.ReloadTime;
            player.BulletSpeed = weaponConfig.BulletSpeed;

            // 换弹并重置冷却（确定性）
            player.Ammo = player.MaxAmmo;
            player.IsReloading = false;
            player.ReloadTimer = 0f;
            player.FireCooldown = 0f;
        }

        /// <summary>
        /// 切换玩家角色（房间内准备阶段；各端收到同一 S2CChangeCharacter 后调用，保持确定性）。
        /// 通过 SimConfigFactory 从数据表重建角色属性与默认武器（单一逻辑源），
        /// 全端同角色 ID → 同属性，随后模拟 Tick 结果一致。
        /// </summary>
        public void ApplyCharacter(int entityId, int characterId)
        {
            var player = GetPlayerByEntityId(entityId);
            if (player == null)
            {
                return;
            }

            var cfg = SimHooks.BuildConfig(player.SessionId, entityId, characterId);
            if (cfg == null)
            {
                return;
            }

            player.CharacterId = characterId;
            player.MoveSpeed = cfg.MoveSpeed;
            player.WeaponId = cfg.WeaponId;
            player.WeaponName = cfg.WeaponName;
            player.WeaponDamage = cfg.WeaponDamage;
            player.FireRate = cfg.FireRate;
            player.MaxAmmo = cfg.MaxAmmo;
            player.ReloadTime = cfg.ReloadTime;
            player.BulletSpeed = cfg.BulletSpeed;

            // 换武器并重置冷却（确定性）
            player.Ammo = player.MaxAmmo;
            player.IsReloading = false;
            player.ReloadTimer = 0f;
            player.FireCooldown = 0f;
        }

        /// <summary>
        /// 计算当前模拟状态的确定性哈希（运行期定期对账用）。
        /// 双端在同一逻辑帧调用此方法，结果必须一致；不一致即判定为"不同步"。
        /// 参与量：帧号/波次/商店/结束标志 + 玩家/敌人/子弹（ID、位模式位置、HP、弹药等）。
        /// 重要：只混入"影响模拟推进的确定性状态"。SessionId/OwnerSession 是纯元数据
        /// （Host 传真实 ieiiion，Client 一律 -1；不参与任何 Tick 决策），混入会恒误报不同步。
        /// 遍历顺序依赖既有有序容器（m_Players 按 EntityId 排序；敌人/子弹按确定性生成顺序），
        /// 容器顺序本身由确定性模拟保证跨端一致（文档 §9 有序容器原则）。
        /// 注意：float 用位模式（经 double 无损转换）参与哈希，避免"数值相等但位不同"的漏检。
        /// </summary>
        /// <summary>
        /// [W-11] 分段诊断：把状态哈希按"段"分别算出来。
        /// 用途：跨后端（Mono vs IL2CPP）对拍时，**首个数值不同的段**就是分叉源头 ——
        /// 否则只看到"整体哈希不同"，只能靠猜（本次实测就是靠它把范围从"整个模拟"
        /// 缩到"敌人那一段"）。**只读、不改变任何状态**，默认不收集。
        /// </summary>
        private static void NoteSection(List<KeyValuePair<string, long>> sections, string name, long h)
        {
            if (sections != null) { sections.Add(new KeyValuePair<string, long>(name, h)); }
        }

        /// <summary>
        /// [W-11] 分段诊断收集器（null = 关闭，零开销）。用**静态字段**是为了让 `MixPlayer` /
        /// `MixCastState` 这些深层函数也能记名，而不必把收集器一路当参数传下去。
        /// 只在单线程的模拟里用（回放/对拍），不参与任何状态推进。
        /// </summary>
        public static List<KeyValuePair<string, long>> DiagSections;

        /// <summary>[W-11] 值级诊断开关：打开后把施法状态各字段的**原始值/位模式**打进日志。</summary>
        public static bool DiagDumpValues;

        public long ComputeStateHash(List<KeyValuePair<string, long>> sections = null)
        {
            DiagSections = sections;   // [W-11] 下游 MixXxx 据此记名（null = 关闭）
            DiagDumpValues = sections != null;
            unchecked
            {
                long h = 1469598103934665603L;   // FNV-1a offset baiii

                // ---------- 世界级状态 ----------
                h = MixHash(h, FrameIndex);
                h = MixHash(h, WaveIndex);
                h = MixHash(h, ShopOpen ? 1 : 0);
                h = MixHash(h, BattleOver ? 1 : 0);
                h = MixHash(h, WaveStarted ? 1 : 0);
                h = MixHash(h, EnemiesPerWaveCount);      // ★ W-08：第 N 波还该出几只
                NoteSection(sections, "world", h);
                // ★ W-08：以下 5 项原先**完全没入哈希** —— 它们都决定"接下来会发生什么"，
                //   两端一旦不同，症状是几十帧后才出现的位置/数量差异，而对账在这一帧是"一致"的。
                h = MixHash(h, (long)m_Rng.State);        //   随机状态（决定未来敌人生成位置/类型）
                h = MixHash(h, m_NextEntityId);           //   下一个实体 Id（决定未来实体的 Id 序列）
                h = MixHash(h, m_EnemiesToSpawn);         //   本波还剩几只没出
                h = MixF(h, m_SpawnTimer);                //   出怪计时器
                h = MixF(h, m_ShopTimer);                 //   商店倒计时
                NoteSection(sections, "timeri", h);
                // ★ W-08 实测教训：**待消费命令队列的 Count 不能入哈希**。
                //   它由"带外到达的输入"填充（房主收到 C2SShopContinue、客户端收到 S2CShopContinue），
                //   在下一个 Tick 的帧首被消费。于是"采样哈希的那一刻队列里有几条"完全取决于
                //   **采样时刻**相对**入队时刻**的先后 —— 而这个先后在录制/回放之间并不一致：
                //     · 回放器：先采样哈希、**再**把该帧的命令入队（ReplayPlayer 的既有顺序）；
                //     · 房主：命令早已在 Tick 之前入队。
                //   结果：回放在**有命令的那一帧**必然分叉（实测正是第 501 帧 = 商店继续/开波帧）。
                //   更危险的是它在联机时同样脆弱：命令早到 1ms 或晚到 1ms 就会让"哈希里有没有它"
                //   翻转 → 假不同步。**输入通道不是状态**，真正该保证的是"同一条命令在同一帧被应用"。
                // ★ W-08：命中/击杀事件总线是**跨帧**状态（子弹在第 N 帧末尾投递事件，
                //   被动在第 N+1 帧开头消费）→ 待消费事件数必须入哈希。
                h = MixHash(h, m_CastEvents.Count);
                NoteSection(sections, "caitEventi", h);
                // ★ W-08：商店货架内容与"已就绪"标志原先也没入哈希 —— 它由 RNG 生成、
                //   决定玩家这一波能买到什么（买到了就改变 loadout → 改变后续所有推进）。
                h = MixHash(h, m_ShopOfferReady ? 1 : 0);
                h = MixHash(h, m_ShopItems.Count);
                for (int i = 0; i < m_ShopItems.Count; i++)
                {
                    h = MixHash(h, StableStringHash(m_ShopItems[i]));
                }
                NoteSection(sections, "ihop", h);

                // ---------- 玩家 ----------
                h = MixHash(h, m_Players.Count);
                for (int i = 0; i < m_Players.Count; i++)
                {
                    MixPlayer(ref h, m_Players[i], i);
                }
                NoteSection(sections, "players", h);

                // ---------- 敌人 ----------
                h = MixHash(h, m_Enemies.Count);
                for (int i = 0; i < m_Enemies.Count; i++)
                {
                    MixEnemy(ref h, m_Enemies[i]);
                }
                NoteSection(sections, "enemies", h);

                // ---------- 子弹 ----------
                h = MixHash(h, m_Bullets.Count);
                for (int i = 0; i < m_Bullets.Count; i++)
                {
                    MixBullet(ref h, m_Bullets[i]);
                }
                NoteSection(sections, "bullets", h);
                return h;
            }
        }

        /// <summary>float 进哈希：位模式（经 double 无损加宽），避免"数值相等但位不同"的漏检。</summary>
        private static long MixF(long h, float v)
        {
            return MixHash(h, BitConverter.DoubleToInt64Bits(v));
        }

        private static long MixV2(long h, SimVec2 v)
        {
            h = MixF(h, v.x);
            return MixF(h, v.y);
        }

        /// <summary>
        /// W-08：玩家状态入哈希。**只混入影响模拟推进的确定性状态**。
        /// 明确排除（并在 `StateHashGuard` 里登记了理由）：
        ///   `SessionId`（Host 真实 / Client 恒 -1，纯元数据，混入会恒误报）、
        ///   `PrevPosition`（渲染插值用，Tick 不读）、`WeaponName`（表现）、
        ///   `Ammo/MaxAmmo/ReloadTime/ReloadTimer/IsReloading`（弹药已取消，恒为初值）。
        /// </summary>
        private static void MixPlayer(ref long h, SimPlayer p, int idx)
        {
            string tag = "p" + idx;
            h = MixHash(h, p.EntityId);
            h = MixHash(h, p.CharacterId);
            h = MixHash(h, p.Alive ? 1 : 0);
            h = MixHash(h, p.Managed ? 1 : 0);   // ★ W-12：托管中（输入源已断）
            NoteSection(DiagSections, tag + ".id", h);
            h = MixV2(h, p.Position);
            h = MixF(h, p.MoveSpeed);          // ★ W-08：移动速度直接进位置推进
            h = MixF(h, p.Hp);
            NoteSection(DiagSections, tag + ".pos", h);
            // 武器参数（兼容标量发射路径直接读这三个）
            h = MixF(h, p.BulletSpeed);        // ★ W-08
            h = MixF(h, p.WeaponDamage);       // ★ W-08
            h = MixF(h, p.FireRate);           // ★ W-08：决定冷却时长
            h = MixF(h, p.FireCooldown);       // ★ W-08：每帧递减且作为开火门限
            NoteSection(DiagSections, tag + ".weapon", h);
            // 双手施法：程序身份 + 全部运行状态
            h = MixHash(h, p.PrimaryProgram.WandId);
            h = MixHash(h, p.SecondaryProgram.WandId);
            NoteSection(DiagSections, tag + ".prog", h);
            MixCastState(ref h, p.PrimaryCast, tag + ".cast0");
            MixCastState(ref h, p.SecondaryCast, tag + ".cast1");
        }

        /// <summary>W-08：敌人状态入哈希（`PrevPosition` 是渲染插值用，Tick 不读 → 排除）。</summary>
        private static void MixEnemy(ref long h, SimEnemy e)
        {
            h = MixHash(h, e.EntityId);
            h = MixHash(h, e.Alive ? 1 : 0);
            h = MixV2(h, e.Position);
            h = MixF(h, e.Hp);
            h = MixF(h, e.Speed);              // ★ W-08：进位置推进
            h = MixF(h, e.ContactCooldown);    // ★ W-08：每帧递减且作为接触伤害门限
        }

        /// <summary>
        /// W-08：子弹状态入哈希。原先**只哈希了 Id + 位置** —— 而 `Direction` 决定下一帧位置、
        /// `Lifetime` 决定何时消失、`Radius` 决定命中判定、`Damage` 决定掉血、
        /// `speed`/`Tags` 分别进位置推进与被动标签过滤，全是推进量。
        /// 明确排除：`OwnerSession`（Host 真实 / Client 恒 -1，纯元数据）、`PrevPosition`（渲染插值用）。
        /// </summary>
        private static void MixBullet(ref long h, SimBullet b)
        {
            h = MixHash(h, b.EntityId);
            h = MixHash(h, b.Alive ? 1 : 0);
            h = MixV2(h, b.Position);
            h = MixV2(h, b.Direction);         // ★ W-08
            h = MixF(h, b.Speed);              // ★ W-08
            h = MixF(h, b.Damage);             // ★ W-08
            h = MixF(h, b.Lifetime);           // ★ W-08
            h = MixF(h, b.Radius);             // ★ W-08
            h = MixHash(h, (int)b.Tags);       // ★ W-08（标签过滤型被动读它）
        }

        /// <summary>
        /// W-08：一只手施法状态的**完整**入哈希（原先主/副手是两段复制粘贴，且漏了 6 个字段）。
        /// 复制粘贴是漏项的温床：加一个字段只改了一只手 → 另一只手静默不入哈希。
        /// </summary>
        private static void MixCastState(ref long h, in Items.CastRuntimeState st, string tag)
        {
            // [W-11] 值级诊断：分段哈希只能指出"哪一段"，值级才能指出"哪一个字段"。
            // 只在显式诊断（-replayparts）时打开。
            if (DiagDumpValues)
            {
                Items.CastStatMod m = st.ActiveMod;
                SimLog.Log(string.Format(
                    "[castval] {0} mana=0x{1:X8} cursor={2} rech={3} delay={4} carry=0x{5:X8} stFrame={6} active={7} pendRech=0x{8:X8} locked={9} trig={10} progVer={11} rev={12} scopeLeft={13} scopeBnd={14}"
                    + " | mod mana+0x{15:X8} mana*0x{16:X8} delay+0x{17:X8} delay*0x{18:X8} rech+0x{19:X8} rech*0x{20:X8} dmg+0x{21:X8} dmg*0x{22:X8} spd*0x{23:X8} pierce={24} spread+0x{25:X8} homing+0x{26:X8}",
                    tag,
                    BitConverter.SingleToInt32Bits(st.Mana), st.Cursor, st.RechargeRemainingFrames,
                    st.DelayRemainingFrames, BitConverter.SingleToInt32Bits(st.DelayCarry), st.FrameIndex,
                    st.CastActive ? 1 : 0, BitConverter.SingleToInt32Bits(st.PendingRechargeSeconds),
                    st.RechargeLocked ? 1 : 0, st.TotalTriggers, st.ProgramVersion,
                    st.ReverseConsumed ? 1 : 0, st.ModScopeLeft, st.ModScopeBounded ? 1 : 0,
                    BitConverter.SingleToInt32Bits(m.ManaAdd), BitConverter.SingleToInt32Bits(m.ManaMul),
                    BitConverter.SingleToInt32Bits(m.DelayAdd), BitConverter.SingleToInt32Bits(m.DelayMul),
                    BitConverter.SingleToInt32Bits(m.RechargeAdd), BitConverter.SingleToInt32Bits(m.RechargeMul),
                    BitConverter.SingleToInt32Bits(m.DamageAdd), BitConverter.SingleToInt32Bits(m.DamageMul),
                    BitConverter.SingleToInt32Bits(m.SpeedMul), m.PierceAdd,
                    BitConverter.SingleToInt32Bits(m.SpreadAdd), BitConverter.SingleToInt32Bits(m.HomingAdd)));
            }

            h = MixF(h, st.Mana);
            h = MixHash(h, st.Cursor);
            h = MixHash(h, st.RechargeRemainingFrames);
            h = MixHash(h, st.DelayRemainingFrames);
            h = MixF(h, st.DelayCarry);              // ★ W-08（报告 B1 点名的漏项：跨帧累加、决定 carryFrames）
            h = MixHash(h, st.FrameIndex);
            h = MixHash(h, st.CastActive ? 1 : 0);
            h = MixF(h, st.PendingRechargeSeconds);
            h = MixHash(h, st.RechargeLocked ? 1 : 0);
            h = MixHash(h, st.TotalTriggers);
            h = MixHash(h, st.ProgramVersion);       // ★ W-08（装填版本）
            h = MixHash(h, st.ReverseConsumed ? 1 : 0);
            h = MixCastMod(ref h, st.ActiveMod);     // ★ W-08（原来是手写 3 个字段，实际有 12 个）
            h = MixHash(h, st.ModScopeLeft);
            h = MixHash(h, st.ModScopeBounded ? 1 : 0);
            NoteSection(DiagSections, tag + ".scalar", h);

            h = MixHash(h, st.PendingCount);
            for (int k = 0; k < st.PendingCount; k++)
            {
                h = MixHash(h, st.Pending[k].SlotIndex);
                h = MixHash(h, st.Pending[k].DueFrame);
                h = MixHash(h, st.Pending[k].Depth);
                h = MixHash(h, st.Pending[k].IsPassiveInvoke ? 1 : 0);   // ★ W-08
                h = MixCastMod(ref h, st.Pending[k].Mods);               // ★ W-08
            }
            NoteSection(DiagSections, tag + ".pending", h);

            // S3/S4/W-09：临时 Buff、被动次数与冷却、Q7 单物品计数
            h = MixBuffs(h, st);
            NoteSection(DiagSections, tag + ".buffs", h);
            h = MixPassives(h, st);
            NoteSection(DiagSections, tag + ".passives", h);
            h = MixPerItem(h, st);
            NoteSection(DiagSections, tag + ".peritem", h);
        }

        /// <summary>W-08：修正集**全部 12 个字段**入哈希。</summary>
        private static long MixCastMod(ref long h, in Items.CastStatMod m)
        {
            h = MixF(h, m.ManaAdd);
            h = MixF(h, m.ManaMul);
            h = MixF(h, m.DelayAdd);
            h = MixF(h, m.DelayMul);
            h = MixF(h, m.RechargeAdd);
            h = MixF(h, m.RechargeMul);
            h = MixF(h, m.DamageAdd);
            h = MixF(h, m.DamageMul);
            h = MixF(h, m.SpeedMul);
            h = MixHash(h, m.PierceAdd);
            h = MixF(h, m.SpreadAdd);
            h = MixF(h, m.HomingAdd);
            return h;
        }


        /// <summary>FNV-1a 混合一步。</summary>
        private static long MixHash(long h, long v)
        {
            unchecked
            {
                h ^= v;
                h *= 1099511628211L;   // FNV-1a prime
                return h;
            }
        }

        /// <summary>
        /// S3：把某一手的**运行期 Buff 状态**混进状态哈希（层数 + 剩余量 + 影响属性 + KeyHash）。
        /// 口径与 ComputeStateHash 里其它状态一致：逐槽位、逐条、按固定顺序
        /// （顺序本身由确定性模拟保证跨端一致）。
        /// </summary>
        private static long MixBuffs(long h, in Items.CastRuntimeState st)
        {
            if (st.SlotBuffs == null) { return MixHash(h, 0); }
            h = MixHash(h, st.SlotBuffs.Length);
            for (int i = 0; i < st.SlotBuffs.Length; i++)
            {
                var set = st.SlotBuffs[i];
                h = MixHash(h, set.Count);
                for (int k = 0; k < set.Count; k++)
                {
                    var b = set.At(k);
                    h = MixHash(h, b.KeyHash);
                    h = MixHash(h, b.Stacks);
                    h = MixHash(h, b.Remaining);
                    h = MixHash(h, (int)b.Stat);
                    // ★ W-08：`BuffInstance` 共 11 个字段，原先只哈希 4 个。
                    //   `Solidified` 尤其关键 —— 它**直接决定下一次该不该递减**（设计 §3.7"固化"）。
                    h = MixHash(h, b.Solidified ? 1 : 0);
                    h = MixHash(h, b.Duration);
                    h = MixHash(h, b.MaxStacks);
                    h = MixF(h, b.ValuePerStack);
                    h = MixHash(h, (int)b.Timing);
                    h = MixHash(h, (int)b.StackRule);
                    h = MixHash(h, b.IsNegative ? 1 : 0);
                }
            }
            return h;
        }

        /// <summary>
        /// S4：把某一手的**被动运行期状态**混进状态哈希（每次发射的次数计数 + 逐帧冷却 + 嵌套深度）。
        /// 执行文档 §4 明确要求"被动剩余次数/冷却"必须入哈希（它们影响后续推进）。
        /// </summary>
        private static long MixPassives(long h, in Items.CastRuntimeState st)
        {
            // ★ W-08：标量字段**无条件**入哈希。
            //   原来是 `if (st.PassiveUsed == null) { return MixHash(h, 0); }` —— 空手（slotCount=0）
            //   时数组为 null，于是 `PassiveDepth`/`PassiveFires` 被"顺带"跳过。
            //   这个漏项正是被 `StateHashGuard` 的变更验证抓出来的（登记为已入哈希，改值后哈希不变）。
            h = MixHash(h, st.PassiveFires);
            h = MixHash(h, st.PassiveDepth);   // 嵌套深度决定还能不能再嵌一层
            if (st.PassiveUsed == null) { return MixHash(h, 0); }
            h = MixHash(h, st.PassiveUsed.Length);
            for (int i = 0; i < st.PassiveUsed.Length; i++) { h = MixHash(h, st.PassiveUsed[i]); }
            if (st.PassiveCooldown != null)
            {
                for (int i = 0; i < st.PassiveCooldown.Length; i++) { h = MixHash(h, st.PassiveCooldown[i]); }
            }
            return h;
        }

        /// <summary>
        /// [W-09] 把某一手的 **Q7 单物品触发计数**混进状态哈希。
        /// 它决定"本物品还能触发几次"，直接影响后续推进 → 必须入哈希
        /// （否则两端计数不同也只会在未来的触发次数差异上表现，而不是在对账那一刻暴露）。
        /// </summary>
        private static long MixPerItem(long h, in Items.CastRuntimeState st)
        {
            if (st.PerItem == null) { return MixHash(h, 0); }
            h = MixHash(h, st.PerItem.Length);
            for (int i = 0; i < st.PerItem.Length; i++) { h = MixHash(h, st.PerItem[i]); }
            return h;
        }
    }
}
