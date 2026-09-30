//------------------------------------------------------------
// EmojiWar GameMain - 插槽落点契约（P3）
//
// 方案：doc/物品与法杖系统设计.md §5
//   插槽只声明"我是哪个槽（SlotRef）"，可放性一律问 InventoryService.CanMove
//   —— 规则只有一份，UI 高亮与真实结果永不漂移（旧版把规则写在插槽里，导致
//   "高亮能放、放下却失败"这类不一致）。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>插槽落点（挂在 Cell 根上）。</summary>
    [DisallowMultipleComponent]
    public sealed class UiDropTarget : MonoBehaviour
    {
        /// <summary>活动插槽注册表（射线未命中时的兜底搜索用）。</summary>
        public static readonly List<UiDropTarget> Registry = new List<UiDropTarget>();

        [SerializeField] private Image m_HighlightImage = null;      // 高亮底图（可为空则用自身 Image）
        [SerializeField] private Color m_HoverColor = new Color(1f, 1f, 0.4f, 0.35f);
        [SerializeField] private Color m_ValidColor = new Color(0.3f, 1f, 0.4f, 0.35f);
        [SerializeField] private Color m_InvalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);

        private Color m_NormalColor = Color.white;
        private bool m_HasNormal = false;

        public SlotRef Slot { get; private set; }
        public bool IsActiveTarget { get { return isActiveAndEnabled && Slot.IsValid; } }
        /// <summary>当前高亮状态（探针断言用）。</summary>
        public DropHighlight Highlight { get; private set; }

        private void OnEnable()
        {
            if (!Registry.Contains(this)) { Registry.Add(this); }
        }

        private void OnDisable()
        {
            Registry.Remove(this);
            SetHighlight(DropHighlight.None);
        }

        /// <summary>绑定到具体槽位（由 UiSlotContainer 调用）。</summary>
        public void Bind(SlotRef slot)
        {
            Slot = slot;
        }

        /// <summary>能否接受该载荷；reason 为中文提示（拒绝时给用户看）。</summary>
        public bool CanAccept(in DragPayload payload, out string reason)
        {
            reason = null;
            if (!Slot.IsValid) { reason = "槽位未绑定"; return false; }
            if (!payload.IsValid) { reason = "没有可拖拽的物品"; return false; }
            if (payload.Source.Equals(Slot)) { reason = "已经在原位置"; return false; }

            var service = ItemSystem.Service;
            if (service == null) { reason = "物品服务未就绪"; return false; }

            var result = service.CanMove(payload.Source, Slot);
            if (result == MoveReason.Ok) { return true; }
            reason = DescribeReason(result);
            return false;
        }

        /// <summary>插槽中心的世界坐标（吸附动画与兜底距离判定用）。</summary>
        public Vector3 GetWorldCenter()
        {
            var rect = transform as RectTransform;
            if (rect == null) { return transform.position; }
            return rect.TransformPoint(rect.rect.center);
        }

        /// <summary>高亮（幂等；不改变布局）。</summary>
        public void SetHighlight(DropHighlight state)
        {
            Highlight = state;
            var img = m_HighlightImage != null ? m_HighlightImage : GetComponent<Image>();
            if (img == null) { return; }
            if (!m_HasNormal)
            {
                m_NormalColor = img.color;
                m_HasNormal = true;
            }
            switch (state)
            {
                case DropHighlight.Hover: img.color = m_HoverColor; break;
                case DropHighlight.Valid: img.color = m_ValidColor; break;
                case DropHighlight.Invalid: img.color = m_InvalidColor; break;
                default: img.color = m_NormalColor; break;
            }
        }

        /// <summary>失败原因 → 玩家可读文案。</summary>
        public static string DescribeReason(MoveReason reason)
        {
            switch (reason)
            {
                case MoveReason.Full: return "这里放不下";
                case MoveReason.NotSpell: return "法杖槽只能放法术";
                case MoveReason.NotWand: return "手上只能拿法杖";
                case MoveReason.ReadOnlySlot: return "这个位置不能放";
                case MoveReason.EmptySource: return "没有可移动的物品";
                case MoveReason.TypeMismatch: return "类型不匹配";
                case MoveReason.IndexInvalid: return "槽位不存在";
                case MoveReason.SelfMove: return "已经在原位置";
                case MoveReason.NotStackable: return "这种物品不能分堆";
                default: return "不能放在这里";
            }
        }
    }
}
