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
                        config.WeaponDamage = weapon.Damage;
                        config.FireRate = weapon.FireRate;
                        config.MaxAmmo = weapon.MaxAmmo;
                        config.ReloadTime = weapon.ReloadTime;
                        config.BulletSpeed = weapon.BulletSpeed;
                    }

                    // 双手法杖 loadout（决策：法术槽属于"手"，左手一套/右手一套；
                    // 右手空手 = 没有副武器、右键不开火；副武器数值 = 右手杖 + 右手那套法术）。
                    // 说明：本轮先按"本机 ItemSystem"编译，Host/Client 需装备一致（同机测试/单机成立）；
                    // P5 收尾时改为 Host 权威编译 + S2CLoadoutSync 下发，消除两端各自编译的分叉风险。
                    var itemService = EmojiWar.GameMain.ItemSystem.Service;
                    if (itemService != null)
                    {
                        config.PrimaryProgram = Items.LoadoutCompiler.CompileHand(
                            EmojiWar.GameMain.ItemSystem.HandLeftId, itemService.Table, itemService);
                        config.SecondaryProgram = Items.LoadoutCompiler.CompileHand(
                            EmojiWar.GameMain.ItemSystem.HandRightId, itemService.Table, itemService);
                    }
                }
            }
            return config;
        }
    }
}
