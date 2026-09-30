//------------------------------------------------------------
// EmojiWar GameMain - 法杖配置（Wand）
//
// 方案：doc/物品与法杖系统设计.md（v1.0 冻结）§4
//   法杖 = 带"程序槽"的武器：槽内放法术（实体卡），按顺序解释执行；
//   魔力/充能是**真实硬限制**（魔力不足卡手、槽位游走完进入充能）。
//   全部武器统一成法杖（本轮决策 #16）。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{

    /// <summary>法杖配置。</summary>
    [CreateAssetMenu(fileName = "Wand_", menuName = "EmojiWar/Data/Wand")]
    public sealed class WandSO : ScriptableObject
    {
        [Header("基础")]
        public int Id = 0;
        public string WandKey = "";        // "wand_apprentice"
        public string DisplayName = "";
        [TextArea] public string Description = "";
        public Sprite IconSprite = null;   // 资产引用

        [Header("槽位与节奏")]
        public int SlotCount = 3;          // 法术槽数量（1..8）

        // P2b=A 定稿（执行文档 §7.2）：**序列内相邻触发间隔 = 该物品的施法延迟 + 法杖基础施法延迟**。
        // CastDelay 是**加值**，每次序列时长计算都要参与（不是"低于某值才生效"）。
        // 出厂默认 学徒 0.12 / 镜像 0.08 / 秘银重杖 0.06（设计文档 §12.1）。
        public float CastDelay = 0.12f;    // 法杖基础施法延迟（秒；参与序列内节奏）
        public float RechargeTime = 1.0f;  // 槽位走完后的充能时间（秒）

        [Header("魔力")]
        public int ManaMax = 100;
        public float ManaRegen = 20f;      // 每秒回复

        [Header("施法模式 / 出厂装填")]
        public CastMode Mode = CastMode.Sequential;
        public int[] DefaultSpellIds = new int[0];
    }
}
