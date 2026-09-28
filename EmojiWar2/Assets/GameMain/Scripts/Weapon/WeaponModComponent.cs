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
        Spread = 5,         // 散射（一次多发）
        DamageUp = 6,       // 伤害提升
    }

    /// <summary>
    /// 已装备的 Mod 实例。
    /// </summary>
    public sealed class EquippedMod
    {
        public Data.ModSO Row;
    }

    /// <summary>
    /// 武器 Mod 挂载组件。
    /// </summary>
    public class WeaponModComponent : MonoBehaviour
    {
        private readonly List<EquippedMod> m_Mods = new List<EquippedMod>();

        // OnKill 临时攻速加成状态
        private float m_KillFireRateBonus = 0f;
        private float m_KillBonusRemaining = 0f;
        private float m_KillBonusDuration = 3f;

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
        public void AddMod(Data.ModSO row)
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
        public void RemoveMod(Data.ModSO row)
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

            // OnKill 临时攻速加成
            if (m_KillFireRateBonus > 0f && m_KillBonusRemaining > 0f)
            {
                mul += m_KillFireRateBonus;
            }

            return (add, mul);
        }

        private void Update()
        {
            // 推进 OnKill 临时加成计时
            if (m_KillBonusRemaining > 0f)
            {
                m_KillBonusRemaining -= Time.deltaTime;
                if (m_KillBonusRemaining <= 0f)
                {
                    m_KillFireRateBonus = 0f;
                }
            }
        }

        /// <summary>
        /// 触发击杀事件（由 Projectile 在击杀敌人时调用）。
        /// 拥有 OnKill Mod 时获得临时攻速加成。
        /// </summary>
        public void NotifyKill()
        {
            foreach (var mod in m_Mods)
            {
                if (mod.Row != null && GetEffectType(mod.Row.EffectType) == ModEffectType.OnKill)
                {
                    // Param1 = 攻速加成比例, Param2 = 持续秒数
                    m_KillFireRateBonus = mod.Row.Param1;
                    m_KillBonusDuration = mod.Row.Param2 > 0f ? mod.Row.Param2 : 3f;
                    m_KillBonusRemaining = m_KillBonusDuration;
                    return;
                }
            }
        }

        /// <summary>是否正在享受 OnKill 加成。</summary>
        public bool HasKillBonus
        {
            get { return m_KillFireRateBonus > 0f && m_KillBonusRemaining > 0f; }
        }

        /// <summary>
        /// 计算伤害修正（累加）。
        /// </summary>
        public float GetDamageModifier()
        {
            float add = 0f;
            foreach (var mod in m_Mods)
            {
                if (mod.Row != null && GetEffectType(mod.Row.EffectType) == ModEffectType.DamageUp)
                {
                    add += mod.Row.Param1;
                }
            }
            return add;
        }

        /// <summary>
        /// 获取散射弹数（无散射 Mod 时返回 1）。
        /// </summary>
        public int GetSpreadCount()
        {
            foreach (var mod in m_Mods)
            {
                if (mod.Row != null && GetEffectType(mod.Row.EffectType) == ModEffectType.Spread)
                {
                    return Mathf.Max(1, (int)mod.Row.Param1);
                }
            }
            return 1;
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
                case "Spread": return ModEffectType.Spread;
                case "DamageUp": return ModEffectType.DamageUp;
                default: return ModEffectType.None;
            }
        }
    }
}
