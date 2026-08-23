//------------------------------------------------------------
// EmojiWar GameMain - OnKill 效果验证工具（Editor）
// 菜单：EmojiWar/Diagnostics/Test OnKill Mod
// 验证：武器挂载 OnKill Mod → NotifyKill → 临时攻速加成生效
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// OnKill Mod 效果测试。
    /// </summary>
    public static class OnKillTest
    {
        [MenuItem("EmojiWar/Diagnostics/Test OnKill Mod")]
        public static void TestOnKill()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[OnKill] 请先进入 Play 模式");
                return;
            }

            // 构造武器挂载点
            var weaponGo = new GameObject("TestWeapon");
            var weapon = weaponGo.AddComponent<Weapon.RangedWeapon>();
            var modComp = weapon.ModComponent;   // WeaponBase.Awake 自动创建

            // 读取 OnKill Mod 数据（id=3 杀戮怒火）
            var modRow = GameEntry.Data.GetMod(3);
            if (modRow == null)
            {
                Debug.LogError("[OnKill] Mod 3 (杀戮怒火) 数据不存在");
                Object.Destroy(weaponGo);
                return;
            }

            modComp.AddMod(modRow);
            Debug.Log("[OnKill] 已挂载: " + modRow.ModName + " effect=" + modRow.EffectType);

            // 触发击杀
            modComp.NotifyKill();
            bool bonusActive = modComp.HasKillBonus;

            // 检查攻速修正
            var (add, mul) = modComp.GetFireRateModifiers();
            Debug.Log(string.Format("[OnKill] 击杀后 bonusActive={0} mul={1:F2}", bonusActive, mul));

            if (bonusActive && mul > 0f)
            {
                Debug.Log("===== [OnKill] 杀戮怒火效果验证通过 ✅ 击杀触发临时攻速加成 =====");
            }
            else
            {
                Debug.LogWarning("[OnKill] 效果未生效");
            }

            Object.Destroy(weaponGo);
        }
    }
}
