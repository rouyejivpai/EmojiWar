//------------------------------------------------------------
// EmojiWar GameMain - 背包页面（战斗阶段 / 波间商店阶段可用，Tab 开关）
//
// P4 接线：页面 = 真容器视图（UiSlotContainer），不再是只读展示。
//   左：两只手的法杖槽（Hand_L / Hand_R，可拖）
//   中：手部法杖的法术槽序列（内嵌法杖编辑区，按当前法杖实例绑定 wand#N）
//   右：6×5 = 30 格背包（ItemGrid）
// 显隐：Tab 开关（BackpackHotkey）；打开为 Popup 组，不暂停战斗；离开战斗流程自动关闭。
// 规范：容器下放 Cell 模板（UiListCell 行源）+ 池化 + 先清空再生成 + 规范登记见 UiListNormTools。
//------------------------------------------------------------

using UnityEngine;
using DG.Tweening;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>背包页面。</summary>
    public partial class BackpackForm : UGuiForm
    {
        public const string InstanceName = "BackpackForm(Clone)";
        private const string PanelName = "panel_Slide";

        // ---- QuickBind 绑定字段名（范式：UI 引用统一挂在根上的 QuickBind 组件里） ----
        // 手改层级/尺寸后只需跑一次 EmojiWar/Tools/Rebind Backpack UI (QuickBind)，
        // 或者在根物体的 QuickBind 组件上手改这几条引用；逻辑脚本不再自己持有 SerializeField。
        private const string BindItemGrid = "m_ItemGrid";
        private const string BindWeaponSlotL = "m_WeaponSlotL";
        private const string BindWeaponSlotR = "m_WeaponSlotR";
        private const string BindSpellListL = "m_SpellListL";
        private const string BindSpellListR = "m_SpellListR";
        private const string BindBtnCollapse = "m_BtnCollapse";

        [Header("背包滑出（参考选角界面）")]
        [SerializeField] private SlideMotionDirection m_SlideDirection = SlideMotionDirection.Top;
        [SerializeField] private float m_AnimDuration = 0.32f;
        [SerializeField] private bool m_StartHidden = true;

        private SlideMotion m_Motion = null;
        private RectTransform m_PanelRect = null;
        private CanvasGroup m_PanelGroup = null;

        private UiSlotContainer m_HandLeft = null;
        private UiSlotContainer m_HandRight = null;
        private UiSlotContainer m_ItemGrid = null;
        private UiSlotContainer m_LeftSpells = null;    // 武器槽的子列表：左手法术槽
        private UiSlotContainer m_RightSpells = null;   // 武器槽的子列表：右手法术槽
        private int m_LeftBoundCapacity = -1;
        private int m_RightBoundCapacity = -1;
        private QuickBind m_Bind = null;                // 根上的 QuickBind：全部 UI 引用的唯一来源
        private SpellPreviewPanel m_Preview = null;     // S6/D17：序列预览面板（代码构建，挂在本窗下）

        public bool IsOpen { get { return m_Motion != null && m_Motion.IsOpen; } }

        // 探针/自动化测试用（P4 拖拽集成探针需要拿到真实 Cell）
        public UiSlotContainer ItemGridContainer { get { return m_ItemGrid; } }
        public UiSlotContainer HandLeftContainer { get { return m_HandLeft; } }
        public UiSlotContainer HandRightContainer { get { return m_HandRight; } }
        public UiSlotContainer LeftSpellsContainer { get { return m_LeftSpells; } }
        public UiSlotContainer RightSpellsContainer { get { return m_RightSpells; } }

        /// <summary>S6/D17：序列预览面板（未打开时可能为 null）。供探针断言。</summary>
        public SpellPreviewPanel Preview { get { return m_Preview; } }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            // UI 引用的唯一来源：根物体上的 QuickBind（逻辑脚本不再自己声明 SerializeField）
            m_Bind = GetComponent<QuickBind>();
            if (m_Bind == null)
            {
                WriteProbe("[backpack] 缺少 QuickBind 组件：UI 引用将全部落空，请挂上并跑 Rebind Backpack UI");
            }

            m_PanelRect = transform.Find(PanelName) as RectTransform;
            if (m_PanelRect == null) { m_PanelRect = transform as RectTransform; }
            m_PanelGroup = m_PanelRect != null ? m_PanelRect.GetComponent<CanvasGroup>() : null;
            if (m_PanelGroup == null && m_PanelRect != null) { m_PanelGroup = m_PanelRect.gameObject.AddComponent<CanvasGroup>(); }

            m_Motion = gameObject.GetComponent<SlideMotion>();
            if (m_Motion == null) { m_Motion = gameObject.AddComponent<SlideMotion>(); }
            m_Motion.Configure(m_SlideDirection, m_PanelRect, m_PanelGroup, m_PanelGroup,
                Mathf.Max(0.01f, m_AnimDuration), Ease.OutCubic, 30f, m_StartHidden);

            // 收起按钮：优先取 QuickBind 绑定（m_BtnCollapse），没绑定时退回按名字找（并提示补绑定）
            var collapse = BindComponent<UnityEngine.UI.Button>(BindBtnCollapse);
            if (collapse == null)
            {
                var t = transform.Find(PanelName + "/btn_Collapse");
                if (t == null) { t = transform.Find("btn_Collapse"); }
                if (t != null) { collapse = t.GetComponent<UnityEngine.UI.Button>(); }
            }
            if (collapse != null)
            {
                collapse.onClick.RemoveAllListeners();
                collapse.onClick.AddListener(() => TogglePanel(false, true));
            }
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindContainers();
            TogglePanel(true, false);

            // S6/D17：序列预览面板（总耗蓝/延迟/充能/周期 + 触发顺序）。
            // 面板**完全由代码构建**（不改手改过的 BackpackForm.prefab，也不需要重打 AB）。
            var svc = ItemSystem.Service;
            if (svc != null)
            {
                m_Preview = SpellPreviewPanel.Attach(m_PanelRect, svc);
                if (m_Preview != null) { m_Preview.Refresh(); }
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (m_Motion != null) { m_Motion.SnapClose(); }
            base.OnClose(isShutdown, userData);
        }

        /// <summary>展开/收起。</summary>
        public void TogglePanel(bool show, bool animate = true)
        {
            if (m_Motion == null) { return; }
            m_Motion.SetOpen(show, animate);
            if (show) { BindContainers(); }
        }

        // ---------- 容器绑定 ----------

        /// <summary>
        /// 绑定容器视图：背包(30) / 左手+左手法术槽子列表 / 右手+右手法术槽子列表。
        /// 法术槽是**武器槽的子列表**，Cell 数 = 该手法杖的槽数（动态）；没杖时子列表隐藏。
        /// </summary>
        private void BindContainers()
        {
            var service = ItemSystem.Service;
            if (service == null) { return; }

            // 全部引用来自根上的 QuickBind（缺失即在探针里点名，去 QuickBind 补/m_Rebind 一键重绑）
            m_ItemGrid = BindComponent<UiSlotContainer>(BindItemGrid);
            m_HandLeft = BindComponent<UiSlotContainer>(BindWeaponSlotL);
            m_HandRight = BindComponent<UiSlotContainer>(BindWeaponSlotR);
            m_LeftSpells = BindComponent<UiSlotContainer>(BindSpellListL);
            m_RightSpells = BindComponent<UiSlotContainer>(BindSpellListR);

            if (m_ItemGrid != null) { m_ItemGrid.Bind(ItemSystem.BackpackId); }
            if (m_HandLeft != null)
            {
                m_HandLeft.Bind(ItemSystem.HandLeftId);
                m_HandLeft.SetKeyLabel("主武器 · 左键");     // 左键=主武器（CharacterSO.DefaultWeaponId）
            }
            if (m_HandRight != null)
            {
                m_HandRight.Bind(ItemSystem.HandRightId);
                m_HandRight.SetKeyLabel("副武器 · 右键");     // 右键=副武器（CharacterSO.SecondWeaponId）
            }

            m_LeftBoundCapacity = BindSpellList(service, m_LeftSpells, ItemSystem.HandLeftId, m_LeftBoundCapacity);
            m_RightBoundCapacity = BindSpellList(service, m_RightSpells, ItemSystem.HandRightId, m_RightBoundCapacity);

            WriteProbe(string.Format("[backpack] bind grid={0} handL={1} spellSlotsL={2} handR={3} spellSlotsR={4} (source=QuickBind)",
                m_ItemGrid != null ? m_ItemGrid.CellCount : -1,
                m_HandLeft != null ? m_HandLeft.CellCount : -1,
                m_LeftSpells != null && m_LeftSpells.gameObject.activeSelf ? m_LeftSpells.CellCount : -1,
                m_HandRight != null ? m_HandRight.CellCount : -1,
                m_RightSpells != null && m_RightSpells.gameObject.activeSelf ? m_RightSpells.CellCount : -1));

            // 内容探针：手上到底有没有法杖（"武器槽是空的"= 这里 dump 出 empty）
            var svc = ItemSystem.Service;
            if (svc != null)
            {
                var hl = svc.GetContainer(ItemSystem.HandLeftId);
                var hr = svc.GetContainer(ItemSystem.HandRightId);
                var bag = svc.GetContainer(ItemSystem.BackpackId);
                WriteProbe(string.Format("[backpack] handL={0} | handR={1} | backpack={2}",
                    hl != null ? hl.Dump() : "null",
                    hr != null ? hr.Dump() : "null",
                    bag != null ? bag.Dump() : "null"));
            }
        }

        /// <summary>
        /// 从 QuickBind 取组件；缺绑定/类型不符时写探针点名（避免"界面是空的却不知道为什么"）。
        /// </summary>
        private T BindComponent<T>(string fieldName) where T : Component
        {
            if (m_Bind == null) { return null; }
            var comp = m_Bind.Get<T>(fieldName);
            if (comp == null)
            {
                WriteProbe("[backpack] QuickBind 缺少绑定: " + fieldName + "（请跑 EmojiWar/Tools/Rebind Backpack UI）");
            }
            return comp;
        }

        /// <summary>绑定一只手法术槽子列表；槽数变化时重建，没杖（0 槽）时隐藏。返回本次绑定的槽数。</summary>
        private int BindSpellList(InventoryService service, UiSlotContainer view, string handContainerId, int boundCapacity)
        {
            if (view == null) { return boundCapacity; }

            string spellContainerId = InventoryService.HandSpellContainerId(handContainerId);
            var spellContainer = service.GetContainer(spellContainerId);
            int capacity = spellContainer != null ? spellContainer.ActiveCapacity : 0;

            if (capacity <= 0)
            {
                view.gameObject.SetActive(false);
                return 0;
            }

            view.gameObject.SetActive(true);
            if (boundCapacity != capacity)
            {
                view.Bind(spellContainerId);
            }
            else
            {
                view.RefreshAll();
            }
            return capacity;
        }

        // 说明：UI 引用不再按节点名字运行时查找 —— 统一走根上的 QuickBind（m_ItemGrid / m_WeaponSlotL …）。
        // 手改层级后跑一次 EmojiWar/Tools/Rebind Backpack UI (QuickBind) 即可，代码无需改。

        // ---------- 静态辅助（供 Hotkey 使用） ----------

        /// <summary>查找已展开的背包实例（滑出收起后不算打开，供 Tab 判定）。</summary>
        public static BackpackForm FindOpenInstance()
        {
            var any = FindInstance();
            return any != null && any.IsOpen ? any : null;
        }

        /// <summary>查找当前存在的背包实例（不论展开/收起）。</summary>
        public static BackpackForm FindInstance()
        {
            var ui = GameEntry.UI;
            if (ui == null) { return null; }
            foreach (var group in ui.GetAllUIGroups())
            {
                foreach (var form in group.GetAllUIForms())
                {
                    var logic = form as UnityGameFramework.Runtime.UIForm;
                    if (logic == null || logic.Logic == null) { continue; }
                    var backpack = logic.Logic as BackpackForm;
                    if (backpack != null && backpack.gameObject.activeInHierarchy)
                    {
                        return backpack;
                    }
                }
            }
            return null;
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
    }
}
