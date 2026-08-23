//------------------------------------------------------------
// EmojiWar GameMain - 实体基类
// 封装 EntityData 与 MonoBehaviour 的绑定：移动、伤害、死亡。
// 表现层（Sprite/Animator）由子类负责。
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EmojiWar.GameMain.Entity
{
    /// <summary>
    /// 实体基类：持有 EntityData，提供移动/受击/死亡基础逻辑。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class EntityBase : MonoBehaviour
    {
        [SerializeField]
        private EntityData m_Data = new EntityData();

        protected Rigidbody2D m_Rigidbody = null;
        protected SpriteRenderer m_SpriteRenderer = null;
        protected Buff.BuffComponent m_BuffComponent = null;

        /// <summary>实体数据（只读访问）。</summary>
        public EntityData Data
        {
            get { return m_Data; }
        }

        /// <summary>Buff 组件。</summary>
        public Buff.BuffComponent BuffComponent
        {
            get { return m_BuffComponent; }
        }

        /// <summary>移动速度倍率（含 Buff 修正）。</summary>
        public float MoveSpeedMultiplier
        {
            get { return m_BuffComponent != null ? m_BuffComponent.MoveSpeedMultiplier : 1f; }
        }

        /// <summary>是否被眩晕。</summary>
        public bool IsStunned
        {
            get { return m_BuffComponent != null && m_BuffComponent.IsStunned; }
        }

        /// <summary>阵营。</summary>
        public EntityTeam Team
        {
            get { return m_Data.Team; }
            set { m_Data.Team = value; }
        }

        /// <summary>是否存活。</summary>
        public bool IsAlive
        {
            get { return m_Data.IsAlive; }
        }

        /// <summary>当前生命值。</summary>
        public float CurrentHealth
        {
            get { return m_Data.CurrentHealth; }
        }

        /// <summary>移动速度。</summary>
        public float MoveSpeed
        {
            get { return m_Data.MoveSpeed; }
            set { m_Data.MoveSpeed = value; }
        }

        /// <summary>死亡事件（参数：死亡实体）。</summary>
        public event Action<EntityBase> OnDeath;

        protected virtual void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody2D>();
            m_SpriteRenderer = GetComponent<SpriteRenderer>();
            m_BuffComponent = GetComponent<Buff.BuffComponent>();
            if (m_BuffComponent == null)
            {
                m_BuffComponent = gameObject.AddComponent<Buff.BuffComponent>();
            }

            // 尝试应用美术精灵（无则保持占位）
            ApplyArtSprite();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.gravityScale = 0f;
                m_Rigidbody.drag = 0f;
                m_Rigidbody.angularDrag = 0f;
                m_Rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                m_Rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            }
        }

        protected virtual void Start()
        {
        }

        /// <summary>
        /// 初始化实体数据（用于实例化时注入配置）。
        /// </summary>
        public void Init(EntityData data)
        {
            if (data == null)
            {
                return;
            }

            m_Data = data;
        }

        /// <summary>
        /// 设置移动方向（每帧由子类驱动）。
        /// </summary>
        protected void Move(Vector2 direction)
        {
            if (!m_Data.IsAlive || m_Rigidbody == null || IsStunned)
            {
                return;
            }

            Vector2 normalized = direction.sqrMagnitude > 1f ? direction.normalized : direction;
            m_Rigidbody.velocity = normalized * m_Data.MoveSpeed * MoveSpeedMultiplier;

            if (normalized.sqrMagnitude > 0.01f)
            {
                m_Data.State = EntityState.Moving;
            }
            else
            {
                m_Data.State = EntityState.Idle;
            }
        }

        /// <summary>
        /// 立即停止移动。
        /// </summary>
        protected void StopMoving()
        {
            if (m_Rigidbody != null)
            {
                m_Rigidbody.velocity = Vector2.zero;
            }
            m_Data.State = EntityState.Idle;
        }

        /// <summary>
        /// 受到伤害。返回是否造成伤害。
        /// </summary>
        public virtual bool TakeDamage(float damage, EntityBase attacker = null)
        {
            if (!m_Data.IsAlive || damage <= 0f)
            {
                return false;
            }

            m_Data.CurrentHealth = Mathf.Max(0f, m_Data.CurrentHealth - damage);
            OnTakeDamage();

            if (m_Data.CurrentHealth <= 0f)
            {
                Die();
            }

            return true;
        }

        /// <summary>受击回调（子类可覆盖做闪烁等表现）。</summary>
        protected virtual void OnTakeDamage()
        {
            Audio.SfxManager.PlayHit();
        }

        /// <summary>
        /// 死亡。
        /// </summary>
        protected virtual void Die()
        {
            if (!m_Data.IsAlive)
            {
                return;
            }

            m_Data.IsAlive = false;
            m_Data.State = EntityState.Dead;
            StopMoving();

            OnDeath?.Invoke(this);
            Destroy(gameObject, 0.1f);
        }

        /// <summary>
        /// 应用美术精灵（子类覆盖指定具体 Sprite）。
        /// </summary>
        protected virtual void ApplyArtSprite()
        {
        }

        /// <summary>
        /// 设置 SpriteRenderer 的精灵（带缩放适配）。
        /// </summary>
        protected void SetSprite(Sprite sprite)
        {
            if (sprite == null || m_SpriteRenderer == null)
            {
                return;
            }

            m_SpriteRenderer.sprite = sprite;

            // 按精灵尺寸缩放对象（保持视觉大小一致）
            float baseSize = 1f;
            float spriteWidth = sprite.bounds.size.x;
            if (spriteWidth > 0.01f)
            {
                transform.localScale = Vector3.one * (baseSize / spriteWidth);
            }
        }

        /// <summary>
        /// 设置视觉方向（翻转 Sprite）。
        /// </summary>
        protected void FaceDirection(Vector2 direction)
        {
            if (m_SpriteRenderer == null || direction.x == 0f)
            {
                return;
            }

            m_SpriteRenderer.flipX = direction.x < 0f;
        }
    }
}
