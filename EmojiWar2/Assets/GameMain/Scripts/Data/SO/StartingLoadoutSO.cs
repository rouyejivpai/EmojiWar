//------------------------------------------------------------
// EmojiWar GameMain - 初始装备配置（StartingLoadoutSO，D32）
//
// 依据：doc/法术编程系统-执行文档.md §8.1「初始装备（杖 Id + 预填法术）」
//   现状 ❌：ItemSystem.GrantStartingLoadout 里写死 201/202 与 {1,2,5,8}。
//   本资产替换该硬编码：初始两手杖 + 每手预填法术 + 背包初始物品卡。
//
// 出厂装填口径（执行文档 P8 已定）：
//   预填**只在初始装备时发生**；换杖 / 买杖不按 DefaultSpellIds 补齐空槽 → 空槽 = 少一段序列。
// 引用的法术/法杖**一律用资产引用**（工程约定 §二），不允许写 Id 字符串。
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>一手初始装备：法杖 + 预填到该手法术槽的法术序列（按顺序）。</summary>
    [Serializable]
    public struct StartingHand
    {
        [Tooltip("该手初始法杖（资产引用；留空 = 该手空手，右键不开火）")]
        public WandSO Wand;
        [Tooltip("预填到该手法术槽的法术序列（按顺序，长度不超过法杖槽数；留空槽 = 少一段序列）")]
        public SpellSO[] Spells;
    }

    /// <summary>初始装备配置。</summary>
    [CreateAssetMenu(fileName = "StartingLoadout", menuName = "EmojiWar/Data/StartingLoadout")]
    public sealed class StartingLoadoutSO : ScriptableObject
    {
        [Header("两手初始法杖与预填法术")]
        [Tooltip("左手 = 主武器（左键）")]
        public StartingHand LeftHand;
        [Tooltip("右手 = 副武器（右键）")]
        public StartingHand RightHand;

        [Header("背包初始物品卡")]
        [Tooltip("开局放进背包的物品卡（资产引用，按顺序添加）")]
        public ItemSO[] BackpackItems;

        [Header("初始金币（0=沿用角色表 Coin）")]
        public int StartingCoin = 0;
    }
}
