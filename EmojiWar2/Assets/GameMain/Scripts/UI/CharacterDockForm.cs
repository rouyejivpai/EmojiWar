//------------------------------------------------------------
// EmojiWar GameMain - 角色选择全屏面板（数据驱动，准备阶段可随时切换角色）
// 形态：
//   - 默认展开：全屏面板（左右分栏：左=角色卡片竖排【按 SelectConfigSO 动态实例化】，
//     右=选中角色详情），顶部有"收起"按钮 + "确认选择"按钮。
//   - 收起后：面板主体向右滑出屏幕，仅右侧边缘露出窄条标签（btn_Tab），
//     点击标签再次滑入全屏面板。
// 数据：CharacterSelectConfigSO（Resources/Data/Select，Inspector 可编辑）；
//       战斗数值查 CharacterSO（单一逻辑源）。
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

        /// <summary>Change 事件累计触发次数（自动化复现观测用）。</summary>
        public static int ChangeCount { get; private set; }

        public static void Change(int characterId)
        {
            ChangeCount++;
            OnCharacterChanged?.Invoke(characterId);
        }

        public static void Confirm(int characterId) { OnCharacterConfirmed?.Invoke(characterId); }

        public static void DockOpened(CharacterDockForm dock) { OnDockOpened?.Invoke(dock); }
    }

    /// <summary>
    /// 角色选择全屏面板（partial：UI 字段由 QuickBind 生成，见 CharacterDockForm.QuickBind.cs）。
    /// </summary>
    public partial class CharacterDockForm : SidePanelForm
    {
        private int m_SelectedCharacterId = 1;
        private CharacterSO[] m_Characters = null;
        private Text m_TabLabel = null;   // 收起态 Tab 内标签（显示当前角色名）

        [Header("CharacterDock 布局（可选项）")]
        [Tooltip("勾选=每次打开把顶部控件(标题/收起/确认)贴面板顶边防超宽裁切；不勾选(默认)=完全遵循你在预制体里保存的子物体数值")]
        [SerializeField]
        private bool m_AutoPinTopControls = false;

        // 动态卡片（按 JSON 实例化）
        private readonly List<CharacterCard> m_Cards = new List<CharacterCard>();
        private RectTransform m_CardContainer = null;
        private GameObject m_CharCardPrefab = null;   // CharacterCard.prefab(items，UiPrefab 加载)
        private Image m_DetailIconImage = null;      // 详情图标（Image，运行时把旧 Text 节点转成 Image）
        private const string CardContainerName = "CardContainer";

        /// <summary>
        /// 确保选角卡片预制体已加载（Assets/GameMain/UI/items/CharacterCard.prefab，
        /// 属 'game' AssetBundle，经 UiPrefab 全路径加载）；加载完成后再建卡片。
        /// </summary>
        private void EnsureCards()
        {
            var path = Constant.UIItemAssetPath.CharacterCard;
            if (m_CharCardPrefab == null)
            {
                m_CharCardPrefab = UiPrefab.GetCached(path);
            }
            if (m_CharCardPrefab != null)
            {
                BuildCards();
                return;
            }
            UiPrefab.Load(path, prefab =>
            {
                if (prefab == null)
                {
                    WriteProbe("[dock] CharacterCard prefab 加载失败: " + path);
                    return;
                }
                m_CharCardPrefab = prefab;
                WriteProbe("[dock] CharacterCard prefab 已加载 (items)");
                if (gameObject != null && gameObject.activeInHierarchy)
                {
                    BuildCards();
                    SelectCharacter(Procedure.ProcedureBattle.SelectedCharacterId, false);
                }
            });
        }

        /// <summary>当前选中角色 ID。</summary>
        public int SelectedCharacterId { get { return m_SelectedCharacterId; } }

        /// <summary>当前动态卡片数（自动化复现用）。</summary>
        public int CardCount { get { return m_Cards != null ? m_Cards.Count : 0; } }

        /// <summary>模拟点击指定角色 ID 的卡片（与卡片 onClick 同路径：SelectCharacter notify=true）。</summary>
        public void SimulateCardClick(int characterId)
        {
            SelectCharacter(characterId, true);
        }

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

        // 展开/隐藏位置与面板几何由 SidePanelForm 基类的编辑器配置驱动
        // （SlideDirection/FillCanvas/PanelSize/HiddenMargin，可在预制体 Inspector 修改）：
        //   可见位 = 贴所选方向边缘 (0,0)；隐藏位 = 沿方向按当前面板实际尺寸 + 余量滑出屏幕。

        protected override void OnInit(object userData)
        {
            // 关键：QuickBind 字段绑定（m_BtnCollapse/m_BtnConfirm/m_TxtDetailXxx 等）。
            // SidePanelForm/UGuiForm 基类的 OnInit 不会自动调用 QuickBindApplyBindings——
            // 若缺失，按钮引用为 null，onClick 无法绑定 → 点击无反应。
            base.OnInit(userData);

            var bind = GetComponent<QuickBind>();
            if (bind != null)
            {
                QuickBindApplyBindings(bind);
            }

            // 收起态 Tab 内标签（btn_Tab/TabLabel；显示当前角色名）
            if (m_TabLabel == null)
            {
                var tab = transform.Find("btn_Tab");
                if (tab != null)
                {
                    var label = tab.Find("TabLabel");
                    if (label != null)
                    {
                        m_TabLabel = label.GetComponent<Text>();
                    }
                }
            }
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            LoadCharacters();
            EnsureCards();   // 卡片预制体(items)异步就绪后建卡

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

            // 布局说明：
            //  - 面板几何（是否占满 / 尺寸 / 停靠方向 / 收起位移）由 SidePanelForm→SlideMotion
            //    驱动（预制体 Inspector：FillCanvas、PanelSize、SlideDirection、HiddenMargin）
            //  - 顶部控件默认**遵循预制体里保存的数值**；仅当勾选 m_AutoPinTopControls 时
            //    才贴面板顶边防裁切（防超宽屏裁切的可选项）。
            if (m_AutoPinTopControls)
            {
                ApplyResponsiveLayout();
            }
            else
            {
                WriteProbe("[dock] top controls follow prefab (autoPin=false)");
            }

            // 默认展开（用户需求：进房间即全屏角色面板），并通知房间页绑定本实例
            ShowImmediate();
            {
                var st = SlideTarget;
                WriteProbe(string.Format("[dock] open pose: anchored={0} fill={1} visible={2}",
                    st != null ? st.anchoredPosition.ToString() : "null",
                    FillCanvasSetting,
                    IsVisible));
            }
            CharacterDockEvents.DockOpened(this);
        }

        /// <summary>
        /// 内容贴边锚定：面板几何（占满/尺寸/方向/隐藏位移）由 SidePanelForm 基类的
        /// 编辑器配置统一负责（ApplyConfigLayout / GetHiddenPos）；本方法只把顶部控件
        /// （标题/收起/确认）改贴"面板顶边"（负偏移），防止异宽高比下面板较矮时被裁。
        /// 幂等：每次 Open 都设置同一规范值，与是否全屏/哪个方向无关。
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            var slide = SlideTarget;
            float slideH = slide != null ? slide.rect.height : 0f;
            WriteProbe(string.Format("[dock] responsive: slideH={0:F0} anchors={1}-{2}",
                slideH,
                slide != null ? slide.anchorMin.ToString() : "null",
                slide != null ? slide.anchorMax.ToString() : "null"));

            // 顶部一排统一锚：贴面板顶边(y=1)、pivot 顶部；x 相对面板中心（保持原横向设计）
            RepinTopCenter("txt_SelectTitle", -260f, -80f);   // 标题（原 y=460 → 距顶 80）
            RepinTopCenter("btn_Collapse", 300f, -80f);       // 收起（原 y=460）
            RepinTopCenter("btn_Confirm", 300f, -220f);       // 确认（原下移到 y=320，距顶 220，且 x=300 在房间条左侧安全区）
        }

        /// <summary>把某个子物体重新锚定为"顶部居中"并给定位移（幂等，每次 Open 都设同一规范值）。</summary>
        private void RepinTopCenter(string childName, float x, float topOffsetY)
        {
            var slide = SlideTarget;
            if (slide == null)
            {
                return;
            }
            var child = slide.Find(childName);
            if (child == null)
            {
                WriteProbe("[dock] RepinTopCenter 未找到: " + childName);
                return;
            }
            var rt = child as RectTransform;
            if (rt == null)
            {
                return;
            }
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, topOffsetY);
            WriteProbe(string.Format("[dock] responsive pin {0} -> x={1} topOffset={2}", childName, x, topOffsetY));
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
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
                m_Characters = new CharacterSO[0];
            }
        }

        /// <summary>
        /// 按 CharacterSelectConfigSO 动态实例化角色卡片（数据驱动，加角色自动多一张卡）。
        /// 卡片从 CharacterCard.prefab（Assets/GameMain/UI/items/，UiPrefab 加载）实例化。
        /// </summary>
        private void BuildCards()
        {
            // 卡片容器（panel_PanelSlide 下的 CardContainer）
            m_CardContainer = FindCardContainer();

            // 清空容器全部子物体（含预制体预置的占位卡 Card_Template + 旧运行时卡片），
            // 再按 JSON 重新实例化——预制体里的占位仅用于编辑器中观察父子级关系。
            if (m_CardContainer != null)
            {
                for (int i = m_CardContainer.childCount - 1; i >= 0; i--)
                {
                    var child = m_CardContainer.GetChild(i);
                    if (child != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
            foreach (var card in m_Cards)
            {
                if (card != null && card.gameObject != null)
                {
                    Destroy(card.gameObject);
                }
            }
            m_Cards.Clear();

            var config = CharacterSelectConfigLoader.Load();
            if (config == null || config.characters == null || config.characters.Count == 0)
            {
                Debug.LogWarning("[CharDock] SelectConfig 为空，无卡片生成（请检查 Resources/Data/Select）");
                return;
            }

            var cardPrefab = m_CharCardPrefab;
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

                // 简略属性：查 CharacterSO（单一逻辑源）
                string statsLine = "";
                var row = GetCharacter(entry.id);
                if (row != null)
                {
                    statsLine = string.Format("生命{0} 移速{1:F0}", row.MaxHealth, row.MoveSpeed);
                }

                // 图标：优先选角条目引用，回退角色配置引用（SO 资产引用，非字符串码点）
                Sprite iconSprite = entry.iconSprite != null
                    ? entry.iconSprite
                    : (row != null ? row.IconSprite : null);

                int capturedId = entry.id;
                card.Setup(entry.id, entry.name, statsLine, iconSprite,
                    (id) => SelectCharacter(id, true));

                // 手动定位（竖排；容器无 LayoutGroup 时按间距摆放；锚顶居中，逐张下移）
                var cardRect = go.transform as RectTransform;
                if (cardRect != null && m_CardContainer != null)
                {
                    cardRect.anchorMin = new Vector2(0.5f, 1f);
                    cardRect.anchorMax = new Vector2(0.5f, 1f);
                    cardRect.pivot = new Vector2(0.5f, 0.5f);
                    cardRect.anchoredPosition = new Vector2(0f, -110f - index * 200f);
                    cardRect.sizeDelta = new Vector2(380f, 180f);
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

        private CharacterSO GetCharacter(int id)
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

            // 详情区（CharacterSO 数值 + SelectConfig 描述）
            var row = GetCharacter(characterId);
            var entry = CharacterSelectConfigLoader.GetEntry(characterId);
            if (row != null)
            {
                if (m_TxtCurrentChar != null)
                {
                    m_TxtCurrentChar.text = "角色: " + row.CharacterName;
                }
                if (m_TabLabel != null)
                {
                    m_TabLabel.text = row.CharacterName;
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
                SetDetailIconSprite(entry != null && entry.iconSprite != null ? entry.iconSprite : row.IconSprite);
            }

            if (notify)
            {
                CharacterDockEvents.Change(characterId);
            }
        }


        /// <summary>设置详情图标（资产引用 Sprite）：旧预制体该节点是 Text，运行时转成 Image。</summary>
        private void SetDetailIconSprite(Sprite sprite)
        {
            if (m_DetailIconImage == null && m_TxtDetailIcon != null)
            {
                // 同一 GameObject 不能同时挂 Text 与 Image（Graphic 互斥）：
                // 在 Text 节点下新建子物体放 Image，并隐藏 Text。
                var parentRect = m_TxtDetailIcon.rectTransform;
                var spriteGo = new GameObject("IconSprite");
                spriteGo.transform.SetParent(parentRect, false);
                var sr = spriteGo.AddComponent<RectTransform>();
                sr.anchorMin = Vector2.zero;
                sr.anchorMax = Vector2.one;
                sr.offsetMin = Vector2.zero;
                sr.offsetMax = Vector2.zero;
                m_DetailIconImage = spriteGo.AddComponent<Image>();
                m_DetailIconImage.preserveAspect = true;
                m_TxtDetailIcon.enabled = false;
            }
            if (m_DetailIconImage != null)
            {
                m_DetailIconImage.sprite = sprite;
                m_DetailIconImage.enabled = sprite != null;
            }
        }

        /// <summary>确认选择：触发确认事件并收起面板。</summary>
        public void ConfirmSelection()
        {
            CharacterDockEvents.Confirm(m_SelectedCharacterId);
            RequestCollapse();
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
