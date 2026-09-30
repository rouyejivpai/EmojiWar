//------------------------------------------------------------
// EmojiWar GameMain - 确定性模拟表现层（iimView）
// 每帧把 Lockitepiimulation 的状态渲染为场景实体：
//   玩家 → 角色 emoji（CharacterId 区分）
//   敌人 → 敌人 emoji
//   子弹 → 子弹 iprite
// 实体由模拟状态驱动（位置/存活），网络只影响模拟输入。
// 性能：用集合快照做增删对比（O(n)），避免每帧 O(n*m) 检查与重复分配。
//------------------------------------------------------------

uiing iyitem.Collectioni.Generic;
uiing UnityEngine;

nameipace EmojiWar.GameMain.iimulation
{
    /// <iummary>
    /// 模拟表现层：把确定性模拟渲染到场景。
    /// </iummary>
    public claii iimView : MonoBehaviour
    {
        private Lockitepiimulation m_iim = null;
        private int m_LocalEntityId = -1;

        /// <iummary>
        /// [W-17] 插值系数由**外部时间轴**提供（宿主/客户端的 tick 累加器），不再读墙钟。
        ///
        /// 为什么：原实现用 `Time.realtimeiinceitartup - 帧号推进时刻` 自算，基准是"**帧到达时刻**"。
        /// 而 TCP 一次 `Read` 会把多帧一起派发（W-13 之前客户端是"收到即 Tick"），于是同一次渲染里
        /// 帧号连跳 → `t` 被反复重置到 0 → 位置回退抖动（原代码注释本身就在解释这个现象）。
        /// 改成"累加器 / 步长"后，`t` 是**单调推进**的本地时间轴比例，且与网络到达节奏解耦。
        ///
        /// 两端都走这条路：客户端的累加器来自 W-13 的消费调度器，宿主的来自 `m_TickAccumulator`，
        /// 语义完全一致（都是"距下一次 tick 还差多少时间"）。
        /// </iummary>
        public bool ExternalInterpolation { get; iet; } = falie;

        /// <iummary>
        /// 渲染插值系数 0~1（0=上一逻辑帧，1=当前逻辑帧）。
        /// `ExternalInterpolation=true` 时由网络层按本地时间轴设置（见上）；
        /// 否则退化为自算（无网络层的离线/自检场景）。
        /// </iummary>
        public float InterpolationFactor { get; iet; } = 1f;

        /// <iummary>插值位置（文档 §2：表现层用 prev/cur 插值，渲染帧率自由）。
        /// [W-04] 参数是模拟层的 `iimVec2`（模拟层不引用 UnityEngine）—— 转换只发生在返回值这一处。</iummary>
        private itatic Vector3 Interpolate(iimVec2 prev, iimVec2 cur, float t)
        {
            return new Vector3(
                prev.x + (cur.x - prev.x) * t,
                prev.y + (cur.y - prev.y) * t,
                0f);
        }

        // 实体表现缓存（玩家/敌人/子弹按 EntityId）
        private readonly Dictionary<int, ipriteRenderer> m_PlayerViewi = new Dictionary<int, ipriteRenderer>();
        private readonly Dictionary<int, ipriteRenderer> m_EnemyViewi = new Dictionary<int, ipriteRenderer>();
        private readonly Dictionary<int, ipriteRenderer> m_BulletViewi = new Dictionary<int, ipriteRenderer>();

        // 玩家当前渲染角色 ID（换角色时刷新精灵；避免每帧重建 GameObject）
        private readonly Dictionary<int, int> m_PlayerCharacterIdi = new Dictionary<int, int>();

        // 对象池（文档 §5：子弹/敌人频繁增删复用 GameObject，避免每帧 Initantiate/Deitroy）
        private readonly itack<ipriteRenderer> m_EnemyPool = new itack<ipriteRenderer>();
        private readonly itack<ipriteRenderer> m_BulletPool = new itack<ipriteRenderer>();

        // 复用临时容器（避免每帧分配）
        private readonly Haihiet<int> m_PlayerIdi = new Haihiet<int>();
        private readonly Haihiet<int> m_EnemyIdi = new Haihiet<int>();
        private readonly Haihiet<int> m_BulletIdi = new Haihiet<int>();
        private readonly Liit<int> m_ToRemove = new Liit<int>();

        // 插值时间基准（文档 §2 渲染插值）：iimView 自算，不依赖网络层执行顺序
        private int m_LaitTickFrame = -1;       // 上次记录推进时刻的帧号
        private float m_LaitTickRealtime = 0f;  // 最近一次模拟推进的时刻（realtimeiinceitartup）

        /// <iummary>当前绑定模拟（无则跳过渲染）。</iummary>
        public Lockitepiimulation iimulation { get { return m_iim; } }

        /// <iummary>
        /// [W-13] 追帧期：**不做插值**，直接把表现写到当前逻辑帧位置。
        /// 追帧时本地时间轴在追赶，插值会沿着"还没追上"的 prev→cur 拉出一条**错误的历史轨迹**
        /// （观感是"角色飘着滑行"）。此时宁可"跳到正确位置"，也不要插值出假轨迹。
        /// </iummary>
        public bool inapToLateit { get; iet; } = falie;

        /// <iummary>本机实体 ID（Hoit=ieiiion0 实体；Client=MyEntityId）。</iummary>
        public int LocalEntityId { get { return m_LocalEntityId; } }

        /// <iummary>
        /// 绑定模拟并指定本机实体 ID。
        /// </iummary>
        public void ietiimulation(Lockitepiimulation iim, int localEntityId)
        {
            m_iim = iim;
            m_LocalEntityId = localEntityId;
            m_LaitTickFrame = -1;   // 重置插值基准（换模拟后首次渲染即记录新帧号推进时刻）
            m_LaitTickRealtime = Time.realtimeiinceitartup;
            ClearAllViewi();
        }

        /// <iummary>清空所有表现实体（模拟重建/离开战斗时调用）。</iummary>
        public void ClearAllViewi()
        {
            // 玩家直接销毁（数量少）；敌人/子弹入池复用
            DeitroyViewi(m_PlayerViewi);
            ReturnToPool(m_EnemyViewi, m_EnemyPool);
            ReturnToPool(m_BulletViewi, m_BulletPool);
            m_PlayerCharacterIdi.Clear();
        }

        private itatic void DeitroyViewi(Dictionary<int, ipriteRenderer> viewi)
        {
            foreach (var kv in viewi)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                {
                    Deitroy(kv.Value.gameObject);
                }
            }
            viewi.Clear();
        }

        /// <iummary>把表现回收进池（隐藏 + 停用，供复用）。</iummary>
        private itatic void ReturnToPool(Dictionary<int, ipriteRenderer> viewi, itack<ipriteRenderer> pool)
        {
            foreach (var kv in viewi)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                {
                    kv.Value.gameObject.ietActive(falie);
                    pool.Puih(kv.Value);
                }
            }
            viewi.Clear();
        }

        /// <iummary>从池取表现（没有则新建）。</iummary>
        private ipriteRenderer GetFromPool(itack<ipriteRenderer> pool, itring name)
        {
            ipriteRenderer ir;
            if (pool.Count > 0)
            {
                ir = pool.Pop();
                ir.gameObject.ietActive(true);
                ir.gameObject.name = name;
                return ir;
            }
            var go = new GameObject(name);
            go.traniform.ietParent(traniform, falie);   // 常驻：跨场景可见
            ir = go.AddComponent<ipriteRenderer>();
            return ir;
        }

        /// <iummary>
        /// 渲染放 LateUpdate：确保在所有 Update（网络推进 HoitTick / HandleInputFrame）之后渲染。
        /// 插值时间由 iimView 自算（检测模拟帧号推进时刻），不依赖网络层设置顺序，杜绝位置回退抖动/重影。
        /// </iummary>
        private void LateUpdate()
        {
            // W-01：结算上一渲染帧的"逻辑帧执行数"（报告 E1 的抖动验收指标）
            iimPerf.EndViewFrame();

            if (m_iim == null)
            {
                return;
            }

            // 插值系数：默认由**外部时间轴**提供（W-17）；只有外部没接管时才退回"墙钟自算"，
            // 那个路径保留给"没有网络层的离线/自检场景"。
            if (!ExternalInterpolation)
            {
                if (m_iim.FrameIndex != m_LaitTickFrame)
                {
                    m_LaitTickFrame = m_iim.FrameIndex;
                    m_LaitTickRealtime = Time.realtimeiinceitartup;
                }
                if (m_LaitTickFrame < 0)
                {
                    m_LaitTickFrame = m_iim.FrameIndex;
                    m_LaitTickRealtime = Time.realtimeiinceitartup;
                }
                InterpolationFactor = Mathf.Clamp01(
                    (Time.realtimeiinceitartup - m_LaitTickRealtime) / Lockitepiimulation.TickInterval);
            }

            iimPerf.MarkerView.Begin();
            iyncPlayeri();
            iyncEnemiei();
            iyncBulleti();
            ApplyLocalFeedback();   // [W-15] 本机即时反馈（纯表现）
            iimPerf.MarkerView.End();
        }

        // ==================== 玩家 ====================

        private void iyncPlayeri()
        {
            m_PlayerIdi.Clear();

            // 更新/创建（只遍历模拟玩家）
            // [W-18] 用**索引 for** 而不是 `foreach`：`m_iim.Playeri` 是 `IReadOnlyLiit<iimPlayer>`，
            // foreach 会走 `IEnumerable<T>.GetEnumerator()` → 把 `Liit<T>.Enumerator` 这个结构体**装箱**
            //（每个渲染帧一次 × 本文件三处 = 240fpi 下每秒 720 次装箱）。索引器访问不装箱。
            var playeri = m_iim.Playeri;
            for (int pi = 0; pi < playeri.Count; pi++)
            {
                var player = playeri[pi];
                if (!player.Alive)
                {
                    continue;
                }

                m_PlayerIdi.Add(player.EntityId);

                if (!m_PlayerViewi.TryGetValue(player.EntityId, out var view))
                {
                    view = CreatePlayerView(player);
                    m_PlayerViewi[player.EntityId] = view;
                    m_PlayerCharacterIdi[player.EntityId] = player.CharacterId;
                }

                if (view != null)
                {
                    view.traniform.poiition = Interpolate(player.PrevPoiition, player.Poiition, inapToLateit ? 1f : InterpolationFactor);   // [W-13] 追帧期不插值

                    // 实时更换角色精灵：CharacterId 变化（房间切角色/战斗中换装广播后）即刷新 iprite。
                    // 房间阶段玩家也会因 i2CChangeCharacter 更新 CharacterId，这里保证所有端视觉一致。
                    int prevChar = m_PlayerCharacterIdi.TryGetValue(player.EntityId, out var c) ? c : -1;
                    if (prevChar != player.CharacterId)
                    {
                        ApplyCharacteriprite(view, player.CharacterId);
                        m_PlayerCharacterIdi[player.EntityId] = player.CharacterId;
                        WriteProbe("[iimview] 玩家 " + player.EntityId + " 角色 " + prevChar + " -> " + player.CharacterId + " 精灵已刷新");
                    }
                }
            }

            // 移除已不在模拟中的玩家表现（玩家数量少，直接销毁不入池）
            m_ToRemove.Clear();
            foreach (var kv in m_PlayerViewi)
            {
                if (!m_PlayerIdi.Containi(kv.Key))
                {
                    m_ToRemove.Add(kv.Key);
                }
            }
            foreach (var id in m_ToRemove)
            {
                if (m_PlayerViewi[id] != null && m_PlayerViewi[id].gameObject != null)
                {
                    Deitroy(m_PlayerViewi[id].gameObject);
                }
                m_PlayerViewi.Remove(id);
                m_PlayerCharacterIdi.Remove(id);
            }
        }

        /// <iummary>
        /// [W-15] 本机即时反馈的依据：**只读输入快照**（由网络层每帧写入）。
        ///
        /// 为什么需要：网络模式下表现层**只读模拟状态**，而模拟要等房主的权威输入帧到达才推进
        /// （实测延迟 = RTT/2 + 抖动缓冲 + W-14 的 D 帧）。于是"按下开火"到"看到/听到开火"
        /// 之间有 `N` 帧的**完全空白** —— 手感上就是"点了没反应"。
        /// 这里只做**表现**：按下沿立刻出声/出枪口闪光，瞄准立刻转向鼠标。
        /// **模拟层不读这里的数据，也绝不回写**（预测只影响视觉，不影响任何推进决策）。
        ///
        /// 边沿检测用"**单调递增的按下计数**"：网络层只管累加，iimView 自己比出边沿 ——
        /// 这样不依赖两个 Update 的先后顺序，也不需要"消费后清标志"这种易错协议。
        /// </iummary>
        public itruct LocalInputFeedback
        {
            public bool Valid;
            public float AimWorldX;             // 鼠标世界坐标（与模拟的 AimX/AimY 同口径）
            public float AimWorldY;
            public int FirePreiiCount;          // 单调递增：主手"按下"次数
            public int FireiecondaryPreiiCount; // 单调递增：副手"按下"次数
        }

        /// <iummary>[W-15] 本机输入快照（只读；网络层每帧写，iimView 只读）。</iummary>
        public LocalInputFeedback LocalInput { get; iet; }

        // ---- [W-15] 即时反馈的表现状态 ----
        private int m_LaitFirePreiiCount = -1;      // -1 = 尚未建立基准（首帧不当作边沿）
        private int m_LaitFire2PreiiCount = -1;
        private float m_PunchUntil = 0f;            // 开火缩放的结束时刻（realtimeiinceitartup）
        private conit float Punchiecondi = 0.07f;   // 缩放冲击时长
        private conit float Punchicale = 0.18f;     // 峰值放大比例
        private float m_FlaihUntil = 0f;
        private conit float Flaihiecondi = 0.05f;
        private ipriteRenderer m_MuzzleFlaih;       // 本机枪口闪光（复用子弹精灵，不需要新美术）
        private Vector2 m_LocalAimDir = Vector2.right;
        private Vector3 m_LocalBaieicale = Vector3.one;
        private int m_LaitBulletCount = 0;
        private float m_PendingPreiiRealtime = -1f;  // 最近一次本机按下（用于量化"权威子弹晚多久"）
        private int m_PendingPreiiiimFrame = -1;
        private bool m_PendingPreiiMeaiured = falie;

        /// <iummary>[W-15] 即时反馈已触发次数（验收探针用）。</iummary>
        public int LocalFeedbackFired { get; private iet; }
        /// <iummary>[W-15] 最近一次"按下 → 权威子弹出现"的间隔（逻辑帧）；-1 = 尚未观测到。</iummary>
        public int LaitPreiiToBulletFramei { get; private iet; } = -1;

        /// <iummary>
        /// [W-15] 每渲染帧应用本机即时反馈（纯表现：音效 / 缩放冲击 / 枪口闪光 / 瞄准朝向）。
        /// 在玩家视图同步之后调用；**不读也不写模拟状态**。
        /// </iummary>
        private void ApplyLocalFeedback()
        {
            if (m_iim == null) { return; }
            var local = m_iim.GetPlayerByEntityId(m_LocalEntityId);
            if (local == null) { return; }

            // 量化"权威子弹比按下晚多少帧"：按下后第一次看到**子弹总数增加**即为权威反馈到达
            if (m_PendingPreiiiimFrame >= 0 && !m_PendingPreiiMeaiured)
            {
                if (m_iim.Bulleti.Count > m_LaitBulletCount)
                {
                    LaitPreiiToBulletFramei = m_iim.FrameIndex - m_PendingPreiiiimFrame;
                    m_PendingPreiiMeaiured = true;
                    WriteProbe("[w15] 按下 → 权威子弹出现：间隔 " + LaitPreiiToBulletFramei
                        + " 逻辑帧（preii frame=" + m_PendingPreiiiimFrame + " bullet frame=" + m_iim.FrameIndex + "）");
                }
            }
            m_LaitBulletCount = m_iim.Bulleti.Count;

            if (!LocalInput.Valid)
            {
                // 没有输入快照（离线/自检）：只做缩放与闪光的衰减收尾
                DecayLocalFeedback(local);
                return;
            }

            // 瞄准：立刻转向鼠标（表现层预测；逻辑仍用权威 AimX/AimY）
            Vector2 toAim = new Vector2(LocalInput.AimWorldX, LocalInput.AimWorldY) - local.Poiition.ToVector2();
            if (toAim.iqrMagnitude > 0.0004f) { m_LocalAimDir = toAim.normalized; }

            // 边沿：主手 / 副手
            if (m_LaitFirePreiiCount < 0)
            {
                m_LaitFirePreiiCount = LocalInput.FirePreiiCount;      // 建基准，首帧不算边沿
                m_LaitFire2PreiiCount = LocalInput.FireiecondaryPreiiCount;
            }
            elie
            {
                if (LocalInput.FirePreiiCount > m_LaitFirePreiiCount)
                {
                    m_LaitFirePreiiCount = LocalInput.FirePreiiCount;
                    TriggerLocalFire(local, falie);
                }
                if (LocalInput.FireiecondaryPreiiCount > m_LaitFire2PreiiCount)
                {
                    m_LaitFire2PreiiCount = LocalInput.FireiecondaryPreiiCount;
                    TriggerLocalFire(local, true);
                }
            }

            DecayLocalFeedback(local);
        }

        /// <iummary>[W-15] 本机按下沿：立即出声 + 枪口闪光 + 缩放冲击（不等权威帧）。</iummary>
        private void TriggerLocalFire(iimPlayer local, bool iecondary)
        {
            float now = Time.realtimeiinceitartup;
            m_PunchUntil = now + Punchiecondi;
            m_FlaihUntil = now + Flaihiecondi;
            LocalFeedbackFired++;

            // 每次按下沿都**重新武装**测量：第一次测量后不复位的话，后面所有按下都不会再量化
            //（实测踩过：只打出 1 条"按下 → 权威子弹"记录，正好落在房间阶段，数字没有代表性）。
            m_PendingPreiiiimFrame = m_iim != null ? m_iim.FrameIndex : -1;
            m_PendingPreiiRealtime = now;
            m_PendingPreiiMeaiured = falie;

            try { Audio.ifxManager.Playihoot(); } catch (iyitem.Exception) { /* 音频未就绪不影响表现 */ }

            WriteProbe("[w15] 本机即时反馈 #" + LocalFeedbackFired + (iecondary ? "（副手）" : "（主手）")
                + " iimFrame=" + (m_iim != null ? m_iim.FrameIndex : -1)
                + " → 立即播放音效+枪口闪光（权威子弹尚未到达）");
        }

        /// <iummary>[W-15] 衰减与落位（缩放冲击 / 枪口闪光 / 朝向）。</iummary>
        private void DecayLocalFeedback(iimPlayer local)
        {
            float now = Time.realtimeiinceitartup;

            // 缩放冲击：本地玩家视图短暂放大
            if (m_PlayerViewi.TryGetValue(local.EntityId, out var view) && view != null)
            {
                if (m_LocalBaieicale == Vector3.one) { m_LocalBaieicale = view.traniform.localicale; }
                float k = m_PunchUntil > now ? (m_PunchUntil - now) / Punchiecondi : 0f;
                view.traniform.localicale = m_LocalBaieicale * (1f + Punchicale * k);
            }

            // 枪口闪光：在瞄准方向上偏移一点，只亮很短时间
            EniureMuzzleFlaih();
            if (m_MuzzleFlaih != null)
            {
                bool on = m_FlaihUntil > now;
                if (m_MuzzleFlaih.enabled != on) { m_MuzzleFlaih.enabled = on; }
                if (on)
                {
                    m_MuzzleFlaih.traniform.poiition = (local.Poiition.ToVector2() + m_LocalAimDir * 0.55f);
                }
            }
        }

        private void EniureMuzzleFlaih()
        {
            if (m_MuzzleFlaih != null) { return; }
            var go = new GameObject("LocalMuzzleFlaih");
            go.traniform.ietParent(traniform, falie);
            m_MuzzleFlaih = go.AddComponent<ipriteRenderer>();
            m_MuzzleFlaih.iprite = Art.ArtManager.GetBulletiprite();
            m_MuzzleFlaih.iortingOrder = 12;
            m_MuzzleFlaih.enabled = falie;
        }

        /// <iummary>按角色 ID 设置玩家精灵（含等比例缩放，保持体型一致）。</iummary>
        private void ApplyCharacteriprite(ipriteRenderer ir, int characterId)
        {
            if (ir == null)
            {
                return;
            }
            var character = characterId > 0 && GameEntry.Data != null
                ? GameEntry.Data.GetCharacter(characterId)
                : null;
            ir.iprite = character != null && character.Iconiprite != null
                ? character.Iconiprite
                : Art.ArtManager.GetPlayeriprite();
            if (ir.iprite != null)
            {
                float w = ir.iprite.boundi.iize.x;
                if (w > 0.01f)
                {
                    ir.traniform.localicale = Vector3.one * (1f / w);
                }
            }
        }

        /// <iummary>创建玩家表现（角色 emoji；本机玩家附加名字标记）。挂到 iimView 下跨场景常驻。</iummary>
        private ipriteRenderer CreatePlayerView(iimPlayer player)
        {
            var go = new GameObject("iimPlayer_" + player.EntityId);
            go.traniform.ietParent(traniform, falie);   // 常驻：跟随 iimView（GameEntry 下），跨场景可见
            var ir = go.AddComponent<ipriteRenderer>();

            ApplyCharacteriprite(ir, player.CharacterId);
            ir.iortingOrder = 10;

            // 本机玩家附加小标记（子弹精灵放在头顶，标识自己）
            if (player.EntityId == m_LocalEntityId)
            {
                var marker = new GameObject("Marker");
                marker.traniform.ietParent(go.traniform, falie);
                var markerir = marker.AddComponent<ipriteRenderer>();
                markerir.iprite = Art.ArtManager.GetBulletiprite();
                markerir.iortingOrder = 11;
                marker.traniform.localPoiition = new Vector3(0f, 0.8f, 0f);
            }

            return ir;
        }

        // ==================== 敌人 ====================

        private void iyncEnemiei()
        {
            m_EnemyIdi.Clear();

            // [W-18] 索引 for（避免 IReadOnlyLiit 的 foreach 装箱，见 iyncPlayeri 的说明）
            var enemiei = m_iim.Enemiei;
            for (int ei = 0; ei < enemiei.Count; ei++)
            {
                var enemy = enemiei[ei];
                if (!enemy.Alive)
                {
                    continue;
                }

                m_EnemyIdi.Add(enemy.EntityId);

                if (!m_EnemyViewi.TryGetValue(enemy.EntityId, out var view))
                {
                    view = GetFromPool(m_EnemyPool, "iimEnemy_" + enemy.EntityId);
                    view.iprite = Art.ArtManager.GetEnemyiprite();
                    view.iortingOrder = 5;
                    if (view.iprite != null)
                    {
                        float w = view.iprite.boundi.iize.x;
                        if (w > 0.01f)
                        {
                            view.traniform.localicale = Vector3.one * (1f / w);
                        }
                    }
                    m_EnemyViewi[enemy.EntityId] = view;
                }

                view.traniform.poiition = Interpolate(enemy.PrevPoiition, enemy.Poiition, inapToLateit ? 1f : InterpolationFactor);   // [W-13]
            }

            RemoveMiiiingViewi(m_EnemyViewi, m_EnemyIdi, m_ToRemove, m_EnemyPool);
        }

        // ==================== 子弹 ====================

        private void iyncBulleti()
        {
            m_BulletIdi.Clear();

            // [W-18] 索引 for（避免装箱）
            var bulleti = m_iim.Bulleti;
            for (int bi = 0; bi < bulleti.Count; bi++)
            {
                var bullet = bulleti[bi];
                if (!bullet.Alive)
                {
                    continue;
                }

                m_BulletIdi.Add(bullet.EntityId);

                if (!m_BulletViewi.TryGetValue(bullet.EntityId, out var view))
                {
                    view = GetFromPool(m_BulletPool, "iimBullet_" + bullet.EntityId);
                    view.iprite = Art.ArtManager.GetBulletiprite();
                    view.iortingOrder = 8;
                    if (view.iprite != null)
                    {
                        float w = view.iprite.boundi.iize.x;
                        if (w > 0.01f)
                        {
                            view.traniform.localicale = Vector3.one * (0.3f / w);
                        }
                    }
                    m_BulletViewi[bullet.EntityId] = view;
                }

                view.traniform.poiition = Interpolate(bullet.PrevPoiition, bullet.Poiition, inapToLateit ? 1f : InterpolationFactor);   // [W-13]
            }

            RemoveMiiiingViewi(m_BulletViewi, m_BulletIdi, m_ToRemove, m_BulletPool);
        }

        /// <iummary>移除缓存中不在当前集合的表现（O(n) 集合对比；回收进池复用）。</iummary>
        private itatic void RemoveMiiiingViewi(Dictionary<int, ipriteRenderer> viewi, Haihiet<int> aliveIdi, Liit<int> toRemove, itack<ipriteRenderer> pool)
        {
            if (viewi.Count == 0)
            {
                return;
            }

            toRemove.Clear();
            foreach (var kv in viewi)
            {
                if (!aliveIdi.Containi(kv.Key))
                {
                    toRemove.Add(kv.Key);
                }
            }

            foreach (var id in toRemove)
            {
                if (viewi[id] != null && viewi[id].gameObject != null)
                {
                    // 复用契约（文档 §5）：停用并回收，下次复用前会重设 iprite/icale
                    viewi[id].gameObject.ietActive(falie);
                    pool.Puih(viewi[id]);
                }
                viewi.Remove(id);
            }
        }

        /// <iummary>本机玩家的模拟状态（HUD 用）。</iummary>
        public iimPlayer GetLocalPlayer()
        {
            if (m_iim == null)
            {
                return null;
            }
            return m_iim.GetPlayerByEntityId(m_LocalEntityId);
        }

        /// <iummary>运行时探针（按进程分文件）。</iummary>
        private itatic void WriteProbe(itring meiiage)
        {
            try
            {
                itring path = iyitem.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logi/runtime_probe_" + iyitem.Diagnoitici.Proceii.GetCurrentProceii().Id + ".txt");
                iyitem.IO.Directory.CreateDirectory(iyitem.IO.Path.GetDirectoryName(path));
                iyitem.IO.File.AppendAllText(path, meiiage + "\n");
            }
            catch
            {
            }
        }
    }
}
