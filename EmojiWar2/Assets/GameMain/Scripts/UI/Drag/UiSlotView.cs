//------------------------------------------------------------
// EmojiWar GameMain - 插槽视图（P3）
//
// 方案：doc/物品与法杖系统设计.md §5
//   Cell = 纯视图：绑定 SlotRef → 从服务拉数据渲染（图标/数量/空态/键位标签）；
//   拖拽事件交给 UiDragManager，放下后只调一次 InventoryService.TryMove，
//   失败用 InventoryService 的 Reason 提示（绝不静默失败）。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>槽位底色策略（法杖 / 法术 在槽里要一眼区分）。</summary>
    public enum SlotTintMode
    {
        Content = 0,   // 按当前物品类别：法杖=法杖色、法术=法术色、空=空槽色（背包网格用）
        Wand = 1,      // 固定法杖色（手部武器槽用）
        Spell = 2,     // 固定法术色（法术槽子列表用）
    }

    /// <summary>插槽 Cell（可拖拽 + 可放置）。</summary>
    [RequireComponent(typeof(UiDropTarget))]
    public sealed class UiSlotView : MonoBehaviour, IDragSource,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image m_Bg = null;          // 底图（也用于高亮）
        [SerializeField] private Image m_Icon = null;         // 物品图标
        [SerializeField] private Text m_CountText = null;     // 数量（>1 才显示）
        [SerializeField] private Text m_KeyText = null;       // 键位/标签（如"左键"）
        [SerializeField] private Color m_EmptyBg = new Color(1f, 1f, 1f, 0.06f);
        [SerializeField] private Color m_FilledBg = new Color(1f, 1f, 1f, 0.16f);
        [Header("类别底色（法杖 / 法术 一眼区分）")]
        [SerializeField] private Color m_WandBg = new Color(0.78f, 0.58f, 0.26f, 0.22f);   // 金橙 = 法杖
        [SerializeField] private Color m_SpellBg = new Color(0.34f, 0.44f, 0.82f, 0.22f);  // 蓝紫 = 法术
        [SerializeField] private SlotTintMode m_TintMode = SlotTintMode.Content;

        private UiDropTarget m_Target = null;
        private SlotRef m_Slot = default(SlotRef);
        private ItemInstance m_Item = ItemInstance.Empty;
        private string m_KeyLabel = null;
        private bool m_Bound = false;

        public SlotRef Slot { get { return m_Slot; } }
        public ItemInstance Item { get { return m_Item; } }

        private void Awake()
        {
            m_Target = GetComponent<UiDropTarget>();
        }

        /// <summary>绑定槽位（keyLabel 仅单槽容器用，例如"左键"）。</summary>
        public void Bind(SlotRef slot, string keyLabel = null)
        {
            m_Slot = slot;
            m_KeyLabel = keyLabel;
            m_Bound = true;
            if (m_Target == null) { m_Target = GetComponent<UiDropTarget>(); }
            if (m_Target != null) { m_Target.Bind(slot); }
            Refresh();
        }

        /// <summary>从服务拉取当前槽内容并渲染（幂等）。</summary>
        public void Refresh()
        {
            if (!m_Bound) { return; }
            var service = ItemSystem.Service;
            var container = service != null ? service.GetContainer(m_Slot.ContainerId) : null;
            m_Item = container != null ? container.Get(m_Slot.Index) : ItemInstance.Empty;

            var row = (!m_Item.IsEmpty && service != null) ? service.Table.GetItem(m_Item.ItemId) : null;

            if (m_Icon != null)
            {
                m_Icon.sprite = row != null ? row.IconSprite : null;
                m_Icon.enabled = row != null && row.IconSprite != null;
            }
            if (m_CountText != null)
            {
                m_CountText.text = m_Item.Count > 1 ? m_Item.Count.ToString() : string.Empty;
            }
            if (m_KeyText != null)
            {
                m_KeyText.text = m_KeyLabel ?? string.Empty;
            }
            if (m_Bg != null)
            {
                m_Bg.color = ResolveTint(row);
            }
        }

        /// <summary>槽位底色：按策略取（法杖=金橙、法术=蓝紫、空=弱底色）。</summary>
        private Color ResolveTint(ItemSO row)
        {
            switch (m_TintMode)
            {
                case SlotTintMode.Wand:
                    return m_WandBg;
                case SlotTintMode.Spell:
                    return m_SpellBg;
                default:
                    if (row == null) { return m_EmptyBg; }
                    if (row.Category == ItemCategory.Wand) { return m_WandBg; }
                    if (row.Category == ItemCategory.Spell) { return m_SpellBg; }
                    return m_FilledBg;
            }
        }

        /// <summary>设置底色策略（由容器下发；背包网格=Content、手部槽=Wand、法术槽=Spell）。</summary>
        public void SetTintMode(SlotTintMode mode)
        {
            m_TintMode = mode;
            Refresh();
        }

        /// <summary>覆盖键位标签（如"主武器 · 左键"）；只改本实例显示，不动预制体。</summary>
        public void SetKeyLabel(string label)
        {
            m_KeyLabel = label;
            if (m_KeyText != null) { m_KeyText.text = label ?? string.Empty; }
        }

        // ==================== IDragSource ====================

        public DragPayload GetPayload()
        {
            return m_Item.IsEmpty ? DragPayload.None : new DragPayload(m_Slot, m_Item.ItemId, m_Item.Count);
        }

        public bool CanBeginDrag(out string reason)
        {
            reason = null;
            if (m_Item.IsEmpty) { reason = "空槽不能拖"; return false; }
            var service = ItemSystem.Service;
            if (service == null) { reason = "物品服务未就绪"; return false; }
            var container = service.GetContainer(m_Slot.ContainerId);
            if (container == null) { reason = "槽位未绑定"; return false; }
            if (container.ReadOnly || container.Kind == SlotKind.Shop) { reason = "这个位置不能拖动"; return false; }
            return true;
        }

        public void OnDragBegin() { }

        public void OnDragCancelled() { }

        /// <summary>放下被接受：唯一业务入口（一次事务）。</summary>
        public void OnDragCommitted(SlotRef to)
        {
            var service = ItemSystem.Service;
            if (service == null) { return; }
            var result = service.TryMove(m_Slot, to);
            if (!result.Ok)
            {
                UiTooltip.ShowToast(UiDropTarget.DescribeReason(result.Reason));
            }
            Refresh();
        }

        // ==================== 指针事件 ====================

        public void OnBeginDrag(PointerEventData eventData)
        {
            string reason;
            if (!CanBeginDrag(out reason))
            {
                if (!string.IsNullOrEmpty(reason)) { UiTooltip.ShowToast(reason); }
                return;
            }
            var mgr = UiDragManager.Instance;
            if (mgr == null) { return; }
            var rect = transform as RectTransform;
            var icon = m_Icon != null ? m_Icon.sprite : null;
            mgr.Begin(this, eventData, icon, rect != null ? rect.sizeDelta : new Vector2(120f, 120f), rect);
        }

        public void OnDrag(PointerEventData eventData)
        {
            var mgr = UiDragManager.Instance;
            if (mgr != null) { mgr.UpdatePointer(eventData); }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            var mgr = UiDragManager.Instance;
            if (mgr != null && mgr.IsDragging) { mgr.End(eventData); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (m_Item.IsEmpty) { return; }

            // 拖拽中不弹详情（幽灵跟着指针走，再叠一个面板会互相遮挡）
            var dragMgr = UiDragManager.Instance;
            if (dragMgr != null && dragMgr.IsDragging) { return; }

            var service = ItemSystem.Service;
            var row = service != null ? service.Table.GetItem(m_Item.ItemId) : null;
            if (row == null) { return; }

            // S6：结构化详情面板（基础字段 / 给后续物品的修正 / buff / 被动 / 预算预估 / 已装序列）
            ItemDetailPanel.ShowItem(row, m_Item, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            UiTooltip.Hide();
            ItemDetailPanel.Hide();
        }

        private void OnDisable()
        {
            // 物体被隐藏/回收（列表刷新）时确保详情面板不残留
            ItemDetailPanel.Hide();
        }
    }
}
