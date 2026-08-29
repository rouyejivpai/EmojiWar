//------------------------------------------------------------
// EmojiWar GameMain - 角色选择全屏面板（数据驱动，准备阶段可随时切换角色）
// 形态：
//   - 默认展开：全屏面板（左右分栏：左=角色卡片竖排【按 JSON 动态实例化】，
//     右=选中角色详情），顶部有"收起"按钮 + "确认选择"按钮。
//   - 收起后：面板主体向右滑出屏幕，仅右侧边缘露出窄条标签（btn_Tab），
//     点击标签再次滑入全屏面板。
// 数据：character_select.json（UI 展示配置，Resources/Configs/），
//       属性查 Character.txt（单一逻辑源）。
// 交互：点卡片选中（高亮，不收起）；点"确认选择"收起并确认角色。
// 动效：DOTween 滑入/滑出（SidePanelForm 基类，滑动 SlideTarget 子面板）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.UI
{
    /// <summary>角色切换事件（房间页使用）。</summary>
    public static class CharacterDockEvents
    {
        /// <summary>玩家切换角色（参数：角色 ID）。</summary>
        public static event Action<int> OnCharacterChanged;

        /// <summary>玩家确认角色（参数：角色 ID；点"确认选择"触发，随后收起面板）。</summary>
        public static event Action<int> OnCharacterConfirmed;

        /// <summary>角色面板已打开（参数：面板实例；房间页据此绑定/展开）。</summary>
        public static event Action<CharacterDockForm> OnDockOpened;

        public static void Change(int characterId) { OnCharacterChanged?.Invoke(characterId); }

        public static void Confirm(int characterId) { OnCharacterConfirmed?.Invoke(characterId); }

        public static void DockOpened(CharacterDockForm dock) { OnDockOpened?.Invoke(dock); }
    }

    /// <summary>
    /// 角色选择全屏面板（partial：UI 字段由 QuickBind 生成，见 CharacterDockForm.QuickBind.cs）。
    /// </summary>
    public partial class CharacterDockForm : SidePanelForm
    {
        private int m_SelectedCharacterId = 1;
        private DRCharacter[] m_Characters = null;

        // 动态卡片（按 JSON 实例化）
        private readonly List<CharacterCard> m_Cards = new List<CharacterCard>();
        private RectTransform m_CardContainer = null;
        private const string CardContainerName = "CardContainer";

        /// <summary>当前选中角色 ID。</summary>
        public int SelectedCharacterId { get { return m_SelectedCharacterId; } }

        // ============ 面板定位（全屏，右缘滑入/滑出） ============

        /// <summary>滑动主体（panel_PanelSlide 子物体；按名查找，不依赖 QuickBind 字段）。</summary>
        protected override RectTransform SlideTarget
        {
            get
            {
                if (m_SlideTarget == null)
                {
                    var t = transform.Find("panel_PanelSlide");
                    if (t == null)
                    {
                        t = transform.Find("PanelSlide");
                    }
                    if (t != null)
                    {
                        m_SlideTarget = t as RectTransform;
                    }
                }
                return m_SlideTarget != null ? m_SlideTarget : base.SlideTarget;
            }
        }

        private RectTransform m_SlideTarget = null;
        private CanvasGroup m_SlideCanvasGroup = null;

        /// <summary>
        /// raycast 控制器：指向 PanelSlide 上的 CanvasGroup——
        /// 展开时 PanelSlide 拦截点击（全屏面板可交互），收起时释放（右侧窄条 Tab 保持可点）。
        /// </summary>
        protected override CanvasGroup RaycastBlocker
        {
            get
            {
                var target = SlideTarget;
                if (target == null)
                {
                    return PanelCanvasGroup;
                }
                if (m_SlideCanvasGroup == null)
                {
                    m_SlideCanvasGroup = target.GetComponent<CanvasGroup>();
                    if (m_SlideCanvasGroup == null)
                    {
                        m_SlideCanvasGroup = target.gameObject.AddComponent<CanvasGroup>();
                    }
                }
                return m_SlideCanvasGroup;
            }
        }

        protected override Vector2 GetVisiblePos()
        {
            // 展开：面板主体完全在屏幕内（锚 (1,0.5) 右缘，anchoredPosition.x = 0）
            return new Vector2(0f, 0f);
        }

        protected override Vector2 GetHiddenPos()
        {
            // 收起：面板主体向右滑出（宽度 + 余量）；右缘窄条 Tab 常驻可见
            var target = SlideTarget;
            float w = target != null ? target.rect.width : 1920f;
            return new Vector2(w + 30f, 0f);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            LoadCharacters();
            BuildCards();

            // 收起按钮 + 确认按钮 + 右侧窄条 Tab
            if (m_BtnCollapse != null)
            {
                m_BtnCollapse.onClick.RemoveAllListeners();
                m_BtnCollapse.onClick.AddListener(() => RequestCollapse());
            }
            if (m_BtnConfirm != null)
            {
                m_BtnConfirm.onClick.RemoveAllListeners();
                m_BtnConfirm.onClick.AddListener(() => ConfirmSelection());
            }
            if (m_BtnTab != null)
            {
                m_BtnTab.onClick.RemoveAllListeners();
                m_BtnTab.onClick.AddListener(() => TogglePanel(true, true));
            }

            SelectCharacter(Procedure.ProcedureBattle.SelectedCharacterId, false);

            // 默认展开（用户需求：进房间即全屏角色面板），并通知房间页绑定本实例
            ShowImmediate();
            CharacterDockEvents.DockOpened(this);
        }

        /// <summary>从战斗数据表加载角色（属性查询用，Id=1..4）。</summary>
        private void LoadCharacters()
        {
            m_Characters = null;
            if (GameEntry.Data != null)
            {
                m_Characters = GameEntry.Data.GetAllCharacters().ToArray();
            }
            if (m_Characters == null || m_Characters.Length == 0)
            {
                m_Characters = new DRCharacter[0];
            }
        }

        /// <summary>
        /// 按 character_select.json 动态实例化角色卡片（数据驱动，加角色自动多一张卡）。
        /// 卡片从 CharacterCard.prefab（Resources/UI/CharacterCard）加载。
        /// </summary>
        private void BuildCards()
        {
            // 清理旧卡
            foreach (var card in m_Cards)
            {
                if (card != null && card.gameObject != null)
                {
                    Destroy(card.gameObject);
                }
            }
            m_Cards.Clear();

            // 卡片容器（panel_PanelSlide 下的 CardContainer）
            m_CardContainer = FindCardContainer();

            var config = CharacterSelectConfigLoader.Load();
            if (config == null || config.characters == null || config.characters.Count == 0)
            {
                Debug.LogWarning("[CharDock] character_select.json 为空，无卡片生成");
                return;
            }

            var cardPrefab = Resources.Load<GameObject>("UI/CharacterCard");
            if (cardPrefab == null)
            {
                Debug.LogWarning("[CharDock] 未找到 CharacterCard.prefab（Resources/UI/CharacterCard）");
                return;
            }

            int index = 0;
            foreach (var entry in config.characters)
            {
                if (entry == null || entry.id <= 0)
                {
                    continue;
                }

                var go = Instantiate(cardPrefab, m_CardContainer);
                go.name = "Card_" + entry.id;

                var card = go.GetComponent<CharacterCard>();
                if (card == null)
                {
                    Destroy(go);
                    continue;
                }

                // 简略属性：查 Character.txt（单一逻辑源）
                string statsLine = "";
                var row = GetCharacter(entry.id);
                if (row != null)
                {
                    statsLine = string.Format("生命{0} 移速{1:F0}", row.MaxHealth, row.MoveSpeed);
                }

                // 图标：emoji 转 Sprite（ArtManager 有 GetCharacterSprite）
                Sprite iconSprite = null;
                if (!string.IsNullOrEmpty(entry.icon))
                {
                    iconSprite = Art.ArtManager.GetCharacterSprite(entry.icon);
                }

                int capturedId = entry.id;
                card.Setup(entry.id, entry.name, entry.icon, statsLine, iconSprite,
                    (id) => SelectCharacter(id, true));

                // 手动定位（竖排；容器无 LayoutGroup 时按间距摆放）
                var cardRect = go.transform as RectTransform;
                if (cardRect != null && m_CardContainer != null)
                {
                    cardRect.anchorMin = new Vector2(0.5f, 1f);
                    cardRect.anchorMax = new Vector2(0.5f, 1f);
                    cardRect.pivot = new Vector2(0.5f, 0.5f);
                    cardRect.anchoredPosition = new Vector2(0f, -80f - index * 180f);
                    cardRect.sizeDelta = new Vector2(300f, 160f);
                }

                m_Cards.Add(card);
                index++;
            }
        }

        /// <summary>查找卡片容器（PanelSlide 下）。</summary>
        private RectTransform FindCardContainer()
        {
            var slide = SlideTarget;
            if (slide == null)
            {
                return null;
            }
            var t = slide.Find(CardContainerName);
            if (t == null)
            {
                // 兜底：创建
                var go = new GameObject(CardContainerName);
                go.transform.SetParent(slide, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                return rt;
            }
            return t as RectTransform;
        }

        private DRCharacter GetCharacter(int id)
        {
            foreach (var c in m_Characters)
            {
                if (c != null && c.Id == id)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>选中角色：更新卡片高亮 + 右侧详情 + 触发切换事件（供房间页同步角色）。</summary>
        public void SelectCharacter(int characterId, bool notify)
        {
            m_SelectedCharacterId = characterId;
            Procedure.ProcedureBattle.SelectedCharacterId = characterId;

            // 卡片高亮
            foreach (var card in m_Cards)
            {
                if (card != null)
                {
                    card.SetHighlight(card.CharacterId == characterId);
                }
            }

            // 详情区（数据查 Character.txt + JSON 描述）
            var row = GetCharacter(characterId);
            var entry = CharacterSelectConfigLoader.GetEntry(characterId);
            if (row != null)
            {
                if (m_TxtCurrentChar != null)
                {
                    m_TxtCurrentChar.text = "角色: " + row.CharacterName;
                }
                if (m_TxtDetailName != null)
                {
                    m_TxtDetailName.text = row.CharacterName;
                }
                if (m_TxtDetailDesc != null)
                {
                    m_TxtDetailDesc.text = entry != null && !string.IsNullOrEmpty(entry.desc) ? entry.desc : row.Description;
                }
                if (m_TxtDetailStats != null)
                {
                    m_TxtDetailStats.text = string.Format("生命 {0}  ·  移速 {1:F0}  ·  初始金币 {2}",
                        row.MaxHealth, row.MoveSpeed, row.Coin);
                }
                if (m_TxtDetailIcon != null)
                {
                    string icon = entry != null && !string.IsNullOrEmpty(entry.icon) ? entry.icon : row.Icon;
                    m_TxtDetailIcon.text = GetIconEmoji(icon);
                }
            }

            if (notify)
            {
                CharacterDockEvents.Change(characterId);
            }
        }

        /// <summary>确认选择：触发确认事件并收起面板。</summary>
        public void ConfirmSelection()
        {
            CharacterDockEvents.Confirm(m_SelectedCharacterId);
            RequestCollapse();
        }

        /// <summary>emoji 码点 → 显示字符（1f605 → 😅 等）。</summary>
        private static string GetIconEmoji(string code)
        {
            try
            {
                int cp = Convert.ToInt32(code, 16);
                return char.ConvertFromUtf32(cp);
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>由外部（房间页）设置当前角色并刷新标签（不触发事件）。</summary>
        public void RefreshCurrentLabel(int characterId)
        {
            m_SelectedCharacterId = characterId;
            var row = GetCharacter(characterId);
            if (row != null && m_TxtCurrentChar != null)
            {
                m_TxtCurrentChar.text = "角色: " + row.CharacterName;
            }
        }

        /// <summary>面板自收起（顶部"收起"按钮 / 外部调用）。</summary>
        public void RequestCollapse()
        {
            TogglePanel(false, true);
        }
    }
}
