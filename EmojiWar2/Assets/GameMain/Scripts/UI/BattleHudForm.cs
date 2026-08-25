//------------------------------------------------------------
// EmojiWar GameMain - 战斗 HUD 界面
// 显示：金币、波次、玩家血量。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 战斗 HUD：局内状态显示。
    /// </summary>
    public class BattleHudForm : UGuiForm
    {
        [SerializeField]
        private Text m_CoinText = null;

        [SerializeField]
        private Text m_WaveText = null;

        [SerializeField]
        private Text m_HpText = null;

        [SerializeField]
        private Text m_WeaponText = null;   // 当前武器名

        [SerializeField]
        private Text m_AmmoText = null;     // 弹药 / 装弹状态

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
            // 每帧刷新波次与血量（简化）
            RefreshWave();
            RefreshHp();

            // 武器信息低频刷新（弹药/装弹）
            m_WeaponRefreshTimer -= Time.deltaTime;
            if (m_WeaponRefreshTimer <= 0f)
            {
                m_WeaponRefreshTimer = 0.2f;
                RefreshWeaponInfo();
            }
        }

        /// <summary>刷新当前武器信息（名称 + 弹药 + 装弹状态）。</summary>
        private void RefreshWeaponInfo()
        {
            if (m_WeaponText == null && m_AmmoText == null)
            {
                return;
            }

            var player = UnityEngine.Object.FindObjectOfType<Entity.PlayerEntity>();
            var weapon = player != null ? player.PrimaryWeapon : null;

            if (m_WeaponText != null)
            {
                m_WeaponText.text = "武器：" + (weapon != null ? weapon.WeaponName : "无");
            }

            if (m_AmmoText != null)
            {
                if (weapon != null)
                {
                    m_AmmoText.text = weapon.IsReloading
                        ? "装弹中..."
                        : string.Format("弹药 {0}/{1}", weapon.CurrentAmmo, weapon.MaxAmmo);
                }
                else
                {
                    m_AmmoText.text = "";
                }
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
            var session = Battle.RunSession.Instance;
            m_WaveText.text = "波次：" + (session != null ? session.WaveIndex : 0);
        }

        private void RefreshHp()
        {
            if (m_HpText == null)
            {
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
