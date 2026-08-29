//------------------------------------------------------------
// EmojiWar GameMain - 角色选择全屏面板（准备阶段可随时切换角色）
// 形态：
//   - 默认展开：全屏面板（左右分栏：左=4 角色卡片竖排，右=选中角色详情），
//     顶部有"收起"按钮。
//   - 收起后：面板主体向右滑出屏幕，仅右侧边缘露出一个窄条标签
//     （btn_Tab，"角色"），点击标签再次滑入全屏面板。
//   - 与房间面板（RoomForm 右侧窄条）共存：展开时本面板置顶覆盖。
// 动效：DOTween 滑入/滑出（SidePanelForm 基类，滑动 SlideTarget 子面板）。
//------------------------------------------------------------

using System;
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

        /// <summary>角色面板已打开（参数：面板实例；房间页据此绑定/展开）。</summary>
        public static event Action<CharacterDockForm> OnDockOpened;

        public static void Change(int characterId) { OnCharacterChanged?.Invoke(characterId); }

        public static void DockOpened(CharacterDockForm dock) { OnDockOpened?.Invoke(dock); }
    }

    /// <summary>
    /// 角色选择全屏面板（partial：UI 字段由 QuickBind 生成，见 CharacterDockForm.QuickBind.cs）。
    /// </summary>
    public partial class CharacterDockForm : SidePanelForm
    {
        private int m_SelectedCharacterId = 1;
        private DRCharacter[] m_Characters = null;

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
            BindButtons();

            // 收起按钮 + 右侧窄条 Tab
            if (m_BtnCollapse != null)
            {
                m_BtnCollapse.onClick.RemoveAllListeners();
                m_BtnCollapse.onClick.AddListener(() => RequestCollapse());
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

        /// <summary>从数据表加载角色（Id=1..4）。</summary>
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

        /// <summary>绑定 4 个角色按钮。</summary>
        private void BindButtons()
        {
            BindButton(m_BtnChar1, 1, m_TxtChar1Name);
            BindButton(m_BtnChar2, 2, m_TxtChar2Name);
            BindButton(m_BtnChar3, 3, m_TxtChar3Name);
            BindButton(m_BtnChar4, 4, m_TxtChar4Name);
        }

        private void BindButton(Button button, int characterId, Text nameText)
        {
            if (button == null)
            {
                return;
            }

            var row = GetCharacter(characterId);
            if (row != null && nameText != null)
            {
                nameText.text = row.CharacterName;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectCharacter(characterId, true));
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

        /// <summary>选中角色：更新 UI + 触发切换事件（供房间页同步角色）。</summary>
        public void SelectCharacter(int characterId, bool notify)
        {
            m_SelectedCharacterId = characterId;
            Procedure.ProcedureBattle.SelectedCharacterId = characterId;

            var row = GetCharacter(characterId);
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
                    m_TxtDetailDesc.text = row.Description;
                }
                if (m_TxtDetailStats != null)
                {
                    m_TxtDetailStats.text = string.Format("生命 {0}  ·  移速 {1:F0}  ·  初始金币 {2}",
                        row.MaxHealth, row.MoveSpeed, row.Coin);
                }
                if (m_TxtDetailIcon != null && !string.IsNullOrEmpty(row.Icon))
                {
                    m_TxtDetailIcon.text = GetIconEmoji(row.Icon);
                }
            }

            if (notify)
            {
                CharacterDockEvents.Change(characterId);
            }
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
