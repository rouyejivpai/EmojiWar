//------------------------------------------------------------
// EmojiWar GameMain - 武器基类（数据驱动重构）
// 武器参数来自 DRWeapon 数据表，逻辑与表现分离。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Weapon
{
    /// <summary>
    /// 武器基类：由武器数据行驱动。
    /// </summary>
    public abstract class WeaponBase : MonoBehaviour
    {
        [SerializeField]
        protected string m_WeaponName = "Weapon";

        [SerializeField]
        protected float m_Damage = 10f;

        [SerializeField]
        protected float m_FireRate = 3f;        // 每秒射击次数

        [SerializeField]
        protected float m_Range = 10f;           // 射程

        [SerializeField]
        protected int m_MaxAmmo = 30;            // 最大弹药

        [SerializeField]
        protected int m_CurrentAmmo = 30;        // 当前弹药

        [SerializeField]
        protected float m_ReloadTime = 2f;       // 装弹时间

        [SerializeField]
        protected Transform m_FirePoint = null;  // 射击点

        [SerializeField]
        protected bool m_IsReloading = false;

        protected float m_LastFireTime = 0f;
        protected Entity.EntityBase m_Owner = null;
        protected WeaponModComponent m_ModComponent = null;

        public string WeaponName { get { return m_WeaponName; } }
        public float Damage { get { return m_Damage; } }
        public float Range { get { return m_Range; } }
        public int MaxAmmo { get { return m_MaxAmmo; } }
        public int CurrentAmmo { get { return m_CurrentAmmo; } }
        public bool IsReloading { get { return m_IsReloading; } }
        public Entity.EntityBase Owner { get { return m_Owner; } }
        public WeaponModComponent ModComponent { get { return m_ModComponent; } }

        /// <summary>实际射速（含 Mod 修正）。</summary>
        public float FireRate
        {
            get
            {
                if (m_ModComponent == null)
                {
                    return m_FireRate;
                }

                var (add, mul) = m_ModComponent.GetFireRateModifiers();
                return Mathf.Max(0.1f, (m_FireRate + add) * (1f + mul));
            }
        }

        /// <summary>是否可开火。</summary>
        public bool CanFire
        {
            get
            {
                return !m_IsReloading && m_CurrentAmmo > 0 && Time.time - m_LastFireTime >= 1f / Mathf.Max(0.01f, FireRate);
            }
        }

        protected virtual void Awake()
        {
            m_CurrentAmmo = m_MaxAmmo;
            m_ModComponent = GetComponent<WeaponModComponent>();
            if (m_ModComponent == null)
            {
                m_ModComponent = gameObject.AddComponent<WeaponModComponent>();
            }
        }

        protected virtual void Start()
        {
            if (m_FirePoint == null)
            {
                m_FirePoint = transform;
            }
        }

        /// <summary>
        /// 从数据行配置武器参数。
        /// </summary>
        public void Configure(Data.DRWeapon row)
        {
            if (row == null)
            {
                return;
            }

            m_WeaponName = row.WeaponName;
            m_Damage = row.Damage;
            m_FireRate = row.FireRate;
            m_Range = row.Range;
            m_MaxAmmo = row.MaxAmmo;
            m_CurrentAmmo = row.MaxAmmo;
            m_ReloadTime = row.ReloadTime;
        }

        /// <summary>
        /// 绑定持有者。
        /// </summary>
        public virtual void SetOwner(Entity.EntityBase owner)
        {
            m_Owner = owner;
        }

        /// <summary>
        /// 尝试开火（含冷却与弹药检查）。
        /// </summary>
        public virtual bool TryFire(Vector2 target)
        {
            if (!CanFire)
            {
                return false;
            }

            if (Fire(target))
            {
                m_LastFireTime = Time.time;
                m_CurrentAmmo--;
                return true;
            }

            return false;
        }

        /// <summary>具体开火逻辑（子类实现）。</summary>
        protected abstract bool Fire(Vector2 target);

        /// <summary>
        /// 装弹。
        /// </summary>
        public virtual void Reload()
        {
            if (m_IsReloading || m_CurrentAmmo == m_MaxAmmo)
            {
                return;
            }

            StartCoroutine(ReloadCoroutine());
        }

        protected virtual System.Collections.IEnumerator ReloadCoroutine()
        {
            m_IsReloading = true;
            yield return new WaitForSeconds(m_ReloadTime);
            m_CurrentAmmo = m_MaxAmmo;
            m_IsReloading = false;
        }

        /// <summary>开火进度（0~1，用于 UI 冷却条）。</summary>
        public virtual float GetProgress()
        {
            float interval = 1f / Mathf.Max(0.01f, FireRate);
            float progress = (Time.time - m_LastFireTime) / interval;
            return Mathf.Clamp01(progress);
        }

        /// <summary>武器信息（UI 展示）。</summary>
        public virtual string GetWeaponInfo()
        {
            return string.Format("{0} - 弹药 {1}/{2} - 伤害 {3} - 射速 {4:F1}/s",
                m_WeaponName, m_CurrentAmmo, m_MaxAmmo, m_Damage, FireRate);
        }
    }
}
