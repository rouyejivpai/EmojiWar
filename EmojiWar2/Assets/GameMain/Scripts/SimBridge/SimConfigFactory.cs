//------------------------------------------------------------
// EmojiWar GameMain - 确定性玩家配置工厂（单一逻辑源）
//
// 文档 §8「单一逻辑源」：Host/Client 必须从同一份代码+同一份数据
// 构建玩家配置，杜绝"两份看起来一样的实现"导致的不同步隐患。
//
// W-06 改造（报告 A10 / 根因 4，2026-09-28）：
//   旧做法：`Build` 里读**本机** ItemSystem 编译 loadout，而 `NetHostLogic.BuildPlayerConfig`
//           用它**为所有玩家**构建 → Host 眼里每个玩家都用的是 **Host 自己的**杖/法术序列，
//           每个客户端眼里所有人用的都是**它自己的** → 任何玩家动过背包就多端分叉。
//   新做法：`BuildFromIds` —— 用**玩家自己上报的装备 Id** 编译（联机路径唯一入口）。
//           各玩家上报自己的 Id → Host 转发 → 所有端（含玩家自己）用同一份 Id 编译
//           ⇒ 编译输入一致 ⇒ CastProgram 逐位一致。
//   注意：这里的前提是"**没有背包同步**，Host 不知道别人装了什么"，所以数据必须由玩家上报。
//
//   `Build`（读本机背包）**只保留给离线单机** —— 那里"本机背包"就是唯一真相。
//   联机路径一旦调用 `Build`，就重新引入了分叉源。
//------------------------------------------------------------

using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>
    /// 玩家配置构建（Host/Client 共用同一份实现）。
    /// </summary>
    public static class SimConfigFactory
    {
        /// <summary>
        /// 角色/武器等"同资产即同值"的基础属性（**不含 loadout**）。
        /// Host 传真实 SessionId；Client 传 -1（客户端不知道他人 SessionId，输入按 EntityId 匹配）。
        /// </summary>
        public static SimPlayerConfig BuildBase(int sessionId, int entityId, int characterId)
        {
            var config = new SimPlayerConfig
            {
                SessionId = sessionId,
                EntityId = entityId,
                CharacterId = characterId,
                StartPosition = SimVec2.zero,
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
                }
            }
            return config;
        }

        /// <summary>
        /// 【联机路径唯一入口，W-06】用**玩家上报的装备 Id** 构建配置。
        ///
        /// 双手法杖 loadout（决策：法术槽属于"手"；右手空手 = 没有副武器、右键不开火）
        /// 由 <see cref="LoadoutCompiler.CompileFromIds"/> 从 Id 编译 —— 不读任何背包。
        /// </summary>
        public static SimPlayerConfig BuildFromIds(int sessionId, int entityId, int characterId, PlayerLoadoutIds ids)
        {
            var config = BuildBase(sessionId, entityId, characterId);

            config.PrimaryProgram = LoadoutCompiler.CompileFromIds(
                ids.WandLeftItemId, ids.LeftSpellItemIds, ConfigItemTable.Instance);
            config.SecondaryProgram = LoadoutCompiler.CompileFromIds(
                ids.WandRightItemId, ids.RightSpellItemIds, ConfigItemTable.Instance);

            return config;
        }

        /// <summary>
        /// 【离线单机路径】按**本机背包**编译 loadout。
        /// ⚠️ **不要在联机路径调用**：各端按各自背包编译正是报告 A10 的分叉源。
        /// </summary>
        public static SimPlayerConfig Build(int sessionId, int entityId, int characterId)
        {
            var config = BuildBase(sessionId, entityId, characterId);

            var itemService = EmojiWar.GameMain.ItemSystem.Service;
            if (itemService != null)
            {
                config.PrimaryProgram = LoadoutCompiler.CompileHand(
                    EmojiWar.GameMain.ItemSystem.HandLeftId, itemService.Table, itemService);
                config.SecondaryProgram = LoadoutCompiler.CompileHand(
                    EmojiWar.GameMain.ItemSystem.HandRightId, itemService.Table, itemService);
            }
            return config;
        }

        /// <summary>
        /// 读取**本机**双手的装备 Id（W-06：上报给 Host 用）。
        /// 空手/无服务时相应字段为 0 / 空数组（= 空手，各端一致）。
        /// </summary>
        public static PlayerLoadoutIds ReadLocalLoadoutIds(int entityId)
        {
            var ids = new PlayerLoadoutIds { EntityId = entityId };

            var svc = EmojiWar.GameMain.ItemSystem.Service;
            if (svc == null) { return ids; }

            int wandItemId;
            int[] slotItemIds;

            if (LoadoutCompiler.ReadHandIds(EmojiWar.GameMain.ItemSystem.HandLeftId, svc.Table, svc,
                    out wandItemId, out slotItemIds))
            {
                ids.WandLeftItemId = wandItemId;
                ids.LeftSpellItemIds = slotItemIds;
            }

            if (LoadoutCompiler.ReadHandIds(EmojiWar.GameMain.ItemSystem.HandRightId, svc.Table, svc,
                    out wandItemId, out slotItemIds))
            {
                ids.WandRightItemId = wandItemId;
                ids.RightSpellItemIds = slotItemIds;
            }

            return ids;
        }
    }
}
