//------------------------------------------------------------
// EmojiWar GameMain - 确定性玩家配置工厂（单一逻辑源）
// 文档 §8「单一逻辑源」：Host/Client 必须从同一份代码+同一份数据
// 构建玩家配置，杜绝"两份看起来一样的实现"导致的不同步隐患。
// 两端都调用本工厂，配置来源统一为数据表（同种子同参数）。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>
    /// 玩家配置构建（Host/Client 共用同一份实现）。
    /// </summary>
    public static class SimConfigFactory
    {
        /// <summary>
        /// 从角色/武器数据表构建确定性玩家配置。
        /// Host 传真实 SessionId；Client 传 -1（客户端不知道他人 SessionId，输入按 EntityId 匹配）。
        /// </summary>
        public static SimPlayerConfig Build(int sessionId, int entityId, int characterId)
        {
            var config = new SimPlayerConfig
            {
                SessionId = sessionId,
                EntityId = entityId,
                CharacterId = characterId,
                StartPosition = Vector2.zero,
            };

            if (GameEntry.Data != null)
            {
                var character = GameEntry.Data.GetCharacter(characterId);
                if (character != null)
                {
                    config.MoveSpeed = character.MoveSpeed;
                    var weapon = GameEntry.Data.GetWeapon(character.DefaultWeaponId);
                    if (weapon != null)
                    {
                        config.WeaponId = weapon.Id;
                        config.WeaponName = weapon.WeaponName;
                        config.WeaponIcon = weapon.Icon;
                        config.WeaponDamage = weapon.Damage;
                        config.FireRate = weapon.FireRate;
                        config.MaxAmmo = weapon.MaxAmmo;
                        config.ReloadTime = weapon.ReloadTime;
                        config.BulletSpeed = weapon.BulletSpeed;
                        config.Spread = weapon.Spread;
                    }
                }
            }
            return config;
        }
    }
}
