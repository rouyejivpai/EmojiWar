//------------------------------------------------------------
// EmojiWar GameMain - 商店管理器
// 波间商店：按权重随机抽取武器/Mod 商品，金币购买。
// 为多人 PvE 预留：商品列表后续由服务器权威生成。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Shop
{
    /// <summary>
    /// 商店商品类型。
    /// </summary>
    public enum ShopItemType
    {
        Weapon = 0,
        Mod = 1,
    }

    /// <summary>
    /// 商店商品。
    /// </summary>
    public sealed class ShopItem
    {
        public ShopItemType Type;
        public int DataId;      // 武器 ID 或 Mod ID
        public string Name;
        public string Description;
        public int Price;

        public ShopItem(ShopItemType type, int dataId, string name, string description, int price)
        {
            Type = type;
            DataId = dataId;
            Name = name;
            Description = description;
            Price = price;
        }
    }

    /// <summary>
    /// 商店管理器：生成商品、购买处理。
    /// </summary>
    public static class ShopManager
    {
        public const int WeaponPrice = 80;
        public const int ModPrice = 60;

        /// <summary>
        /// 生成商店商品列表（权重随机）。
        /// </summary>
        public static List<ShopItem> GenerateOfferings(int count)
        {
            var items = new List<ShopItem>();
            var data = GameEntry.Data;
            if (data == null)
            {
                return items;
            }

            for (int i = 0; i < count; i++)
            {
                // 50% 武器 / 50% Mod
                if (Random.value < 0.5f)
                {
                    var weapon = RollWeapon();
                    if (weapon != null)
                    {
                        items.Add(new ShopItem(ShopItemType.Weapon, weapon.Id, weapon.WeaponName, weapon.Description, WeaponPrice));
                    }
                }
                else
                {
                    var mod = RollMod();
                    if (mod != null)
                    {
                        items.Add(new ShopItem(ShopItemType.Mod, mod.Id, mod.ModName, mod.Description, ModPrice));
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// 权重随机抽取武器。
        /// </summary>
        private static Data.DRWeapon RollWeapon()
        {
            var weapons = new List<Data.DRWeapon>();
            var table = GameEntry.DataTable != null
                ? GameEntry.DataTable.GetDataTable<Data.DRWeapon>("Weapon")
                : null;
            if (table == null)
            {
                return null;
            }
            table.GetAllDataRows(weapons);

            return WeightedPick(weapons, w => w.Weight);
        }

        /// <summary>
        /// 权重随机抽取 Mod。
        /// </summary>
        private static Data.DRMod RollMod()
        {
            var mods = GameEntry.Data.GetAllMods();
            return WeightedPick(mods, m => m.Weight);
        }

        private static T WeightedPick<T>(List<T> items, System.Func<T, float> weightSelector) where T : class
        {
            if (items == null || items.Count == 0)
            {
                return null;
            }

            float totalWeight = 0f;
            foreach (var item in items)
            {
                totalWeight += weightSelector(item);
            }

            if (totalWeight <= 0f)
            {
                return items[0];
            }

            float roll = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (var item in items)
            {
                cumulative += weightSelector(item);
                if (roll <= cumulative)
                {
                    return item;
                }
            }

            return items[items.Count - 1];
        }
    }
}
