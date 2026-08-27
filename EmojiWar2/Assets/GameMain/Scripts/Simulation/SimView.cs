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

        // 实体表现缓存（玩家/敌人/子弹按 EntityId）
        private readonly Dictionary<int, SpriteRenderer> m_PlayerViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> m_EnemyViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> m_BulletViews = new Dictionary<int, SpriteRenderer>();

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
            DestroyViews(m_PlayerViews);
            DestroyViews(m_EnemyViews);
            DestroyViews(m_BulletViews);
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
                    view.transform.position = new Vector3(player.Position.x, player.Position.y, 0f);
                }
            }

            // 移除已不在模拟中的表现（集合对比，O(n)）
            RemoveMissingViews(m_PlayerViews, m_PlayerIds, m_ToRemove);
        }

        /// <summary>创建玩家表现（角色 emoji；本机玩家附加名字标记）。</summary>
        private SpriteRenderer CreatePlayerView(SimPlayer player)
        {
            var go = new GameObject("SimPlayer_" + player.EntityId);
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
                    var go = new GameObject("SimEnemy_" + enemy.EntityId);
                    view = go.AddComponent<SpriteRenderer>();
                    view.sprite = Art.ArtManager.GetEnemySprite();
                    view.sortingOrder = 5;
                    if (view.sprite != null)
                    {
                        float w = view.sprite.bounds.size.x;
                        if (w > 0.01f)
                        {
                            go.transform.localScale = Vector3.one * (1f / w);
                        }
                    }
                    m_EnemyViews[enemy.EntityId] = view;
                }

                view.transform.position = new Vector3(enemy.Position.x, enemy.Position.y, 0f);
            }

            RemoveMissingViews(m_EnemyViews, m_EnemyIds, m_ToRemove);
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
                    var go = new GameObject("SimBullet_" + bullet.EntityId);
                    view = go.AddComponent<SpriteRenderer>();
                    view.sprite = Art.ArtManager.GetBulletSprite();
                    view.sortingOrder = 8;
                    if (view.sprite != null)
                    {
                        float w = view.sprite.bounds.size.x;
                        if (w > 0.01f)
                        {
                            go.transform.localScale = Vector3.one * (0.3f / w);
                        }
                    }
                    m_BulletViews[bullet.EntityId] = view;
                }

                view.transform.position = new Vector3(bullet.Position.x, bullet.Position.y, 0f);
            }

            RemoveMissingViews(m_BulletViews, m_BulletIds, m_ToRemove);
        }

        /// <summary>移除缓存中不在当前集合的表现（O(n) 集合对比，避免每帧 O(n*m)）。</summary>
        private static void RemoveMissingViews(Dictionary<int, SpriteRenderer> views, HashSet<int> aliveIds, List<int> toRemove)
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
                    Destroy(views[id].gameObject);
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
