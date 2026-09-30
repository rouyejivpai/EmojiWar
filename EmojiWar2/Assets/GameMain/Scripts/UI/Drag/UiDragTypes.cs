//------------------------------------------------------------
// EmojiWar GameMain - 拖拽 UI 基础类型（P3）
//
// 方案：doc/物品与法杖系统设计.md §5
//   与旧版最大的不同：拖拽只传**数据载荷**（SlotRef + 物品 Id/数量），
//   绝不把 GameObject 拖来拖去、也不在 Cell 里改业务数据 —— 业务统一回到 InventoryService 事务。
//------------------------------------------------------------

using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>拖拽载荷（值类型；来源槽 + 物品 + 数量）。</summary>
    public readonly struct DragPayload
    {
        public readonly SlotRef Source;
        public readonly int ItemId;
        public readonly int Count;

        public DragPayload(SlotRef source, int itemId, int count)
        {
            Source = source;
            ItemId = itemId;
            Count = count;
        }

        public static readonly DragPayload None = new DragPayload(default(SlotRef), 0, 0);
        public bool IsValid { get { return Source.IsValid && ItemId > 0 && Count > 0; } }
        public override string ToString() { return "payload(" + Source + " item#" + ItemId + " x" + Count + ")"; }
    }

    /// <summary>插槽高亮状态。</summary>
    public enum DropHighlight
    {
        None = 0,     // 常态
        Hover = 1,    // 指针悬停（可放）
        Valid = 2,    // 可放（拖拽中）
        Invalid = 3,  // 不可放（拖拽中）
    }

    /// <summary>可拖拽的 UI 元素（Cell 侧实现）。</summary>
    public interface IDragSource
    {
        DragPayload GetPayload();
        /// <summary>能否开始拖拽（空槽/只读/战斗中锁定 → false，reason 供提示）。</summary>
        bool CanBeginDrag(out string reason);
        void OnDragBegin();
        /// <summary>放下被拒（回弹）：UI 回到原位，不需要业务处理。</summary>
        void OnDragCancelled();
        /// <summary>放下被接受：业务在此调用 InventoryService 事务。</summary>
        void OnDragCommitted(SlotRef to);
    }
}
