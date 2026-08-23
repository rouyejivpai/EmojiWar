//------------------------------------------------------------
// EmojiWar GameMain - 武器 Mod 挂载组件
// 管理武器上已装备的 Mod，计算叠加后的属性修正。
// 挂在武器 GameObject 上，由武器读取修正值。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Weapon
{
    /// <summary>
    /// Mod 效果类型（对应数据表 EffectType）。
    /// </summary>
    public enum ModEffectType
    {
        None = 0,
        FireRateUp = 1,     // 攻速加值
        FireRateMul = 2,    // 攻速乘区
        OnKill = 3,         // 击杀触发（攻速临时提升）
        Freeze = 4,         // 子弹减速敌人
    }

    /// <summary>
    /// 已装备的 Mod 实例。
    /// </summary>
    public sealed class EquippedMod
    {
        public Data.DRMod Row;
    }

    /// <summary>
    /// 武器 Mod 挂载组件。
    /// </summary>
    public class WeaponModComponent : MonoBehaviour
    {
        private readonly List<EquippedMod> m_Mods = new List<EquippedMod>();

        /// <summary>已装备 Mod 数量。</summary>
        public int ModCount
        {
            get { return m_Mods.Count; }
        }

        /// <summary>全部已装备 Mod（只读）。</summary>
        public IReadOnlyList<EquippedMod> Mods
        {
            get { return m_Mods; }
        }

        /// <summary>
        /// 加装 Mod。
        /// </summary>
        public void AddMod(Data.DRMod row)
        {
            if (row == null)
            {
                return;
            }

            m_Mods.Add(new EquippedMod { Row = row });
        }

        /// <summary>
        /// 卸载指定 Mod。
        /// </summary>
        public void RemoveMod(Data.DRMod row)
        {
            m_Mods.RemoveAll(m => m.Row != null && m.Row.Id == row.Id);
        }

        /// <summary>
        /// 清空所有 Mod。
        /// </summary>
        public void ClearMods()
        {
            m_Mods.Clear();
        }

        /// <summary>
        /// 计算攻速修正：返回 (addValue, mulValue)。
        /// 最终 fireRate = (base + addValue) * (1 + mulValue)
        /// </summary>
        public (float add, float mul) GetFireRateModifiers()
        {
            float add = 0f;
            float mul = 0f;

            foreach (var mod in m_Mods)
            {
                if (mod.Row == null)
                {
                    continue;
                }

                switch (GetEffectType(mod.Row.EffectType))
                {
                    case ModEffectType.FireRateUp:
                        add += mod.Row.Param1;
                        break;
                    case ModEffectType.FireRateMul:
                        mul += mod.Row.Param1;
                        break;
                }
            }

            return (add, mul);
        }

        /// <summary>
        /// 是否拥有指定效果类型的 Mod。
        /// </summary>
        public bool HasEffect(ModEffectType effectType)
        {
            foreach (var mod in m_Mods)
            {
                if (mod.Row != null && GetEffectType(mod.Row.EffectType) == effectType)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取指定效果类型的参数（取第一个命中的）。
        /// </summary>
        public float GetEffectParam(ModEffectType effectType, int paramIndex)
        {
            foreach (var mod in m_Mods)
            {
                if (mod.Row != null && GetEffectType(mod.Row.EffectType) == effectType)
                {
                    return paramIndex == 1 ? mod.Row.Param1 : mod.Row.Param2;
                }
            }
            return 0f;
        }

        /// <summary>
        /// 字符串转效果类型。
        /// </summary>
        public static ModEffectType GetEffectType(string effectType)
        {
            switch (effectType)
            {
                case "FireRateUp": return ModEffectType.FireRateUp;
                case "FireRateMul": return ModEffectType.FireRateMul;
                case "OnKill": return ModEffectType.OnKill;
                case "Freeze": return ModEffectType.Freeze;
                default: return ModEffectType.None;
            }
        }
    }
}
