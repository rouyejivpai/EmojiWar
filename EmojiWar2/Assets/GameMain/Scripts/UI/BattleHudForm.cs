//------------------------------------------------------------
// EmojiWar GameMain - 战斗 HUD 界面
// 显示：金币、波次、玩家血量、武器栏（图标/名称/属性/弹药/冷却进度条/Mod 槽）。
// 参考旧版 WeaponPanel（武器名+图标+Mod 槽）与 SimpleWeaponPanel（射击冷却进度条）。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 战斗 HUD：局内状态显示（含完整武器栏）。
    /// </summary>
    public class BattleHudForm : UGuiForm
    {
        [SerializeField]
        private Text m_CoinText = null;

        [SerializeField]
        private Text m_WaveText = null;

        [SerializeField]
        private Text m_HpText = null;

        // ---- 武器栏 ----
        [SerializeField]
        private Image m_WeaponIconImage = null;    // 武器图标（emoji）

        [SerializeField]
        private Text m_WeaponText = null;          // 当前武器名

        [SerializeField]
        private Text m_AmmoText = null;            // 弹药 / 装弹状态

        [SerializeField]
        private Text m_WeaponStatsText = null;     // 武器属性行（伤害/射速/散射）

        [SerializeField]
        private Image m_FireProgressFill = null;   // 射击冷却进度条填充（参考旧版 SimpleWeaponPanel.progress）

        [SerializeField]
        private Text m_ModSlotsText = null;        // 已装备 Mod 槽（参考旧版 WeaponPanel.ModGrid）

        private float m_WeaponRefreshTimer = 0f;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (Battle.RunSession.Instance != null)
            {
                Battle.RunSession.Instance.OnCoinChanged += OnCoinChanged;
            }

            RefreshAll();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (Battle.RunSession.Instance != null)
            {
                Battle.RunSession.Instance.OnCoinChanged -= OnCoinChanged;
            }
            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            // 每帧刷新波次、血量与射击冷却进度条
            RefreshWave();
            RefreshHp();
            RefreshFireProgress();

            // 武器信息低频刷新（名称/弹药/属性/Mod）
            m_WeaponRefreshTimer -= Time.deltaTime;
            if (m_WeaponRefreshTimer <= 0f)
            {
                m_WeaponRefreshTimer = 0.2f;
                RefreshWeaponInfo();
            }
        }

        /// <summary>刷新射击冷却进度条（参考旧版 SimpleWeaponPanel.setProgress）。</summary>
        private void RefreshFireProgress()
        {
            if (m_FireProgressFill == null)
            {
                return;
            }

            float progress = 1f;
            var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
            if (simPlayer != null && simPlayer.FireRate > 0f)
            {
                // 网络模式：冷却进度 = 距下次开火的间隔比例
                float interval = 1f / simPlayer.FireRate;
                progress = Mathf.Clamp01(1f - simPlayer.FireCooldown / interval);
            }
            else
            {
                var player = Object.FindObjectOfType<Entity.PlayerEntity>();
                var weapon = player != null ? player.PrimaryWeapon : null;
                if (weapon != null)
                {
                    progress = weapon.GetProgress();
                }
            }

            // 进度条填充：锚定左侧，按比例缩放宽度
            var rect = m_FireProgressFill.rectTransform;
            Vector2 size = rect.sizeDelta;
            float maxWidth = rect.parent != null ? rect.parent.GetComponent<RectTransform>().sizeDelta.x : 300f;
            if (maxWidth <= 0f)
            {
                maxWidth = 300f;
            }
            size.x = maxWidth * Mathf.Clamp01(progress);
            rect.sizeDelta = size;
        }

        /// <summary>刷新当前武器信息（图标/名称/弹药/属性/Mod 槽）。网络模式从确定性模拟读取。</summary>
        private void RefreshWeaponInfo()
        {
            if (m_WeaponIconImage == null && m_WeaponText == null && m_AmmoText == null
                && m_WeaponStatsText == null && m_ModSlotsText == null)
            {
                return;
            }

            // 网络模式：本地玩家状态来自确定性模拟
            var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
            if (simPlayer != null)
            {
                if (m_WeaponIconImage != null)
                {
                    m_WeaponIconImage.sprite = Art.ArtManager.GetWeaponSprite(simPlayer.WeaponIcon);
                }
                if (m_WeaponText != null)
                {
                    m_WeaponText.text = "武器：" + simPlayer.WeaponName;
                }
                if (m_AmmoText != null)
                {
                    m_AmmoText.text = simPlayer.IsReloading
                        ? "装弹中..."
                        : string.Format("弹药 {0}/{1}", simPlayer.Ammo, simPlayer.MaxAmmo);
                }
                if (m_WeaponStatsText != null)
                {
                    m_WeaponStatsText.text = string.Format("伤害 {0:F0} · 射速 {1:F1}/s{2}",
                        simPlayer.WeaponDamage, simPlayer.FireRate,
                        simPlayer.Spread > 0f ? string.Format(" · 散射 {0:F2}", simPlayer.Spread) : "");
                }
                if (m_ModSlotsText != null)
                {
                    // 网络模式：Mod 由局内背包携带（确定性模拟暂不模拟 Mod 装备）
                    var session = Battle.RunSession.Instance;
                    int modCount = session != null ? session.ModBag.Count : 0;
                    m_ModSlotsText.text = modCount > 0
                        ? string.Format("Mod 槽：已装备 {0} 个", modCount)
                        : "Mod 槽：无";
                }
                return;
            }

            // 单机模式：本地 PlayerEntity
            var player = UnityEngine.Object.FindObjectOfType<Entity.PlayerEntity>();
            var weaponBase = player != null ? player.PrimaryWeapon : null;

            if (m_WeaponIconImage != null)
            {
                var character = player != null && player.CharacterId > 0 && GameEntry.Data != null
                    ? GameEntry.Data.GetCharacter(player.CharacterId)
                    : null;
                var weaponRow = character != null && GameEntry.Data != null
                    ? GameEntry.Data.GetWeapon(character.DefaultWeaponId)
                    : null;
                m_WeaponIconImage.sprite = Art.ArtManager.GetWeaponSprite(weaponRow != null ? weaponRow.Icon : null);
            }

            if (m_WeaponText != null)
            {
                m_WeaponText.text = "武器：" + (weaponBase != null ? weaponBase.WeaponName : "无");
            }

            if (m_AmmoText != null)
            {
                if (weaponBase != null)
                {
                    m_AmmoText.text = weaponBase.IsReloading
                        ? "装弹中..."
                        : string.Format("弹药 {0}/{1}", weaponBase.CurrentAmmo, weaponBase.MaxAmmo);
                }
                else
                {
                    m_AmmoText.text = "";
                }
            }

            if (m_WeaponStatsText != null && weaponBase != null)
            {
                m_WeaponStatsText.text = string.Format("伤害 {0:F0} · 射速 {1:F1}/s",
                    weaponBase.Damage, weaponBase.FireRate);
            }

            if (m_ModSlotsText != null && weaponBase != null && weaponBase.ModComponent != null)
            {
                int modCount = weaponBase.ModComponent.ModCount;
                m_ModSlotsText.text = modCount > 0
                    ? string.Format("Mod 槽：{0} 个", modCount)
                    : "Mod 槽：无";
            }
            else if (m_ModSlotsText != null)
            {
                m_ModSlotsText.text = "Mod 槽：无";
            }
        }

        private void OnCoinChanged(int coin)
        {
            if (m_CoinText != null)
            {
                m_CoinText.text = "金币：" + coin;
            }
        }

        private void RefreshAll()
        {
            RefreshCoin();
            RefreshWave();
            RefreshHp();
            RefreshWeaponInfo();
        }

        private void RefreshCoin()
        {
            if (m_CoinText == null)
            {
                return;
            }
            var session = Battle.RunSession.Instance;
            m_CoinText.text = "金币：" + (session != null ? session.Coin : 0);
        }

        private void RefreshWave()
        {
            if (m_WaveText == null)
            {
                return;
            }
            // 网络模式：波次来自确定性模拟
            var sim = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            if (sim != null)
            {
                m_WaveText.text = "波次：" + sim.WaveIndex;
                return;
            }
            var session = Battle.RunSession.Instance;
            m_WaveText.text = "波次：" + (session != null ? session.WaveIndex : 0);
        }

        private void RefreshHp()
        {
            if (m_HpText == null)
            {
                return;
            }

            // 网络模式：本地玩家 HP 来自确定性模拟
            var simPlayer = GameEntry.SimView != null ? GameEntry.SimView.GetLocalPlayer() : null;
            if (simPlayer != null)
            {
                m_HpText.text = string.Format("HP: {0:F0}/100", simPlayer.Hp);
                return;
            }

            var player = Object.FindObjectOfType<Entity.PlayerEntity>();
            if (player != null && player.Data != null)
            {
                m_HpText.text = string.Format("HP: {0:F0}/{1:F0}", player.CurrentHealth, player.Data.MaxHealth);
            }
            else
            {
                m_HpText.text = "HP: --";
            }
        }
    }
}
