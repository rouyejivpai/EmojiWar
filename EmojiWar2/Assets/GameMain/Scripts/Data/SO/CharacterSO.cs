//------------------------------------------------------------
// EmojiWar GameMain - 角色配置（ScriptableObject，单一事实源）
// 替代旧 Character.txt DataTable：Inspector 直接编辑，Resources/Data 下每行一资产。
// 字段名与原 DRCharacter 属性一致，调用点（GameEntry.Data.GetCharacter(id).XXX）无需改名。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 角色配置资产。
    /// </summary>
    [CreateAssetMenu(fileName = "Character_", menuName = "EmojiWar/Data/Character")]
    public sealed class CharacterSO : ScriptableObject
    {
        [Header("基础")]
        public int Id = 0;                // 角色 ID（表主键，全表唯一）
        public string CharacterId = "";   // 角色唯一标识（"player1"）
        public string CharacterName = ""; // 显示名（"流汗黄豆"）
        public string Description = "";   // 描述
        public GameObject Prefab = null;  // 预制体引用（资产引用，替代旧 PrefabPath 字符串）

        [Header("默认装备")]
        public int DefaultWeaponId = 1;   // 主武器 ID
        public int SecondWeaponId = 0;    // 副武器 ID

        [Header("属性")]
        public int MaxHealth = 150;       // 最大生命
        public float MoveSpeed = 5f;      // 移动速度
        public int Coin = 300;            // 初始金币

        [Header("展示")]
        public Sprite IconSprite = null;  // 角色图标（资产引用；替代旧 Icon 字符串码点）


    }
}
