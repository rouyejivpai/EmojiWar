//------------------------------------------------------------
// EmojiWar GameMain - 敌人实体
// 简单 AI：朝最近玩家移动，接触造成伤害。
// 后续网络化：AI 由服务器驱动，此处为本地占位实现。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Entity
{
    /// <summary>
    /// 敌人实体：追逐最近玩家并接触伤害。
    /// </summary>
    public class EnemyEntity : EntityBase
    {
        [Header("AI 参数")]
        [SerializeField]
        private float m_ContactDamage = 10f;

        [SerializeField]
        private float m_AttackInterval = 1f;

        [SerializeField]
        private float m_DetectionRadius = 30f;

        private float m_LastAttackTime = 0f;
        private Transform m_Target = null;

        /// <summary>接触伤害。</summary>
        public float ContactDamage
        {
            get { return m_ContactDamage; }
            set { m_ContactDamage = value; }
        }

        private void Update()
        {
            if (!IsAlive)
            {
                return;
            }

            // 寻找目标（缓存，失效时重新找）
            if (m_Target == null || !m_Target.gameObject.activeInHierarchy)
            {
                m_Target = FindNearestPlayer();
            }

            if (m_Target != null)
            {
                Vector2 toTarget = (Vector2)m_Target.position - (Vector2)transform.position;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Move(toTarget.normalized);
                    FaceDirection(toTarget);
                }

                // 接触伤害
                float distance = toTarget.magnitude;
                if (distance < 0.6f && Time.time - m_LastAttackTime >= m_AttackInterval)
                {
                    m_LastAttackTime = Time.time;
                    var player = m_Target.GetComponentInParent<PlayerEntity>();
                    if (player != null)
                    {
                        player.TakeDamage(m_ContactDamage, this);
                    }
                }
            }
            else
            {
                StopMoving();
            }
        }

        /// <summary>
        /// 查找最近的玩家实体。
        /// </summary>
        private Transform FindNearestPlayer()
        {
            PlayerEntity[] players = Object.FindObjectsOfType<PlayerEntity>();
            Transform nearest = null;
            float minDistSqr = m_DetectionRadius * m_DetectionRadius;

            foreach (var player in players)
            {
                if (player == null || player == this || !player.IsAlive)
                {
                    continue;
                }

                float distSqr = ((Vector2)player.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (distSqr < minDistSqr)
                {
                    minDistSqr = distSqr;
                    nearest = player.transform;
                }
            }

            return nearest;
        }
    }
}
