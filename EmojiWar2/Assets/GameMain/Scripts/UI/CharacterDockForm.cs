//------------------------------------------------------------
// EmojiWar GameMain - 角色选择侧边抽屉（准备阶段可随时切换角色）
// 停靠屏幕右侧：展开时展示 4 个角色卡片 + 选中角色简介面板；
// 收起时滑出屏幕右缘（只露一个"角色"标签按钮）。
// 动效：DOTween 滑入/滑出（继承 SidePanelForm）。
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

        /// <summary>角色抽屉已打开（参数：抽屉实例；房间页据此绑定/展开）。</summary>
        public static event Action<CharacterDockForm> OnDockOpened;

        public static void Change(int characterId) { OnCharacterChanged?.Invoke(characterId); }

        public static void DockOpened(CharacterDockForm dock) { OnDockOpened?.Invoke(dock); }
    }

    /// <summary>
    /// 角色选择侧边抽屉（partial：UI 字段由 QuickBind 生成，见 CharacterDockForm.QuickBind.cs）。
    /// </summary>
    public partial class CharacterDockForm : SidePanelForm
    {
        private int m_SelectedCharacterId = 1;
        private DRCharacter[] m_Characters = null;

        /// <summary>当前选中角色 ID。</summary>
        public int SelectedCharacterId { get { return m_SelectedCharacterId; } }

        // ============ 侧边抽屉定位（右侧停靠） ============

        protected override Vector2 GetVisiblePos()
        {
            // 展开：完全在屏幕内（锚右缘），anchoredPosition.x = 0
            return new Vector2(0f, 0f);
        }

        protected override Vector2 GetHiddenPos()
        {
            // 收起：向右滑出屏幕（面板自身宽度）
            float panelWidth = PanelRect != null ? PanelRect.rect.width : 420f;
            return new Vector2(panelWidth + 20f, 0f);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            LoadCharacters();
            BindButtons();
            SelectCharacter(Procedure.ProcedureBattle.SelectedCharacterId, false);

            // 打开后自动展开（滑入动效），并通知房间页绑定本实例
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
            }

            if (notify)
            {
                CharacterDockEvents.Change(characterId);
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

        /// <summary>抽屉自收起（供"收起"按钮/点击空白区调用）。</summary>
        public void RequestCollapse()
        {
            TogglePanel(false, true);
        }
    }
}
