//------------------------------------------------------------
// EmojiWar GameMain - 游戏结束界面
// 显示：波次/金币结算 + 重新开始/返回菜单。
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>重新开始事件。</summary>
    public static class GameOverEvents
    {
        public static event Action OnRestartRequested;
        public static event Action OnMenuRequested;

        public static void RequestRestart() { OnRestartRequested?.Invoke(); }
        public static void RequestMenu() { OnMenuRequested?.Invoke(); }
    }

    /// <summary>
    /// 游戏结束界面：结算展示。
    /// </summary>
    public class GameOverForm : UGuiForm
    {
        [SerializeField]
        private Text m_TitleText = null;

        [SerializeField]
        private Text m_StatsText = null;

        [SerializeField]
        private Button m_RestartButton = null;

        [SerializeField]
        private Button m_MenuButton = null;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 结算信息（网络模式从确定性模拟读取波次）
            int wave = 0;
            int coin = 0;
            var sim = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            if (sim != null)
            {
                wave = sim.WaveIndex;
            }
            else
            {
                var session = Battle.RunSession.Instance;
                wave = session != null ? session.WaveIndex : 0;
                coin = session != null ? session.Coin : 0;
            }

            if (m_TitleText != null)
            {
                m_TitleText.text = "游戏结束";
            }

            if (m_StatsText != null)
            {
                m_StatsText.text = string.Format("坚持到第 {0} 波\n金币 {1}", wave, coin);
            }

            if (m_RestartButton != null)
            {
                // 多人语义：重新开始 = 返回房间，全部准备后开始下一局
                var label = m_RestartButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = "返回房间";
                }
                m_RestartButton.onClick.RemoveAllListeners();
                m_RestartButton.onClick.AddListener(() => GameOverEvents.RequestRestart());
            }

            if (m_MenuButton != null)
            {
                m_MenuButton.onClick.RemoveAllListeners();
                m_MenuButton.onClick.AddListener(() => GameOverEvents.RequestMenu());
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }
    }
}
