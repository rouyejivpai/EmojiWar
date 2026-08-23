//------------------------------------------------------------
// EmojiWar GameMain - 远程武器（水滴枪等）
// 向目标方向发射子弹。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Weapon
{
    /// <summary>
    /// 远程武器：发射 Projectile。
    /// </summary>
    public class RangedWeapon : WeaponBase
    {
        [SerializeField]
        private float m_BulletSpeed = 15f;

        [SerializeField]
        private float m_Spread = 0.05f;

        [SerializeField]
        private GameObject m_ProjectilePrefab = null;

        protected override bool Fire(Vector2 target)
        {
            if (m_ProjectilePrefab == null || m_FirePoint == null)
            {
                return false;
            }

            Vector2 firePos = m_FirePoint.position;
            Vector2 direction = ((Vector2)target - firePos).normalized;
            if (direction == Vector2.zero)
            {
                direction = m_FirePoint.right;
            }

            // 散射 Mod：一次发射多颗子弹（扇形分布）
            int spreadCount = m_ModComponent != null ? m_ModComponent.GetSpreadCount() : 1;
            float baseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            for (int i = 0; i < spreadCount; i++)
            {
                float angleOffset = 0f;

                // 随机扩散（基础）
                if (m_Spread > 0f)
                {
                    angleOffset += Random.Range(-m_Spread, m_Spread);
                }

                // 散射 Mod：均匀扇形分布
                if (spreadCount > 1)
                {
                    float spreadAngle = 25f;
                    float t = spreadCount > 1 ? (float)i / (spreadCount - 1) - 0.5f : 0f;
                    angleOffset += t * spreadAngle;
                }

                float finalAngle = baseAngle + angleOffset;
                Vector2 fireDirection = new Vector2(Mathf.Cos(finalAngle * Mathf.Deg2Rad), Mathf.Sin(finalAngle * Mathf.Deg2Rad));

                GameObject bullet = Instantiate(m_ProjectilePrefab, firePos, Quaternion.identity);
                var projectile = bullet.GetComponent<Projectile>();
                if (projectile != null)
                {
                    // 冰霜效果参数（来自 Mod）
                    float freezeSlow = 0f;
                    float freezeDuration = 0f;
                    if (m_ModComponent != null && m_ModComponent.HasEffect(ModEffectType.Freeze))
                    {
                        freezeSlow = m_ModComponent.GetEffectParam(ModEffectType.Freeze, 1);
                        freezeDuration = m_ModComponent.GetEffectParam(ModEffectType.Freeze, 2);
                    }

                    projectile.Setup(fireDirection, m_BulletSpeed, Damage,
                        Owner != null ? Owner.Team : Entity.EntityTeam.Player, Owner,
                        freezeSlow, freezeDuration, m_ModComponent);
                }
            }

            // 射击音效
            Audio.SfxManager.PlayShoot();

            return true;
        }
    }
}
