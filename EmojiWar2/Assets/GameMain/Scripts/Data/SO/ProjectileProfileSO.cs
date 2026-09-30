//------------------------------------------------------------
// EmojiWar GameMain - 弹道剖面配置（ProjectileProfile）
//
// 方案：doc/物品与法杖系统设计.md（v1.0 冻结）§2
//   纯数值、无资产引用以外的东西：法术引用它，LoadoutCompiler 把它编译进 CastProgram，
//   模拟层只吃编译后的数值（SO 不进 sim、不进网络）。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>弹道剖面（子弹的基础数值与行为参数）。</summary>
    [CreateAssetMenu(fileName = "Proj_", menuName = "EmojiWar/Data/ProjectileProfile")]
    public sealed class ProjectileProfileSO : ScriptableObject
    {
        [Header("基础")]
        public int Id = 0;                 // 主键（法术按 Id 引用，编译进 CastProgram）
        public string ProfileKey = "";     // 稳定标识（"proj_fireball"）

        [Header("基础数值")]
        public float Speed = 12f;          // 速度（单位/秒）
        public float Damage = 10f;         // 伤害
        public float Lifetime = 2f;        // 存活时间（秒）
        public float Radius = 0.22f;       // 碰撞半径

        [Header("发射形态")]
        public int Count = 1;              // 一次发射数量（>1 自动扇形）
        public float SpreadDeg = 0f;       // 扇形总角度

        [Header("行为")]
        public int Pierce = 0;             // 穿透次数（命中后继续飞）
        public float Homing = 0f;          // 追踪强度（0=不追踪）
    }
}
