//------------------------------------------------------------
// EmojiWar GameMain - 武器配置（ScriptableObject，单一事实源）
// 替代旧 Weapon.txt DataTable。弹药/装弹/散射已取消（2026-09-02）：
// 保留 MaxAmmo/ReloadTime 字段仅为兼容历史数据，逻辑上不使用（无限释放）；
// 无 Spread 字段（无散射）。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 武器配置资产。
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon_", menuName = "EmojiWar/Data/Weapon")]
    public sealed class WeaponSO : ScriptableObject
    {
        [Header("基础")]
        public int Id = 0;                 // 武器 ID（表主键）
        public string WeaponName = "";     // 武器名
        public string Description = "";    // 描述
        public GameObject Prefab = null;   // 预制体引用（资产引用，替代旧 PrefabPath 字符串）
        public string Category = "远程";   // 分类（远程/近战/防具）
        public int Rarity = 1;             // 稀有度 1-5
        public float Weight = 1f;          // 商店抽取权重

        [Header("战斗数值")]
        public float Damage = 10f;         // 伤害
        public float FireRate = 3f;        // 每秒射速
        public float Range = 10f;          // 射程（展示）
        public float BulletSpeed = 15f;    // 子弹速度
        public int MaxAmmo = 30;           // 已废弃（无限弹药），仅兼容历史
        public float ReloadTime = 2f;      // 已废弃（无限弹药），仅兼容历史

        [Header("展示")]
        public Sprite IconSprite = null;   // 武器图标（资产引用；替代旧 Icon 字符串）


    }
}
