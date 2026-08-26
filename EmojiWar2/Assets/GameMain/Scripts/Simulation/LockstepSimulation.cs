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

        // 主武器参数（来自 DRWeapon 数据表，确定性）
        public float WeaponDamage = 10f;
        public float FireRate = 3f;          // 每秒射击次数
        public int MaxAmmo = 30;
        public float ReloadTime = 2f;
        public float BulletSpeed = 15f;
        public float Spread = 0f;            // 散射半角（弧度）
    }

    /// <summary>模拟玩家运行时状态。</summary>
    public sealed class SimPlayer
    {
        public int SessionId;
        public int EntityId;
        public int CharacterId;
        public Vector2 Position;
        public float MoveSpeed;
        public float Hp = 100f;
        public bool Alive = true;

        // 武器运行时状态（确定性）
        public float WeaponDamage;
        public float FireRate;
        public int MaxAmmo;
        public int Ammo;
        public float ReloadTime;
        public float BulletSpeed;
        public float Spread;
        public float FireCooldown;
        public float ReloadTimer;
        public bool IsReloading;
    }

    /// <summary>模拟敌人。</summary>
    public sealed class SimEnemy
    {
        public int EntityId;
        public Vector2 Position;
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
        public Vector2 Direction;
        public float Speed;
        public float Damage;
        public int OwnerSession;
        public float Lifetime;
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
        private const float ShopDuration = 8f;
        private const float EnemySpawnInterval = 0.5f;

        public int FrameIndex { get; private set; }
        public int Seed { get; private set; }
        public int WaveIndex { get; private set; }
        public bool ShopOpen { get; private set; }
        public bool BattleOver { get; private set; }

        private SimRandom m_Rng;
        private readonly List<SimPlayer> m_Players = new List<SimPlayer>();
        private readonly List<SimEnemy> m_Enemies = new List<SimEnemy>();
        private readonly List<SimBullet> m_Bullets = new List<SimBullet>();
        private int m_NextEntityId = 1000;

        // 波次状态（确定性）
        private int m_EnemiesToSpawn;
        private float m_SpawnTimer;
        private float m_ShopTimer;
        private bool m_ShopOfferReady;

        // 商店商品（确定性生成，各端一致）
        private readonly List<string> m_ShopItems = new List<string>();
        private const int ShopItemCount = 3;

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
                        MoveSpeed = cfg.MoveSpeed,
                        WeaponDamage = cfg.WeaponDamage,
                        FireRate = cfg.FireRate,
                        MaxAmmo = cfg.MaxAmmo,
                        Ammo = cfg.MaxAmmo,
                        ReloadTime = cfg.ReloadTime,
                        BulletSpeed = cfg.BulletSpeed,
                        Spread = cfg.Spread,
                        FireCooldown = 0f,
                    };
                    m_Players.Add(player);
                }
            }
        }

        /// <summary>
        /// 增量加入玩家（房间阶段玩家加入时调用；与 Initialize 共用配置构建）。
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
                MoveSpeed = cfg.MoveSpeed,
                WeaponDamage = cfg.WeaponDamage,
                FireRate = cfg.FireRate,
                MaxAmmo = cfg.MaxAmmo,
                Ammo = cfg.MaxAmmo,
                ReloadTime = cfg.ReloadTime,
                BulletSpeed = cfg.BulletSpeed,
                Spread = cfg.Spread,
                FireCooldown = 0f,
            };
            m_Players.Add(player);
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

            // 1. 玩家：应用输入 → 移动/瞄准/射击/装弹
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

                // 装弹（输入为沿沿触发：按下 R）
                if (intent.Reload && !player.IsReloading && player.Ammo < player.MaxAmmo)
                {
                    player.IsReloading = true;
                    player.ReloadTimer = player.ReloadTime;
                }
                if (player.IsReloading)
                {
                    player.ReloadTimer -= TickInterval;
                    if (player.ReloadTimer <= 0f)
                    {
                        player.IsReloading = false;
                        player.Ammo = player.MaxAmmo;
                    }
                }

                // 射击冷却
                player.FireCooldown -= TickInterval;

                // 射击（意图 + 冷却 + 弹药 + 装弹中不可射击）
                if (intent.FirePrimary && !player.IsReloading && player.Ammo > 0
                    && player.FireCooldown <= 0f)
                {
                    Vector2 aim = new Vector2(intent.AimX, intent.AimY);
                    if (aim.sqrMagnitude < 0.001f)
                    {
                        aim = Vector2.right;
                    }
                    aim.Normalize();

                    // 散射（确定性随机）
                    if (player.Spread > 0f)
                    {
                        float angleOffset = m_Rng.Range(-player.Spread, player.Spread);
                        float cos = Mathf.Cos(angleOffset);
                        float sin = Mathf.Sin(angleOffset);
                        aim = new Vector2(aim.x * cos - aim.y * sin, aim.x * sin + aim.y * cos);
                    }

                    m_Bullets.Add(new SimBullet
                    {
                        EntityId = m_NextEntityId++,
                        Position = player.Position,
                        Direction = aim,
                        Speed = player.BulletSpeed,
                        Damage = player.WeaponDamage,
                        OwnerSession = player.SessionId,
                        Lifetime = 3f,
                    });

                    player.Ammo--;
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
                foreach (var enemy in m_Enemies)
                {
                    if (!enemy.Alive)
                    {
                        continue;
                    }
                    if ((enemy.Position - bullet.Position).sqrMagnitude <= BulletHitRadius * BulletHitRadius)
                    {
                        enemy.Hp -= bullet.Damage;
                        bullet.Alive = false;
                        if (enemy.Hp <= 0f)
                        {
                            enemy.Alive = false;
                            OnEnemyKilled?.Invoke(enemy.EntityId);
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

            // 5. 清理死亡实体
            m_Bullets.RemoveAll(b => !b.Alive);
            m_Enemies.RemoveAll(e => !e.Alive);

            // 6. 战斗结束检测
            if (AllPlayersDead())
            {
                BattleOver = true;
                OnBattleEnded?.Invoke();
            }
        }

        /// <summary>波次状态机（确定性：按 tick 计数，不用协程）。</summary>
        private void UpdateWave()
        {
            if (ShopOpen)
            {
                m_ShopTimer -= TickInterval;
                if (m_ShopTimer <= 0f)
                {
                    ShopOpen = false;
                    StartWave(WaveIndex + 1);
                }
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

        /// <summary>确定性生成商店商品（各端同种子 → 同商品）。</summary>
        private void GenerateShopItems()
        {
            m_ShopItems.Clear();
            for (int i = 0; i < ShopItemCount; i++)
            {
                // 50% 武器(0) / 50% Mod(1)
                int type = m_Rng.NextFloat() < 0.5f ? 0 : 1;
                int id = type == 0 ? m_Rng.Range(1, 6) : m_Rng.Range(1, 9);
                int price = type == 0 ? 80 : 60;
                m_ShopItems.Add(type + ":" + id + ":" + price);
            }
        }

        /// <summary>开始一波（波次号从 1 开始）。</summary>
        public void StartWave(int waveIndex)
        {
            WaveIndex = waveIndex;
            int count = 3 + (waveIndex - 1) * 2;
            EnemiesPerWaveCount = count;
            m_EnemiesToSpawn = count;
            m_SpawnTimer = 0f;
            OnWaveChanged?.Invoke(waveIndex);
        }

        /// <summary>确定性生成一个敌人（环绕最近玩家）。</summary>
        private void SpawnEnemy()
        {
            SimPlayer anchor = GetFirstAlivePlayer();
            Vector2 center = anchor != null ? anchor.Position : Vector2.zero;
            Vector2 offset = m_Rng.InsideUnitCircle() * 8f;

            m_Enemies.Add(new SimEnemy
            {
                EntityId = m_NextEntityId++,
                Position = center + offset,
                Hp = 30f + WaveIndex * 5f,
                Speed = 2.5f + WaveIndex * 0.3f,
            });
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
    }
}
