//------------------------------------------------------------
// EmojiWar GameMain - Mod 配置（ScriptableObject，单一事实源）
// 替代旧 Mod.txt DataTable。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// Mod（模组）配置资产。
    /// </summary>
    [CreateAssetMenu(fileName = "Mod_", menuName = "EmojiWar/Data/Mod")]
    public sealed class ModSO : ScriptableObject
    {
        [Header("基础")]
        public int Id = 0;               // Mod ID（表主键）
        public string ModId = "";        // 唯一标识（"rateup_mod"）
        public string ModName = "";      // 名称
        public string Description = "";  // 描述
        public string Category = "武器"; // 分类（武器/防具/通用）
        public int Rarity = 1;           // 稀有度 1-5
        public float Weight = 1f;        // 抽取权重

        [Header("效果")]
        public string EffectType = "";   // FireRateUp/FireRateMul/OnKill/Freeze/Spread/DamageUp
        public float Param1 = 0f;        // 效果参数 1
        public float Param2 = 0f;        // 效果参数 2

        [Header("展示")]
        public Sprite IconSprite = null; // Mod 图标（资产引用；替代旧 Icon 字符串）


    }
}
