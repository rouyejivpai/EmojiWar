//------------------------------------------------------------
// EmojiWar GameMain - 确定性帧同步模拟核心（Lockstep）
// 所有端（Host + Clients）以相同输入序列 + 固定 tick 推进同一份模拟：
//   - 固定 tick 20Hz（0.05s），不使用 Time.deltaTime / UnityEngine.Random
//   - 输入只含"意图"（方向/瞄准/射击/装弹），不含位置结果
//   - 玩家移动、子弹、敌人 AI、波次、商店全部确定性计算
// 结果天然一致，网络只负责收集输入并广播输入帧。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

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

        public static PlayerIntent Empty { get { return new PlayerIntent { AimX = 1f }; } }
    }

    /// <summary>玩家模拟配置（初始化时从角色/武器数据表注入）。</summary>
    public sealed class SimPlayerConfig
    {
        public int SessionId;
        public int EntityId;
        public int CharacterId;
        public Vector2 StartPosition = Vector2.zero;
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
        public Vector2 Position;
        public Vector2 PrevPosition;   // 上一逻辑帧位置（渲染插值用）
        public float MoveSpeed;
        public float Hp = 100f;
        public bool Alive = true;

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
        public bool HasSecondary { get { return SecondaryProgram.IsValid; } }
    }

    /// <summary>模拟敌人。</summary>
    public sealed class SimEnemy
    {
        public int EntityId;
        public Vector2 Position;
        public Vector2 PrevPosition;   // 上一逻辑帧位置（渲染插值用）
        public float Hp;
        public float Speed;
        public bool Alive = true;
        public float ContactCooldown;   // 接触攻击冷却
    }

    /// <summary>模拟子弹。</summary>
    public sealed class SimBullet
    {
        public int EntityId;
        public Vector2 Position;
        public Vector2 PrevPosition;   // 上一逻辑帧位置（渲染插值用）
        public Vector2 Direction;
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
        /// <summary>固定逻辑 tick 间隔（20Hz）。</summary>
        public const float TickInterval = 0.05f;

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
        public Data.SpellSystemConfigSO SpellConfig;

        /// <summary>从 SpellSystemConfigSO 注入法术系统上限/兜底（无 SO 时用结构兜底）。</summary>
        public void ApplySpellConfig(Data.SpellSystemConfigSO cfg)
        {
            SpellConfig = cfg;
        }

        /// <summary>从 BattleConfigSO 注入波次参数（无 SO 时保持默认值）。</summary>
        public void ApplyBattleConfig(Data.BattleConfigSO cfg)
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
        private int PlayerEntityBySession(int sessionId)
        {
            for (int i = 0; i < m_Players.Count; i++)
            {
                if (m_Players[i].SessionId == sessionId) { return m_Players[i].EntityId; }
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
        /// <summary>玩家 HP 变化（参数：sessionId, hp）。</summary>
        public event Action<int, float> OnPlayerHpChanged;
        /// <summary>敌人被击杀（参数：entityId）。</summary>
        public event Action<int> OnEnemyKilled;

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
                    sb.Append(" secondary=").Append(p.SecondaryProgram.Dump())
                      .Append(" mana=").Append(p.SecondaryCast.Mana.ToString("F1"))
                      .Append(" cursor=").Append(p.SecondaryCast.Cursor);
                }
                else
                {
                    sb.Append(" secondary=none(空手)");
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

            FrameIndex++;

            // 确定性打点：帧开始（文档 §4）
            DeterminismTracer.RecordInt(DeterminismTracer.Check.FrameStart, FrameIndex, FrameIndex);

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

                // 移动（固定 dt）
                Vector2 moveDir = new Vector2(intent.MoveX, intent.MoveY);
                if (moveDir.sqrMagnitude > 1f)
                {
                    moveDir = moveDir.normalized;
                }
                player.Position += moveDir * player.MoveSpeed * TickInterval;

                // 射击冷却（主/副武器各自独立；有施法程序时冷却由 CastState 管理）
                player.FireCooldown -= TickInterval;

                // 瞄准语义：AimX/AimY 是"鼠标世界坐标目标点"（绝对位置），
                // 发射方向必须 = 目标点 − 玩家位置（相对方向）再归一化。
                Vector2 aimTarget = new Vector2(intent.AimX, intent.AimY);
                Vector2 aim = aimTarget - player.Position;
                if (aim.sqrMagnitude < 0.001f)
                {
                    aim = Vector2.right;
                }
                aim.Normalize();

                if (player.PrimaryProgram.IsValid || player.SecondaryProgram.IsValid)
                {
                    // 法杖编程式施法：一次发射跑完整条序列（Q1）+ 跨帧延迟队列（Q3）+ 三修正 + Q2 中止口径
                    CastResolver.Tick(player.PrimaryProgram, ref player.PrimaryCast, TickInterval, intent.FirePrimary,
                        s_CastPlanPrimary, SpellConfig, m_CastEvents, player.SessionId);
                    SpawnCastPlan(player, aim, s_CastPlanPrimary);

                    if (player.SecondaryProgram.IsValid)
                    {
                        CastResolver.Tick(player.SecondaryProgram, ref player.SecondaryCast, TickInterval, intent.FireSecondary,
                            s_CastPlanSecondary, SpellConfig, m_CastEvents, player.SessionId);
                        SpawnCastPlan(player, aim, s_CastPlanSecondary);
                    }
                }
                else if (intent.FirePrimary && player.FireCooldown <= 0f)
                {
                    // 兼容路径：没有法杖程序时按武器标量发射（旧数据/离线兜底）
                    SpawnPlayerBullet(player, aim, player.BulletSpeed, player.WeaponDamage);
                    player.FireCooldown = 1f / Mathf.Max(0.01f, player.FireRate);
                }
            }

            // 2. 子弹：飞行 + 命中
            foreach (var bullet in m_Bullets)
            {
                if (!bullet.Alive)
                {
                    continue;
                }

                bullet.Position += bullet.Direction * bullet.Speed * TickInterval;
                bullet.Lifetime -= TickInterval;
                if (bullet.Lifetime <= 0f)
                {
                    bullet.Alive = false;
                    continue;
                }

                // 命中检测：与敌人距离（确定性，无物理引擎）
                float hitRadius = bullet.Radius > 0f ? bullet.Radius : BulletHitRadius;
                foreach (var enemy in m_Enemies)
                {
                    if (!enemy.Alive)
                    {
                        continue;
                    }
                    if ((enemy.Position - bullet.Position).sqrMagnitude <= hitRadius * hitRadius)
                    {
                        enemy.Hp -= bullet.Damage;
                        bullet.Alive = false;
                        // S4：命中事件 → 总线（被动监听 Hit；携带子弹 Id / 标签 / 目标 / 伤害）
                        m_CastEvents.PublishHit(bullet.OwnerSession, bullet.EntityId, bullet.Tags, enemy.EntityId,
                            (int)(bullet.Damage + 0.5f));
                        if (enemy.Hp <= 0f)
                        {
                            enemy.Alive = false;
                            OnEnemyKilled?.Invoke(enemy.EntityId);
                            DeterminismTracer.RecordInt(DeterminismTracer.Check.EnemyDeath, enemy.EntityId, FrameIndex);
                            // S4：击杀事件 → 总线（被动监听 Kill；携带击杀者实体 id）
                            m_CastEvents.PublishKill(bullet.OwnerSession, PlayerEntityBySession(bullet.OwnerSession), enemy.EntityId);
                        }
                        break;
                    }
                }
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
                    Vector2 toTarget = target.Position - enemy.Position;
                    if (toTarget.sqrMagnitude > 0.01f)
                    {
                        enemy.Position += toTarget.normalized * enemy.Speed * TickInterval;
                    }

                    if (toTarget.magnitude <= EnemyContactRadius && enemy.ContactCooldown <= 0f)
                    {
                        target.Hp = Mathf.Max(0f, target.Hp - EnemyContactDamage);
                        OnPlayerHpChanged?.Invoke(target.SessionId, target.Hp);
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
                OnBattleEnded?.Invoke();
            }
        }

        /// <summary>
        /// 生成一发玩家子弹（主/副武器共用；纯确定性，无随机）。
        /// PrevPosition = Position：新实体不插值，避免从原点拖出重影。
        /// </summary>
        // 施法计划复用缓冲（避免每帧 GC；模拟层单线程使用）
        private static readonly CastPlan s_CastPlanPrimary = new CastPlan();
        private static readonly CastPlan s_CastPlanSecondary = new CastPlan();

        /// <summary>把一次施法计划落成子弹（同帧多发按扇形展开；确定性，无随机）。</summary>
        private void SpawnCastPlan(SimPlayer player, Vector2 aim, CastPlan plan)
        {
            if (plan == null || plan.Shots.Count == 0) { return; }
            for (int i = 0; i < plan.Shots.Count; i++)
            {
                var shot = plan.Shots[i];
                Vector2 dir = aim;
                if (shot.GroupCount > 1 && shot.SpreadDeg > 0.01f)
                {
                    float t = shot.GroupCount > 1 ? (shot.GroupIndex / (float)(shot.GroupCount - 1)) : 0.5f;
                    float angle = Mathf.Lerp(-shot.SpreadDeg * 0.5f, shot.SpreadDeg * 0.5f, t) * Mathf.Deg2Rad;
                    float cs = Mathf.Cos(angle);
                    float sn = Mathf.Sin(angle);
                    dir = new Vector2(aim.x * cs - aim.y * sn, aim.x * sn + aim.y * cs);
                }
                SpawnPlayerBullet(player, dir, shot.Speed, shot.Damage, shot.Lifetime, shot.Radius, shot.Tags);
            }
        }

        /// <summary>生成一发玩家子弹（主/副武器共用；纯确定性，无随机）。</summary>
        private void SpawnPlayerBullet(SimPlayer player, Vector2 aim, float speed, float damage,
            float lifetime = 0f, float radius = 0f, Data.SpellTag tags = Data.SpellTag.None)
        {
            float life = lifetime > 0f
                ? lifetime
                : (SpellConfig != null ? SpellConfig.DefaultBulletLifetime : 3f);
            float rad = radius > 0f
                ? radius
                : (SpellConfig != null ? SpellConfig.DefaultBulletRadius : 0.2f);

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
            DeterminismTracer.RecordInts(DeterminismTracer.Check.BulletSpawn,
                new[] { m_NextEntityId - 1, BitConverter.SingleToInt32Bits(aim.x), BitConverter.SingleToInt32Bits(aim.y) },
                FrameIndex);
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

            // 确定性打点：商店商品 hash（各端同种子应一致）
            int hash = 17;
            foreach (var item in m_ShopItems)
            {
                hash = hash * 31 + item.GetHashCode();
            }
            DeterminismTracer.RecordInt(DeterminismTracer.Check.ShopOffer, hash, FrameIndex);
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
            Vector2 center = anchor != null ? anchor.Position : Vector2.zero;
            Vector2 offset = m_Rng.InsideUnitCircle() * SpawnRadius;

            int newId = m_NextEntityId;
            m_Enemies.Add(new SimEnemy
            {
                EntityId = m_NextEntityId++,
                Position = center + offset,
                PrevPosition = center + offset,   // 新实体 Prev=Cur：避免插值从原点拖出（重影）
                Hp = EnemyBaseHp + WaveIndex * EnemyHpPerWave,
                Speed = EnemyBaseSpeed + WaveIndex * EnemySpeedPerWave,
            });

            // 确定性打点：敌人生成（位置位模式，跨端可比）
            DeterminismTracer.RecordInts(DeterminismTracer.Check.EnemySpawn,
                new[] { newId,
                    BitConverter.SingleToInt32Bits((center + offset).x),
                    BitConverter.SingleToInt32Bits((center + offset).y) },
                FrameIndex);
        }

        /// <summary>最近玩家（敌人 AI 目标）。</summary>
        private SimPlayer GetNearestPlayer(Vector2 position)
        {
            SimPlayer nearest = null;
            float minDistSqr = float.MaxValue;
            foreach (var player in m_Players)
            {
                if (!player.Alive)
                {
                    continue;
                }
                float distSqr = (player.Position - position).sqrMagnitude;
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

        /// <summary>按 sessionId 查玩家。</summary>
        public SimPlayer GetPlayer(int sessionId)
        {
            foreach (var player in m_Players)
            {
                if (player.SessionId == sessionId)
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

            var cfg = EmojiWar.GameMain.Simulation.SimConfigFactory.Build(player.SessionId, entityId, characterId);
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
        /// （Host 传真实 session，Client 一律 -1；不参与任何 Tick 决策），混入会恒误报不同步。
        /// 遍历顺序依赖既有有序容器（m_Players 按 EntityId 排序；敌人/子弹按确定性生成顺序），
        /// 容器顺序本身由确定性模拟保证跨端一致（文档 §9 有序容器原则）。
        /// 注意：float 用位模式（经 double 无损转换）参与哈希，避免"数值相等但位不同"的漏检。
        /// </summary>
        public long ComputeStateHash()
        {
            unchecked
            {
                long h = 1469598103934665603L;   // FNV-1a offset basis
                h = MixHash(h, FrameIndex);
                h = MixHash(h, WaveIndex);
                h = MixHash(h, ShopOpen ? 1 : 0);
                h = MixHash(h, BattleOver ? 1 : 0);
                h = MixHash(h, WaveStarted ? 1 : 0);
                h = MixHash(h, m_Players.Count);
                for (int i = 0; i < m_Players.Count; i++)
                {
                    var p = m_Players[i];
                    h = MixHash(h, p.EntityId);
                    h = MixHash(h, p.CharacterId);
                    h = MixHash(h, p.Alive ? 1 : 0);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.Position.x));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.Position.y));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.Hp));
                    // 双手施法状态：影响推进，必须入哈希
                    h = MixHash(h, p.PrimaryProgram.WandId);
                    h = MixHash(h, p.SecondaryProgram.WandId);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.PrimaryCast.Mana));
                    h = MixHash(h, p.PrimaryCast.Cursor);
                    h = MixHash(h, p.PrimaryCast.RechargeRemainingFrames);
                    h = MixHash(h, p.PrimaryCast.DelayRemainingFrames);
                    h = MixHash(h, p.PrimaryCast.CastActive ? 1 : 0);
                    h = MixHash(h, p.PrimaryCast.TotalTriggers);
                    h = MixHash(h, p.PrimaryCast.FrameIndex);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.PrimaryCast.PendingRechargeSeconds));
                    h = MixHash(h, p.PrimaryCast.RechargeLocked ? 1 : 0);
                    h = MixHash(h, p.PrimaryCast.ModScopeLeft);
                    h = MixHash(h, p.PrimaryCast.ModScopeBounded ? 1 : 0);
                    h = MixHash(h, p.PrimaryCast.ReverseConsumed ? 1 : 0);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.PrimaryCast.ActiveMod.ManaMul));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.PrimaryCast.ActiveMod.DelayMul));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.PrimaryCast.ActiveMod.DelayAdd));
                    h = MixHash(h, p.PrimaryCast.PendingCount);
                    for (int k = 0; k < p.PrimaryCast.PendingCount; k++)
                    {
                        h = MixHash(h, p.PrimaryCast.Pending[k].SlotIndex);
                        h = MixHash(h, p.PrimaryCast.Pending[k].DueFrame);
                        h = MixHash(h, p.PrimaryCast.Pending[k].Depth);
                    }
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.SecondaryCast.Mana));
                    h = MixHash(h, p.SecondaryCast.Cursor);
                    h = MixHash(h, p.SecondaryCast.RechargeRemainingFrames);
                    h = MixHash(h, p.SecondaryCast.DelayRemainingFrames);
                    h = MixHash(h, p.SecondaryCast.CastActive ? 1 : 0);
                    h = MixHash(h, p.SecondaryCast.TotalTriggers);
                    h = MixHash(h, p.SecondaryCast.FrameIndex);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.SecondaryCast.PendingRechargeSeconds));
                    h = MixHash(h, p.SecondaryCast.RechargeLocked ? 1 : 0);
                    h = MixHash(h, p.SecondaryCast.ModScopeLeft);
                    h = MixHash(h, p.SecondaryCast.ModScopeBounded ? 1 : 0);
                    h = MixHash(h, p.SecondaryCast.ReverseConsumed ? 1 : 0);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.SecondaryCast.ActiveMod.ManaMul));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.SecondaryCast.ActiveMod.DelayMul));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(p.SecondaryCast.ActiveMod.DelayAdd));
                    h = MixHash(h, p.SecondaryCast.PendingCount);
                    for (int k = 0; k < p.SecondaryCast.PendingCount; k++)
                    {
                        h = MixHash(h, p.SecondaryCast.Pending[k].SlotIndex);
                        h = MixHash(h, p.SecondaryCast.Pending[k].DueFrame);
                        h = MixHash(h, p.SecondaryCast.Pending[k].Depth);
                    }
                    // S3：临时 Buff 的运行状态（层数 + 剩余量）必须入哈希（执行文档 §4 与流程 I-4）。
                    h = MixBuffs(h, p.PrimaryCast);
                    h = MixBuffs(h, p.SecondaryCast);
                    // S4：被动剩余次数 / 冷却（执行文档 §4：被动剩余次数与冷却必须入哈希）
                    h = MixPassives(h, p.PrimaryCast);
                    h = MixPassives(h, p.SecondaryCast);
                    // 注：弹药已取消（无限释放），Ammo/IsReloading 恒为初始值，不参与状态哈希
                }
                h = MixHash(h, m_Enemies.Count);
                for (int i = 0; i < m_Enemies.Count; i++)
                {
                    var e = m_Enemies[i];
                    h = MixHash(h, e.EntityId);
                    h = MixHash(h, e.Alive ? 1 : 0);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(e.Position.x));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(e.Position.y));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(e.Hp));
                }
                h = MixHash(h, m_Bullets.Count);
                for (int i = 0; i < m_Bullets.Count; i++)
                {
                    var b = m_Bullets[i];
                    h = MixHash(h, b.EntityId);
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(b.Position.x));
                    h = MixHash(h, BitConverter.DoubleToInt64Bits(b.Position.y));
                }
                return h;
            }
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
            if (st.PassiveUsed == null) { return MixHash(h, 0); }
            h = MixHash(h, st.PassiveUsed.Length);
            for (int i = 0; i < st.PassiveUsed.Length; i++) { h = MixHash(h, st.PassiveUsed[i]); }
            if (st.PassiveCooldown != null)
            {
                for (int i = 0; i < st.PassiveCooldown.Length; i++) { h = MixHash(h, st.PassiveCooldown[i]); }
            }
            h = MixHash(h, st.PassiveFires);
            return h;
        }
    }
}
