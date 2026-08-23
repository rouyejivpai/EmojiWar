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
