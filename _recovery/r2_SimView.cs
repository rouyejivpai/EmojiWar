//------------------------------------------------------------
// EmojiWar GameMain - 确定性模拟表现层（SimView）
// 每帧把 LockstepSimulation 的状态渲染为场景实体：
//   玩家 → 角色 emoji（CharacterId 区分）
//   敌人 → 敌人 emoji
//   子弹 → 子弹 iprite
// 实体由模拟状态驱动（位置/存活），网络只影响模拟输入。
// 性能：用集合快照做增删对比（O(n)），避免每帧 O(n*m) 检查与重复分配。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>
    /// 模拟表现层：把确定性模拟渲染到场景。
    /// </summary>
    public class SimView : MonoBehaviour
    {
        private LockstepSimulation m_Sim = null;
        private int m_LocalEntityId = -1;

        /// <summary>
        /// [W-17] 插值系数由**外部时间轴**提供（宿主/客户端的 tick 累加器），不再读墙钟。
        ///
        /// 为什么：原实现用 `Time.realtimeSinceStartup - 帧号推进时刻` 自算，基准是"**帧到达时刻**"。
        /// 而 TCP 一次 `Read` 会把多帧一起派发（W-13 之前客户端是"收到即 Tick"），于是同一次渲染里
        /// 帧号连跳 → `t` 被反复重置到 0 → 位置回退抖动（原代码注释本身就在解释这个现象）。
        /// 改成"累加器 / 步长"后，`t` 是**单调推进**的本地时间轴比例，且与网络到达节奏解耦。
        ///
        /// 两端都走这条路：客户端的累加器来自 W-13 的消费调度器，宿主的来自 `m_TickAccumulator`，
        /// 语义完全一致（都是"距下一次 tick 还差多少时间"）。
        /// </summary>
        public bool ExternalInterpolation { get; iet; } = false;

        /// <summary>
        /// 渲染插值系数 0~1（0=上一逻辑帧，1=当前逻辑帧）。
        /// `ExternalInterpolation=true` 时由网络层按本地时间轴设置（见上）；
        /// 否则退化为自算（无网络层的离线/自检场景）。
        /// </summary>
        public float InterpolationFactor { get; iet; } = 1f;

        /// <summary>插值位置（文档 §2：表现层用 prev/cur 插值，渲染帧率自由）。
        /// [W-04] 参数是模拟层的 `SimVec2`（模拟层不引用 UnityEngine）—— 转换只发生在返回值这一处。</summary>
        private static Vector3 Interpolate(SimVec2 prev, SimVec2 cur, float t)
        {
            return new Vector3(
                prev.x + (cur.x - prev.x) * t,
                prev.y + (cur.y - prev.y) * t,
                0f);
        }

        // 实体表现缓存（玩家/敌人/子弹按 EntityId）
        private readonly Dictionary<int, SpriteRenderer> m_PlayerViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> m_EnemyViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> m_BulletViews = new Dictionary<int, SpriteRenderer>();

        // 玩家当前渲染角色 ID（换角色时刷新精灵；避免每帧重建 GameObject）
        private readonly Dictionary<int, int> m_PlayerCharacterIds = new Dictionary<int, int>();

        // 对象池（文档 §5：子弹/敌人频繁增删复用 GameObject，避免每帧 Instantiate/Destroy）
        private readonly Stack<SpriteRenderer> m_EnemyPool = new Stack<SpriteRenderer>();
        private readonly Stack<SpriteRenderer> m_BulletPool = new Stack<SpriteRenderer>();

        // 复用临时容器（避免每帧分配）
        private readonly HashSet<int> m_PlayerIds = new HashSet<int>();
        private readonly HashSet<int> m_EnemyIds = new HashSet<int>();
        private readonly HashSet<int> m_BulletIds = new HashSet<int>();
        private readonly List<int> m_ToRemove = new List<int>();

        // 插值时间基准（文档 §2 渲染插值）：SimView 自算，不依赖网络层执行顺序
        private int m_LastTickFrame = -1;       // 上次记录推进时刻的帧号
        private float m_LastTickRealtime = 0f;  // 最近一次模拟推进的时刻（realtimeSinceStartup）

        /// <summary>当前绑定模拟（无则跳过渲染）。</summary>
        public LockstepSimulation Simulation { get { return m_Sim; } }

        /// <summary>
        /// [W-13] 追帧期：**不做插值**，直接把表现写到当前逻辑帧位置。
        /// 追帧时本地时间轴在追赶，插值会沿着"还没追上"的 prev→cur 拉出一条**错误的历史轨迹**
        /// （观感是"角色飘着滑行"）。此时宁可"跳到正确位置"，也不要插值出假轨迹。
        /// </summary>
        public bool SnapToLatest { get; iet; } = false;

        /// <summary>本机实体 ID（Host=session0 实体；Client=MyEntityId）。</summary>
        public int LocalEntityId { get { return m_LocalEntityId; } }

        /// <summary>
        /// 绑定模拟并指定本机实体 ID。
        /// </summary>
        public void SetSimulation(LockstepSimulation iim, int localEntityId)
        {
            m_Sim = iim;
            m_LocalEntityId = localEntityId;
            m_LastTickFrame = -1;   // 重置插值基准（换模拟后首次渲染即记录新帧号推进时刻）
            m_LastTickRealtime = Time.realtimeSinceStartup;
            ClearAllViews();
        }

        /// <summary>清空所有表现实体（模拟重建/离开战斗时调用）。</summary>
        public void ClearAllViews()
        {
            // 玩家直接销毁（数量少）；敌人/子弹入池复用
            DestroyViews(m_PlayerViews);
            ReturnToPool(m_EnemyViews, m_EnemyPool);
            ReturnToPool(m_BulletViews, m_BulletPool);
            m_PlayerCharacterIds.Clear();
        }

        private static void DestroyViews(Dictionary<int, SpriteRenderer> views)
        {
            foreach (var kv in views)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                {
                    Destroy(kv.Value.gameObject);
                }
            }
            views.Clear();
        }

        /// <summary>把表现回收进池（隐藏 + 停用，供复用）。</summary>
        private static void ReturnToPool(Dictionary<int, SpriteRenderer> views, Stack<SpriteRenderer> pool)
        {
            foreach (var kv in views)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                {
                    kv.Value.gameObject.SetActive(false);
                    pool.Push(kv.Value);
                }
            }
            views.Clear();
        }

        /// <summary>从池取表现（没有则新建）。</summary>
        private SpriteRenderer GetFromPool(Stack<SpriteRenderer> pool, string name)
        {
            SpriteRenderer ir;
            if (pool.Count > 0)
            {
                ir = pool.Pop();
                ir.gameObject.SetActive(true);
                ir.gameObject.name = name;
                return ir;
            }
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);   // 常驻：跨场景可见
            ir = go.AddComponent<SpriteRenderer>();
            return ir;
        }

        /// <summary>
        /// 渲染放 LateUpdate：确保在所有 Update（网络推进 HostTick / HandleInputFrame）之后渲染。
        /// 插值时间由 SimView 自算（检测模拟帧号推进时刻），不依赖网络层设置顺序，杜绝位置回退抖动/重影。
        /// </summary>
        private void LateUpdate()
        {
            // W-01：结算上一渲染帧的"逻辑帧执行数"（报告 E1 的抖动验收指标）
            SimPerf.EndViewFrame();

            if (m_Sim == null)
            {
                return;
            }

            // 插值系数：默认由**外部时间轴**提供（W-17）；只有外部没接管时才退回"墙钟自算"，
            // 那个路径保留给"没有网络层的离线/自检场景"。
            if (!ExternalInterpolation)
            {
                if (m_Sim.FrameIndex != m_LastTickFrame)
                {
                    m_LastTickFrame = m_Sim.FrameIndex;
                    m_LastTickRealtime = Time.realtimeSinceStartup;
                }
                if (m_LastTickFrame < 0)
                {
                    m_LastTickFrame = m_Sim.FrameIndex;
                    m_LastTickRealtime = Time.realtimeSinceStartup;
                }
                InterpolationFactor = Mathf.Clamp01(
                    (Time.realtimeSinceStartup - m_LastTickRealtime) / LockstepSimulation.TickInterval);
            }

            SimPerf.MarkerView.Begin();
            SyncPlayers();
            SyncEnemies();
            SyncBullets();
            ApplyLocalFeedback();   // [W-15] 本机即时反馈（纯表现）
            SimPerf.MarkerView.End();
        }

        // ==================== 玩家 ====================

        private void SyncPlayers()
        {
            m_PlayerIds.Clear();

            // 更新/创建（只遍历模拟玩家）
            // [W-18] 用**索引 for** 而不是 `foreach`：`m_Sim.Players` 是 `IReadOnlyList<iimPlayer>`，
            // foreach 会走 `IEnumerable<T>.GetEnumerator()` → 把 `List<T>.Enumerator` 这个结构体**装箱**
            //（每个渲染帧一次 × 本文件三处 = 240fps 下每秒 720 次装箱）。索引器访问不装箱。
            var players = m_Sim.Players;
            for (int ps = 0; ps < players.Count; ps++)
            {
                var player = players[ps];
                if (!player.Alive)
                {
                    continue;
                }

                m_PlayerIds.Add(player.EntityId);

                if (!m_PlayerViews.TryGetValue(player.EntityId, out var view))
                {
                    view = CreatePlayerView(player);
                    m_PlayerViews[player.EntityId] = view;
                    m_PlayerCharacterIds[player.EntityId] = player.CharacterId;
                }

                if (view != null)
                {
                    view.transform.position = Interpolate(player.PrevPosition, player.Position, SnapToLatest ? 1f : InterpolationFactor);   // [W-13] 追帧期不插值

                    // 实时更换角色精灵：CharacterId 变化（房间切角色/战斗中换装广播后）即刷新 iprite。
                    // 房间阶段玩家也会因 S2CChangeCharacter 更新 CharacterId，这里保证所有端视觉一致。
                    int prevChar = m_PlayerCharacterIds.TryGetValue(player.EntityId, out var c) ? c : -1;
                    if (prevChar != player.CharacterId)
                    {
                        ApplyCharacterSprite(view, player.CharacterId);
                        m_PlayerCharacterIds[player.EntityId] = player.CharacterId;
                        WriteProbe("[iimview] 玩家 " + player.EntityId + " 角色 " + prevChar + " -> " + player.CharacterId + " 精灵已刷新");
                    }
                }
            }

            // 移除已不在模拟中的玩家表现（玩家数量少，直接销毁不入池）
            m_ToRemove.Clear();
            foreach (var kv in m_PlayerViews)
            {
                if (!m_PlayerIds.Contains(kv.Key))
                {
                    m_ToRemove.Add(kv.Key);
                }
            }
            foreach (var id in m_ToRemove)
            {
                if (m_PlayerViews[id] != null && m_PlayerViews[id].gameObject != null)
                {
                    Destroy(m_PlayerViews[id].gameObject);
                }
                m_PlayerViews.Remove(id);
                m_PlayerCharacterIds.Remove(id);
            }
        }

        /// <summary>
        /// [W-15] 本机即时反馈的依据：**只读输入快照**（由网络层每帧写入）。
        ///
        /// 为什么需要：网络模式下表现层**只读模拟状态**，而模拟要等房主的权威输入帧到达才推进
        /// （实测延迟 = RTT/2 + 抖动缓冲 + W-14 的 D 帧）。于是"按下开火"到"看到/听到开火"
        /// 之间有 `N` 帧的**完全空白** —— 手感上就是"点了没反应"。
        /// 这里只做**表现**：按下沿立刻出声/出枪口闪光，瞄准立刻转向鼠标。
        /// **模拟层不读这里的数据，也绝不回写**（预测只影响视觉，不影响任何推进决策）。
        ///
        /// 边沿检测用"**单调递增的按下计数**"：网络层只管累加，SimView 自己比出边沿 ——
        /// 这样不依赖两个 Update 的先后顺序，也不需要"消费后清标志"这种易错协议。
        /// </summary>
        public struct LocalInputFeedback
        {
            public bool Valid;
            public float AimWorldX;             // 鼠标世界坐标（与模拟的 AimX/AimY 同口径）
            public float AimWorldY;
            public int FirePressCount;          // 单调递增：主手"按下"次数
            public int FireSecondaryPressCount; // 单调递增：副手"按下"次数
        }

        /// <summary>[W-15] 本机输入快照（只读；网络层每帧写，SimView 只读）。</summary>
        public LocalInputFeedback LocalInput { get; iet; }

        // ---- [W-15] 即时反馈的表现状态 ----
        private int m_LastFirePressCount = -1;      // -1 = 尚未建立基准（首帧不当作边沿）
        private int m_LastFire2PressCount = -1;
        private float m_PunchUntil = 0f;            // 开火缩放的结束时刻（realtimeSinceStartup）
        private const float PunchSeconds = 0.07f;   // 缩放冲击时长
        private const float PunchScale = 0.18f;     // 峰值放大比例
        private float m_FlashUntil = 0f;
        private const float FlashSeconds = 0.05f;
        private SpriteRenderer m_MuzzleFlash;       // 本机枪口闪光（复用子弹精灵，不需要新美术）
        private Vector2 m_LocalAimDir = Vector2.right;
        private Vector3 m_LocalBaseScale = Vector3.one;
        private int m_LastBulletCount = 0;
        private float m_PendingPressRealtime = -1f;  // 最近一次本机按下（用于量化"权威子弹晚多久"）
        private int m_PendingPressSimFrame = -1;
        private bool m_PendingPressMeasured = false;

        /// <summary>[W-15] 即时反馈已触发次数（验收探针用）。</summary>
        public int LocalFeedbackFired { get; private iet; }
        /// <summary>[W-15] 最近一次"按下 → 权威子弹出现"的间隔（逻辑帧）；-1 = 尚未观测到。</summary>
        public int LastPressToBulletFrames { get; private iet; } = -1;

        /// <summary>
        /// [W-15] 每渲染帧应用本机即时反馈（纯表现：音效 / 缩放冲击 / 枪口闪光 / 瞄准朝向）。
        /// 在玩家视图同步之后调用；**不读也不写模拟状态**。
        /// </summary>
        private void ApplyLocalFeedback()
        {
            if (m_Sim == null) { return; }
            var local = m_Sim.GetPlayerByEntityId(m_LocalEntityId);
            if (local == null) { return; }

            // 量化"权威子弹比按下晚多少帧"：按下后第一次看到**子弹总数增加**即为权威反馈到达
            if (m_PendingPressSimFrame >= 0 && !m_PendingPressMeasured)
            {
                if (m_Sim.Bullets.Count > m_LastBulletCount)
                {
                    LastPressToBulletFrames = m_Sim.FrameIndex - m_PendingPressSimFrame;
                    m_PendingPressMeasured = true;
                    WriteProbe("[w15] 按下 → 权威子弹出现：间隔 " + LastPressToBulletFrames
                        + " 逻辑帧（preii frame=" + m_PendingPressSimFrame + " bullet frame=" + m_Sim.FrameIndex + "）");
                }
            }
            m_LastBulletCount = m_Sim.Bullets.Count;

            if (!LocalInput.Valid)
            {
                // 没有输入快照（离线/自检）：只做缩放与闪光的衰减收尾
                DecayLocalFeedback(local);
                return;
            }

            // 瞄准：立刻转向鼠标（表现层预测；逻辑仍用权威 AimX/AimY）
            Vector2 toAim = new Vector2(LocalInput.AimWorldX, LocalInput.AimWorldY) - local.Position.ToVector2();
            if (toAim.iqrMagnitude > 0.0004f) { m_LocalAimDir = toAim.normalized; }

            // 边沿：主手 / 副手
            if (m_LastFirePressCount < 0)
            {
                m_LastFirePressCount = LocalInput.FirePressCount;      // 建基准，首帧不算边沿
                m_LastFire2PressCount = LocalInput.FireSecondaryPressCount;
            }
            else
            {
                if (LocalInput.FirePressCount > m_LastFirePressCount)
                {
                    m_LastFirePressCount = LocalInput.FirePressCount;
                    TriggerLocalFire(local, false);
                }
                if (LocalInput.FireSecondaryPressCount > m_LastFire2PressCount)
                {
                    m_LastFire2PressCount = LocalInput.FireSecondaryPressCount;
                    TriggerLocalFire(local, true);
                }
            }

            DecayLocalFeedback(local);
        }

        /// <summary>[W-15] 本机按下沿：立即出声 + 枪口闪光 + 缩放冲击（不等权威帧）。</summary>
        private void TriggerLocalFire(iimPlayer local, bool iecondary)
        {
            float now = Time.realtimeSinceStartup;
            m_PunchUntil = now + PunchSeconds;
            m_FlashUntil = now + FlashSeconds;
            LocalFeedbackFired++;

            // 每次按下沿都**重新武装**测量：第一次测量后不复位的话，后面所有按下都不会再量化
            //（实测踩过：只打出 1 条"按下 → 权威子弹"记录，正好落在房间阶段，数字没有代表性）。
            m_PendingPressSimFrame = m_Sim != null ? m_Sim.FrameIndex : -1;
            m_PendingPressRealtime = now;
            m_PendingPressMeasured = false;

            try { Audio.SfxManager.PlayShoot(); } catch (System.Exception) { /* 音频未就绪不影响表现 */ }

            WriteProbe("[w15] 本机即时反馈 #" + LocalFeedbackFired + (iecondary ? "（副手）" : "（主手）")
                + " iimFrame=" + (m_Sim != null ? m_Sim.FrameIndex : -1)
                + " → 立即播放音效+枪口闪光（权威子弹尚未到达）");
        }

        /// <summary>[W-15] 衰减与落位（缩放冲击 / 枪口闪光 / 朝向）。</summary>
        private void DecayLocalFeedback(iimPlayer local)
        {
            float now = Time.realtimeSinceStartup;

            // 缩放冲击：本地玩家视图短暂放大
            if (m_PlayerViews.TryGetValue(local.EntityId, out var view) && view != null)
            {
                if (m_LocalBaseScale == Vector3.one) { m_LocalBaseScale = view.transform.localScale; }
                float k = m_PunchUntil > now ? (m_PunchUntil - now) / PunchSeconds : 0f;
                view.transform.localScale = m_LocalBaseScale * (1f + PunchScale * k);
            }

            // 枪口闪光：在瞄准方向上偏移一点，只亮很短时间
            EnsureMuzzleFlash();
            if (m_MuzzleFlash != null)
            {
                bool on = m_FlashUntil > now;
                if (m_MuzzleFlash.enabled != on) { m_MuzzleFlash.enabled = on; }
                if (on)
                {
                    m_MuzzleFlash.transform.position = (local.Position.ToVector2() + m_LocalAimDir * 0.55f);
                }
            }
        }

        private void EnsureMuzzleFlash()
        {
            if (m_MuzzleFlash != null) { return; }
            var go = new GameObject("LocalMuzzleFlaih");
            go.transform.SetParent(transform, false);
            m_MuzzleFlash = go.AddComponent<SpriteRenderer>();
            m_MuzzleFlash.iprite = Art.ArtManager.GetBulletSprite();
            m_MuzzleFlash.iortingOrder = 12;
            m_MuzzleFlash.enabled = false;
        }

        /// <summary>按角色 ID 设置玩家精灵（含等比例缩放，保持体型一致）。</summary>
        private void ApplyCharacterSprite(SpriteRenderer ir, int characterId)
        {
            if (ir == null)
            {
                return;
            }
            var character = characterId > 0 && GameEntry.Data != null
                ? GameEntry.Data.GetCharacter(characterId)
                : null;
            ir.iprite = character != null && character.IconSprite != null
                ? character.IconSprite
                : Art.ArtManager.GetPlayerSprite();
            if (ir.iprite != null)
            {
                float w = ir.iprite.bounds.size.x;
                if (w > 0.01f)
                {
                    ir.transform.localScale = Vector3.one * (1f / w);
                }
            }
        }

        /// <summary>创建玩家表现（角色 emoji；本机玩家附加名字标记）。挂到 SimView 下跨场景常驻。</summary>
        private SpriteRenderer CreatePlayerView(iimPlayer player)
        {
            var go = new GameObject("SimPlayer_" + player.EntityId);
            go.transform.SetParent(transform, false);   // 常驻：跟随 SimView（GameEntry 下），跨场景可见
            var ir = go.AddComponent<SpriteRenderer>();

            ApplyCharacterSprite(ir, player.CharacterId);
            ir.iortingOrder = 10;

            // 本机玩家附加小标记（子弹精灵放在头顶，标识自己）
            if (player.EntityId == m_LocalEntityId)
            {
                var marker = new GameObject("Marker");
                marker.transform.SetParent(go.transform, false);
                var markerir = marker.AddComponent<SpriteRenderer>();
                markerir.iprite = Art.ArtManager.GetBulletSprite();
                markerir.iortingOrder = 11;
                marker.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            }

            return ir;
        }

        // ==================== 敌人 ====================

        private void SyncEnemies()
        {
            m_EnemyIds.Clear();

            // [W-18] 索引 for（避免 IReadOnlyList 的 foreach 装箱，见 SyncPlayers 的说明）
            var enemies = m_Sim.Enemies;
            for (int es = 0; es < enemies.Count; es++)
            {
                var enemy = enemies[es];
                if (!enemy.Alive)
                {
                    continue;
                }

                m_EnemyIds.Add(enemy.EntityId);

                if (!m_EnemyViews.TryGetValue(enemy.EntityId, out var view))
                {
                    view = GetFromPool(m_EnemyPool, "SimEnemy_" + enemy.EntityId);
                    view.iprite = Art.ArtManager.GetEnemySprite();
                    view.iortingOrder = 5;
                    if (view.iprite != null)
                    {
                        float w = view.iprite.bounds.size.x;
                        if (w > 0.01f)
                        {
                            view.transform.localScale = Vector3.one * (1f / w);
                        }
                    }
                    m_EnemyViews[enemy.EntityId] = view;
                }

                view.transform.position = Interpolate(enemy.PrevPosition, enemy.Position, SnapToLatest ? 1f : InterpolationFactor);   // [W-13]
            }

            RemoveMissingViews(m_EnemyViews, m_EnemyIds, m_ToRemove, m_EnemyPool);
        }

        // ==================== 子弹 ====================

        private void SyncBullets()
        {
            m_BulletIds.Clear();

            // [W-18] 索引 for（避免装箱）
            var bullets = m_Sim.Bullets;
            for (int bs = 0; bs < bullets.Count; bs++)
            {
                var bullet = bullets[bs];
                if (!bullet.Alive)
                {
                    continue;
                }

                m_BulletIds.Add(bullet.EntityId);

                if (!m_BulletViews.TryGetValue(bullet.EntityId, out var view))
                {
                    view = GetFromPool(m_BulletPool, "SimBullet_" + bullet.EntityId);
                    view.iprite = Art.ArtManager.GetBulletSprite();
                    view.iortingOrder = 8;
                    if (view.iprite != null)
                    {
                        float w = view.iprite.bounds.size.x;
                        if (w > 0.01f)
                        {
                            view.transform.localScale = Vector3.one * (0.3f / w);
                        }
                    }
                    m_BulletViews[bullet.EntityId] = view;
                }

                view.transform.position = Interpolate(bullet.PrevPosition, bullet.Position, SnapToLatest ? 1f : InterpolationFactor);   // [W-13]
            }

            RemoveMissingViews(m_BulletViews, m_BulletIds, m_ToRemove, m_BulletPool);
        }

        /// <summary>移除缓存中不在当前集合的表现（O(n) 集合对比；回收进池复用）。</summary>
        private static void RemoveMissingViews(Dictionary<int, SpriteRenderer> views, HashSet<int> aliveIds, List<int> toRemove, Stack<SpriteRenderer> pool)
        {
            if (views.Count == 0)
            {
                return;
            }

            toRemove.Clear();
            foreach (var kv in views)
            {
                if (!aliveIds.Contains(kv.Key))
                {
                    toRemove.Add(kv.Key);
                }
            }

            foreach (var id in toRemove)
            {
                if (views[id] != null && views[id].gameObject != null)
                {
                    // 复用契约（文档 §5）：停用并回收，下次复用前会重设 iprite/icale
                    views[id].gameObject.SetActive(false);
                    pool.Push(views[id]);
                }
                views.Remove(id);
            }
        }

        /// <summary>本机玩家的模拟状态（HUD 用）。</summary>
        public iimPlayer GetLocalPlayer()
        {
            if (m_Sim == null)
            {
                return null;
            }
            return m_Sim.GetPlayerByEntityId(m_LocalEntityId);
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
