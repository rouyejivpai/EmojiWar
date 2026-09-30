//------------------------------------------------------------
// EmojiWar Sim - 模拟层需要的**配置视图**（W-04）
//
// 为什么需要接口：`SpellSystemConfigSO` / `BattleConfigSO` 是 `Data` 里的 ScriptableObject
// （在 GameMain 程序集、引用 UnityEngine），而模拟层要拆成 `noEngineReferences` 的程序集 ——
// **asmdef 的边界是程序集**，所以模拟层不能直接引用这两个类型。
//
// 做法：模拟层只认下面两个接口；SO 用**显式接口实现**把它们接上（字段保持 public，
// Unity 序列化完全不变，公开 API 也不变）。
//
// 成员清单是从代码里逐条抄出来的（`config.X` / `SpellConfig.X` / `ApplyBattleConfig` 里读的字段），
// **不要凭印象增删** —— 少一个就是编译错误，多一个是没必要的耦合。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-04] 模拟层读得到的法术系统配置（由 `Data.SpellSystemConfigSO` 实现）。</summary>
    public interface ISimSpellConfig
    {
        float DefaultBulletLifetime { get; }
        float DefaultBulletRadius { get; }
        int DefaultPassiveCooldownMs { get; }
        int DefaultPassiveLimitPerCast { get; }
        float MaxBulletLifetime { get; }
        int MaxPassiveNesting { get; }
        int MaxTotalTriggers { get; }
        int MaxTriggersPerItem { get; }
        // 下面 3 个给 BuffLimits 用（FromSO 也接接口）
        int GlobalMaxBuffStacks { get; }
        int DefaultBuffMaxStacks { get; }
        int BuffDetonateDamagePerStack { get; }
    }

    /// <summary>[W-04] 模拟层读得到的战斗配置（由 `Data.BattleConfigSO` 实现；
    /// `ApplyBattleConfig` 读的就是这 12 个字段）。</summary>
    public interface ISimBattleConfig
    {
        int EnemiesPerWaveBase { get; }
        int EnemiesPerWaveGrowth { get; }
        float SpawnRadius { get; }
        float EnemySpawnInterval { get; }
        float EnemyBaseHp { get; }
        float EnemyHpPerWave { get; }
        float EnemyBaseSpeed { get; }
        float EnemySpeedPerWave { get; }
        float ShopDuration { get; }
        int ShopItemCount { get; }
        int WeaponPrice { get; }
        int ModPrice { get; }
    }

    /// <summary>
    /// [W-04] 注入点：表现层启动时把当前配置交给模拟层（`SimBridge.Install()`）。
    /// 这样模拟层既能拿到配置，又**不必**引用 `Data.ConfigService`（那是引擎侧的数据服务）。
    /// 未注入时为 null，调用方必须自己兜底（本项目已有"结构兜底值"的传统）。
    /// </summary>
    public static class SimInjectedConfig
    {
        public static ISimSpellConfig Spell;
        public static ISimBattleConfig Battle;
    }
}
