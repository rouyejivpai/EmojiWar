//------------------------------------------------------------
// EmojiWar GameMain - 确定性模拟表现层（SimView）
// 每帧把 LockstepSimulation 的状态渲染为场景实体：
//   玩家 → 角色 emoji（CharacterId 区分）
//   敌人 → 敌人 emoji
//   子弹 → 子弹 sprite
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
        /// 渲染插值系数 0~1（0=上一逻辑帧，1=当前逻辑帧）。
        /// 由网络层每帧按 (now - lastTickTime)/TickInterval 设置（文档 §2 渲染插值）。
        /// </summary>
        public float InterpolationFactor { get; set; } = 1f;

        /// <summary>插值位置（文档 §2：表现层用 prev/cur 插值，渲染帧率自由）。</summary>
        private static Vector3 Interpolate(Vector2 prev, Vector2 cur, float t)
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

        // 对象池（文档 §5：子弹/敌人频繁增删复用 GameObject，避免每帧 Instantiate/Destroy）
        private readonly Stack<SpriteRenderer> m_EnemyPool = new Stack<SpriteRenderer>();
        private readonly Stack<SpriteRenderer> m_BulletPool = new Stack<SpriteRenderer>();

        // 复用临时容器（避免每帧分配）
        private readonly HashSet<int> m_PlayerIds = new HashSet<int>();
        private readonly HashSet<int> m_EnemyIds = new HashSet<int>();
        private readonly HashSet<int> m_BulletIds = new HashSet<int>();
        private readonly List<int> m_ToRemove = new List<int>();

        /// <summary>当前绑定模拟（无则跳过渲染）。</summary>
        public LockstepSimulation Simulation { get { return m_Sim; } }

        /// <summary>本机实体 ID（Host=session0 实体；Client=MyEntityId）。</summary>
        public int LocalEntityId { get { return m_LocalEntityId; } }

        /// <summary>
        /// 绑定模拟并指定本机实体 ID。
        /// </summary>
        public void SetSimulation(LockstepSimulation sim, int localEntityId)
        {
            m_Sim = sim;
            m_LocalEntityId = localEntityId;
            ClearAllViews();
        }

        /// <summary>清空所有表现实体（模拟重建/离开战斗时调用）。</summary>
        public void ClearAllViews()
        {
            // 玩家直接销毁（数量少）；敌人/子弹入池复用
            DestroyViews(m_PlayerViews);
            ReturnToPool(m_EnemyViews, m_EnemyPool);
            ReturnToPool(m_BulletViews, m_BulletPool);
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
            SpriteRenderer sr;
            if (pool.Count > 0)
            {
                sr = pool.Pop();
                sr.gameObject.SetActive(true);
                sr.gameObject.name = name;
                return sr;
            }
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);   // 常驻：跨场景可见
            sr = go.AddComponent<SpriteRenderer>();
            return sr;
        }

        private void Update()
        {
            if (m_Sim == null)
            {
                return;
            }

            SyncPlayers();
            SyncEnemies();
            SyncBullets();
        }

        // ==================== 玩家 ====================

        private void SyncPlayers()
        {
            m_PlayerIds.Clear();

            // 更新/创建（只遍历模拟玩家）
            foreach (var player in m_Sim.Players)
            {
                if (!player.Alive)
                {
                    continue;
                }

                m_PlayerIds.Add(player.EntityId);

                if (!m_PlayerViews.TryGetValue(player.EntityId, out var view))
                {
                    view = CreatePlayerView(player);
                    m_PlayerViews[player.EntityId] = view;
                }

                if (view != null)
                {
                    view.transform.position = Interpolate(player.PrevPosition, player.Position, InterpolationFactor);
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
            }
        }

        /// <summary>创建玩家表现（角色 emoji；本机玩家附加名字标记）。挂到 SimView 下跨场景常驻。</summary>
        private SpriteRenderer CreatePlayerView(SimPlayer player)
        {
            var go = new GameObject("SimPlayer_" + player.EntityId);
            go.transform.SetParent(transform, false);   // 常驻：跟随 SimView（GameEntry 下），跨场景可见
            var sr = go.AddComponent<SpriteRenderer>();

            string icon = null;
            var character = player.CharacterId > 0 && GameEntry.Data != null
                ? GameEntry.Data.GetCharacter(player.CharacterId)
                : null;
            if (character != null)
            {
                icon = character.Icon;
            }
            sr.sprite = Art.ArtManager.GetCharacterSprite(icon);
            sr.sortingOrder = 10;

            if (sr.sprite != null)
            {
                float w = sr.sprite.bounds.size.x;
                if (w > 0.01f)
                {
                    go.transform.localScale = Vector3.one * (1f / w);
                }
            }

            // 本机玩家附加小标记（子弹精灵放在头顶，标识自己）
            if (player.EntityId == m_LocalEntityId)
            {
                var marker = new GameObject("Marker");
                marker.transform.SetParent(go.transform, false);
                var markerSr = marker.AddComponent<SpriteRenderer>();
                markerSr.sprite = Art.ArtManager.GetBulletSprite();
                markerSr.sortingOrder = 11;
                marker.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            }

            return sr;
        }

        // ==================== 敌人 ====================

        private void SyncEnemies()
        {
            m_EnemyIds.Clear();

            foreach (var enemy in m_Sim.Enemies)
            {
                if (!enemy.Alive)
                {
                    continue;
                }

                m_EnemyIds.Add(enemy.EntityId);

                if (!m_EnemyViews.TryGetValue(enemy.EntityId, out var view))
                {
                    view = GetFromPool(m_EnemyPool, "SimEnemy_" + enemy.EntityId);
                    view.sprite = Art.ArtManager.GetEnemySprite();
                    view.sortingOrder = 5;
                    if (view.sprite != null)
                    {
                        float w = view.sprite.bounds.size.x;
                        if (w > 0.01f)
                        {
                            view.transform.localScale = Vector3.one * (1f / w);
                        }
                    }
                    m_EnemyViews[enemy.EntityId] = view;
                }

                view.transform.position = Interpolate(enemy.PrevPosition, enemy.Position, InterpolationFactor);
            }

            RemoveMissingViews(m_EnemyViews, m_EnemyIds, m_ToRemove, m_EnemyPool);
        }

        // ==================== 子弹 ====================

        private void SyncBullets()
        {
            m_BulletIds.Clear();

            foreach (var bullet in m_Sim.Bullets)
            {
                if (!bullet.Alive)
                {
                    continue;
                }

                m_BulletIds.Add(bullet.EntityId);

                if (!m_BulletViews.TryGetValue(bullet.EntityId, out var view))
                {
                    view = GetFromPool(m_BulletPool, "SimBullet_" + bullet.EntityId);
                    view.sprite = Art.ArtManager.GetBulletSprite();
                    view.sortingOrder = 8;
                    if (view.sprite != null)
                    {
                        float w = view.sprite.bounds.size.x;
                        if (w > 0.01f)
                        {
                            view.transform.localScale = Vector3.one * (0.3f / w);
                        }
                    }
                    m_BulletViews[bullet.EntityId] = view;
                }

                view.transform.position = Interpolate(bullet.PrevPosition, bullet.Position, InterpolationFactor);
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
                    // 复用契约（文档 §5）：停用并回收，下次复用前会重设 sprite/scale
                    views[id].gameObject.SetActive(false);
                    pool.Push(views[id]);
                }
                views.Remove(id);
            }
        }

        /// <summary>本机玩家的模拟状态（HUD 用）。</summary>
        public SimPlayer GetLocalPlayer()
        {
            if (m_Sim == null)
            {
                return null;
            }
            return m_Sim.GetPlayerByEntityId(m_LocalEntityId);
        }
    }
}
