//------------------------------------------------------------
// EmojiWar GameMain - 商店界面
// 波间商店：展示随机商品，购买武器/Mod，"继续战斗"关闭并恢复。
// 不切换流程：由 ProcedureBattle 打开，继续按钮直接关闭并通知 BattleManager。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 商店界面：商品列表 + 购买 + 继续战斗。
    /// </summary>
    public class ShopForm : UGuiForm
    {
        [SerializeField]
        private Text m_CoinText = null;

        [SerializeField]
        private Text m_InfoText = null;

        [SerializeField]
        private Button m_ContinueButton = null;

        private readonly List<Shop.ShopItem> m_Items = new List<Shop.ShopItem>();
        private Transform m_ItemRoot = null;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 订阅金币变化
            if (Battle.RunSession.Instance != null)
            {
                Battle.RunSession.Instance.OnCoinChanged += OnCoinChanged;
            }

            // 生成商品并构建 UI
            m_Items.Clear();
            m_Items.AddRange(Shop.ShopManager.GenerateOfferings(3));
            BuildUI();
            RefreshCoin();

            if (m_InfoText != null)
            {
                m_InfoText.text = "波间商店：购买强化，继续闯关";
            }

            if (m_ContinueButton != null)
            {
                m_ContinueButton.onClick.RemoveAllListeners();
                m_ContinueButton.onClick.AddListener(OnContinueClick);
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (Battle.RunSession.Instance != null)
            {
                Battle.RunSession.Instance.OnCoinChanged -= OnCoinChanged;
            }

            if (m_ContinueButton != null)
            {
                m_ContinueButton.onClick.RemoveListener(OnContinueClick);
            }
            base.OnClose(isShutdown, userData);
        }

        private void BuildUI()
        {
            // 清理旧商品
            if (m_ItemRoot != null)
            {
                foreach (Transform child in m_ItemRoot)
                {
                    Destroy(child.gameObject);
                }
            }

            if (m_ItemRoot == null)
            {
                var go = new GameObject("ItemsRoot");
                go.transform.SetParent(transform, false);
                m_ItemRoot = go.transform;
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0, 30);
                rt.sizeDelta = new Vector2(1000, 420);
            }

            for (int i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                var btn = CreateButton("Item" + i, m_ItemRoot, "", new Vector2(0, 130 - i * 140));
                var btnText = btn.GetComponentInChildren<Text>();
                if (btnText != null)
                {
                    btnText.text = string.Format("{0}\n{1} - {2}金币", item.Name, item.Description, item.Price);
                    btnText.fontSize = 20;
                    btnText.alignment = TextAnchor.MiddleCenter;
                }

                var captured = item;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnBuyItem(captured));
            }
        }

        private void OnBuyItem(Shop.ShopItem item)
        {
            var session = Battle.RunSession.Instance;
            if (session == null)
            {
                return;
            }

            if (!session.TrySpendCoin(item.Price))
            {
                if (m_InfoText != null)
                {
                    m_InfoText.text = "金币不足！";
                }
                return;
            }

            // 购买成功音效
            Audio.SfxManager.PlayBuy();

            if (item.Type == Shop.ShopItemType.Mod)
            {
                session.AddModToBag(item.DataId);

                // 立即应用到玩家武器
                var player = UnityEngine.Object.FindObjectOfType<Entity.PlayerEntity>();
                var modRow = GameEntry.Data.GetMod(item.DataId);
                if (player != null && player.PrimaryWeapon != null && modRow != null)
                {
                    player.PrimaryWeapon.ModComponent.AddMod(modRow);
                }
            }
            // 武器购买：暂存（Phase 2 简化：仅提示）

            if (m_InfoText != null)
            {
                m_InfoText.text = "已购买：" + item.Name;
            }
            RefreshCoin();

            // 移除已购商品
            m_Items.Remove(item);
            BuildUI();
        }

        private void OnContinueClick()
        {
            // 关闭商店 UI
            if (GameEntry.UI != null)
            {
                GameEntry.UI.CloseUIForm(UIForm);
            }

            // 恢复战斗波次
            var manager = UnityEngine.Object.FindObjectOfType<Battle.BattleManager>();
            if (manager != null)
            {
                manager.ResumeAfterShop();
            }
        }

        private void OnCoinChanged(int coin)
        {
            RefreshCoin();
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

        // ---------- UI 生成辅助 ----------

        private static GameObject CreateText(string name, Transform parent, string content, int fontSize, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(600, 60);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize > 0 ? fontSize : 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return go;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(700, 100);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.5f, 0.9f, 1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var labelGo = CreateText("Label", go.transform, label, 24, Vector2.zero);
            labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(680, 90);

            return button;
        }
    }
}
