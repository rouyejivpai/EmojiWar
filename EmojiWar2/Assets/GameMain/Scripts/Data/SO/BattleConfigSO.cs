using EmojiWar.GameMain.Simulation;
//------------------------------------------------------------
// EmojiWar GameMain - 战斗/波次参数（ScriptableObject）
// 替代 BattleManager/NetHostLogic 里的 m_EnemiesPerWave 等零散字段与
// GameEntry.Config("Battle.*") 键值：统一一处调整。
// 注意：这些数值会被注入确定性模拟（Host/Client 同资产同值 → 双端一致）。
///------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 战斗与波次平衡参数资产。
    /// </summary>
    [CreateAssetMenu(fileName = "BattleConfig", menuName = "EmojiWar/Data/Battle Config")]
public sealed class BattleConfigSO : ScriptableObject, ISimBattleConfig
    {
        // [W-04] 显式接口实现（字段保持 public → Unity 序列化与公开 API 均不变）
        int ISimBattleConfig.EnemiesPerWaveBase { get { return EnemiesPerWaveBase; } }
        int ISimBattleConfig.EnemiesPerWaveGrowth { get { return EnemiesPerWaveGrowth; } }
        float ISimBattleConfig.SpawnRadius { get { return SpawnRadius; } }
        float ISimBattleConfig.EnemySpawnInterval { get { return EnemySpawnInterval; } }
        float ISimBattleConfig.EnemyBaseHp { get { return EnemyBaseHp; } }
        float ISimBattleConfig.EnemyHpPerWave { get { return EnemyHpPerWave; } }
        float ISimBattleConfig.EnemyBaseSpeed { get { return EnemyBaseSpeed; } }
        float ISimBattleConfig.EnemySpeedPerWave { get { return EnemySpeedPerWave; } }
        float ISimBattleConfig.ShopDuration { get { return ShopDuration; } }
        int ISimBattleConfig.ShopItemCount { get { return ShopItemCount; } }
        int ISimBattleConfig.WeaponPrice { get { return WeaponPrice; } }
        int ISimBattleConfig.ModPrice { get { return ModPrice; } }

        [Header("波次敌人数量")]
        [Tooltip("第 1 波敌人数量")]
        public int EnemiesPerWaveBase = 3;
        [Tooltip("每波敌人数量递增")]
        public int EnemiesPerWaveGrowth = 2;
        [Tooltip("敌人生成半径（环绕玩家）")]
        public float SpawnRadius = 8f;
        [Tooltip("敌人出生间隔（秒）")]
        public float EnemySpawnInterval = 0.5f;

        [Header("敌人成长")]
        [Tooltip("第 1 波敌人 HP")]
        public float EnemyBaseHp = 30f;
        [Tooltip("每波敌人 HP 递增")]
        public float EnemyHpPerWave = 5f;
        [Tooltip("第 1 波敌人移速")]
        public float EnemyBaseSpeed = 2.5f;
        [Tooltip("每波敌人移速递增")]
        public float EnemySpeedPerWave = 0.3f;

        [Header("商店")]
        [Tooltip("波间商店持续时间（秒）")]
        public float ShopDuration = 8f;
        [Tooltip("商店商品数")]
        public int ShopItemCount = 3;
        [Tooltip("商店武器价格")]
        public int WeaponPrice = 80;
        [Tooltip("商店 Mod 价格")]
        public int ModPrice = 60;
    }
}
