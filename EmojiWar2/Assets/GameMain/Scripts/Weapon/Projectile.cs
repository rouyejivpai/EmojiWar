//------------------------------------------------------------
// EmojiWar GameMain - 子弹
// 沿方向飞行，命中敌对实体造成伤害。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Weapon
{
    /// <summary>
    /// 子弹：飞行 + 碰撞伤害。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Projectile : MonoBehaviour
    {
        [SerializeField]
        private float m_Speed = 15f;

        [SerializeField]
        private float m_Lifetime = 3f;

        [SerializeField]
        private float m_Damage = 10f;

        private Entity.EntityTeam m_Team = Entity.EntityTeam.Neutral;
        private Entity.EntityBase m_Shooter = null;
        private Rigidbody2D m_Rigidbody = null;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody2D>();
            if (m_Rigidbody != null)
            {
                m_Rigidbody.gravityScale = 0f;
            }
        }

        private void Start()
        {
            Destroy(gameObject, m_Lifetime);
        }

        /// <summary>
        /// 初始化子弹（由武器调用）。
        /// </summary>
        public void Setup(Vector2 direction, float speed, float damage, Entity.EntityTeam team, Entity.EntityBase shooter)
        {
            m_Speed = speed;
            m_Damage = damage;
            m_Team = team;
            m_Shooter = shooter;

            if (m_Rigidbody == null)
            {
                m_Rigidbody = GetComponent<Rigidbody2D>();
            }
            if (m_Rigidbody != null)
            {
                m_Rigidbody.velocity = direction.normalized * m_Speed;
            }

            // 朝向
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var entity = other.GetComponentInParent<Entity.EntityBase>();
            if (entity == null)
            {
                return;
            }

            // 不伤害自己与同阵营
            if (entity == m_Shooter || entity.Team == m_Team)
            {
                return;
            }

            entity.TakeDamage(m_Damage, m_Shooter);
            Destroy(gameObject);
        }
    }
}
