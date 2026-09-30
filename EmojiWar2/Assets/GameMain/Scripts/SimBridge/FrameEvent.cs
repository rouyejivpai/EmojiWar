//------------------------------------------------------------
// EmojiWar GameMain - 帧事件的**应用实现**（W-16 / W-04 拆分）
//
// [W-04] 这里放"要用引擎侧数据服务"的那一半：`WeaponUpdate` 需要 `Data.ConfigService.GetWeapon(id)`
// 把 Id 查成武器数值。纯数据部分（`FrameEvent` / `FrameEventKinds`）在 `Simulation/FrameEvent.cs`。
//
// 为什么应用逻辑只有一份：网络层（装配/消费某帧时）与回放器（重放同一帧时）**共用**它 ——
// 两边各写一份"怎么应用"必然会漂移，而回放对拍是确定性回归网的地基。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>帧事件的唯一应用实现（网络层与回放器共用）。</summary>
    public static class SimFrameEvents
    {
        /// <summary>
        /// 应用一条帧事件。**必须在目标帧的 Tick 之前调用**（两端同帧 → 同结果）。
        /// <paramref name="note"/> 是给探针用的一句话说明；返回 false 表示事件被忽略（数据缺失等）。
        /// </summary>
        public static bool Apply(LockstepSimulation sim, in FrameEvent e, out string note)
        {
            note = null;
            if (sim == null) { note = "模拟为空"; return false; }

            switch (e.Kind)
            {
                case FrameEventKinds.ShopContinue:
                    // 入队 → 在 Tick 开头的 ApplyPendingCommands 里被消费（即"本帧生效"）
                    sim.EnqueueCommand(LockstepSimulation.SimCommandKind.NextWave);
                    note = "ShopContinue → 下一波（本帧生效）";
                    return true;

                case FrameEventKinds.SetCharacter:
                    sim.ApplyCharacter(e.Arg0, e.Arg1);
                    note = "SetCharacter entity=" + e.Arg0 + " char=" + e.Arg1;
                    return true;

                case FrameEventKinds.WeaponUpdate:
                {
                    var weapon = Data.ConfigService.GetWeapon(e.Arg1);
                    if (weapon == null)
                    {
                        note = "WeaponUpdate 找不到武器 id=" + e.Arg1 + "（已忽略）";
                        return false;
                    }
                    var cfg = new SimPlayerConfig
                    {
                        WeaponId = weapon.Id,
                        WeaponName = weapon.WeaponName,
                        WeaponDamage = weapon.Damage,
                        FireRate = weapon.FireRate,
                        BulletSpeed = weapon.BulletSpeed,
                    };
                    sim.ApplyWeapon(e.Arg0, cfg);
                    note = "WeaponUpdate entity=" + e.Arg0 + " weapon=" + weapon.Id
                        + " dmg=" + weapon.Damage.ToString("F1") + " rate=" + weapon.FireRate.ToString("F2");
                    return true;
                }
            }

            note = "未知帧事件 kind=" + e.Kind + "（已忽略）";
            return false;
        }
    }
}