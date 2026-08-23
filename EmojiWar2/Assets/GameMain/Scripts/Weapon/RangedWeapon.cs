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

            // 随机扩散
            if (m_Spread > 0f)
            {
                float randomAngle = Random.Range(-m_Spread, m_Spread);
                direction = Quaternion.Euler(0f, 0f, randomAngle) * direction;
            }

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

                projectile.Setup(direction, m_BulletSpeed, m_Damage,
                    Owner != null ? Owner.Team : Entity.EntityTeam.Player, Owner,
                    freezeSlow, freezeDuration);
            }

            return true;
        }
    }
}
