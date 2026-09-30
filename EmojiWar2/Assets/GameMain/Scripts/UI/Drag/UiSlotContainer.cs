//------------------------------------------------------------
// EmojiWar GameMain - 插槽容器视图（P3）
//
// 方案：doc/物品与法杖系统设计.md §5 + doc/UI列表与Cell规范.md
//   规范照旧：① 容器下放 Cell 预制体（UiListCell 取用为行源）；② 先清空再生成；
//             ③ GfUiItemPool 池化；④ 服务 SlotDirty → 只刷新变化的那一格。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>把一个 ItemContainer 映射成一组 UiSlotView 子物体。</summary>
    public sealed class UiSlotContainer : MonoBehaviour
    {
        [SerializeField] private string m_ContainerId = "Backpack";
        [Tooltip("回落用的 Cell 预制体路径（容器下已放模板时以模板为准）")]
        [SerializeField] private string m_CellItemPath = Constant.UIItemAssetPath.SlotCell;
        [Tooltip("单槽容器（手部）的键位标签，如 左键/右键；留空则不显示")]
        [SerializeField] private string m_KeyLabel = "";
        [Tooltip("槽位底色策略：Content=按当前物品类别（法杖/法术/空）；Wand=固定法杖色；Spell=固定法术色")]
        [SerializeField] private SlotTintMode m_TintMode = SlotTintMode.Content;
        [SerializeField] private bool m_AutoBindOnEnable = true;

        private readonly List<UiSlotView> m_Cells = new List<UiSlotView>();
        private GfUiItemPool m_Pool = null;
        private GameObject m_CellSource = null;
        private RectTransform m_Rect = null;
        private ItemContainer m_Container = null;
        private bool m_Subscribed = false;

        public string ContainerId { get { return m_ContainerId; } }
        public int CellCount { get { return m_Cells.Count; } }

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

        /// <summary>覆盖本容器所有 Cell 的键位标签（运行时用，如"主武器 · 左键"）。</summary>
        public void SetKeyLabel(string label)
        {
            m_KeyLabel = label;
            for (int i = 0; i < m_Cells.Count; i++)
            {
                if (m_Cells[i] != null) { m_Cells[i].SetKeyLabel(label); }
            }
        }

        /// <summary>取某格的 Cell（越界返回 null；探针/自动化测试用）。</summary>
        public UiSlotView GetCell(int index)
        {
            return index >= 0 && index < m_Cells.Count ? m_Cells[index] : null;
        }

        /// <summary>当前绑定的容器（未绑定时为 null）。</summary>
        public ItemContainer Container { get { return m_Container; } }

        private void OnEnable()
        {
            if (m_AutoBindOnEnable) { Bind(m_ContainerId); }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ClearCells();
            if (m_Pool != null) { m_Pool.Destroy(); m_Pool = null; }
            UiListCell.Release(ref m_CellSource);
        }

        /// <summary>绑定容器并生成 Cell（幂等；重复调用=先清空再生成）。</summary>
        public void Bind(string containerId)
        {
            if (!string.IsNullOrEmpty(containerId)) { m_ContainerId = containerId; }
            if (m_Rect == null) { m_Rect = transform as RectTransform; }

            var service = ItemSystem.Service;
            m_Container = service != null ? service.GetContainer(m_ContainerId) : null;
            if (m_Container == null)
            {
                // 绑定失败必须可见（否则视图会继续显示预制体里带的默认数据，
                // 表现成"两手法术槽同步"这类难查的问题）
                WriteProbe("[uislot] 绑定失败：容器不存在 id=" + m_ContainerId + "（节点 " + gameObject.name + "）");
                return;
            }

            // 行源：容器下预放的 Cell 预制体（规范 §一.3）优先，否则 items/ 路径回落
            m_CellSource = UiListCell.Resolve(m_Rect, m_ContainerId, m_CellItemPath, m_CellSource, () => Bind(m_ContainerId));
            if (m_CellSource == null) { return; }

            if (m_Pool == null) { m_Pool = GfUiItemPool.Create("UiSlots_" + m_ContainerId); }

            ClearCells();   // 规范：先清空再生成

            // Cell 数量 = 容器"可用槽数"（法杖法术槽的可用槽数随法杖变化；**无法杖 = 0 槽**）
            int cellCount = m_Container.ActiveCapacity > 0 ? m_Container.ActiveCapacity : 0;
            if (cellCount <= 0)
            {
                // 0 槽：一个格子都不生成，整块隐藏
                gameObject.SetActive(false);
                WriteProbe("[uislot] " + m_ContainerId + " 可用槽数 0 → 不生成任何格子");
                Subscribe();
                return;
            }
            for (int i = 0; i < cellCount; i++)
            {
                var go = m_Pool != null
                    ? m_Pool.Acquire(() => Instantiate(m_CellSource), m_Rect)
                    : Instantiate(m_CellSource, m_Rect);
                if (go == null) { break; }
                go.name = m_ContainerId + "_Slot_" + i;

                var view = go.GetComponent<UiSlotView>();
                if (view == null)
                {
                    Debug.LogWarning("[UiSlotContainer] Cell 预制体缺少 UiSlotView: " + go.name);
                    continue;
                }
                view.Bind(new SlotRef(m_ContainerId, i), string.IsNullOrEmpty(m_KeyLabel) ? null : m_KeyLabel);
                view.SetTintMode(ResolveTintMode());
                m_Cells.Add(view);
            }

            Subscribe();
            BringNestedContainersToFront();
        }

        /// <summary>
        /// 容器底色策略：法术槽 → 固定法术色；手部武器槽 → 固定法杖色；其余（背包网格）→ 按内容类别。
        /// </summary>
        private SlotTintMode ResolveTintMode()
        {
            if (m_TintMode != SlotTintMode.Content) { return m_TintMode; }
            if (m_Container == null) { return SlotTintMode.Content; }
            if (m_Container.Kind == SlotKind.WandSpellSlots) { return SlotTintMode.Spell; }
            if (m_Container.Kind == SlotKind.WandHand) { return SlotTintMode.Wand; }
            return SlotTintMode.Content;
        }

        /// <summary>
        /// 嵌套子容器（武器槽 → 法术槽子列表）必须排在运行时生成的 Cell **之后**，
        /// 否则后建的 Cell 会盖住子列表（射线打到父插槽、法术槽点不到）。
        /// </summary>
        private void BringNestedContainersToFront()
        {
            if (m_Rect == null) { return; }
            for (int i = 0; i < m_Rect.childCount; i++)
            {
                var child = m_Rect.GetChild(i);
                if (child != null && child.GetComponent<UiSlotContainer>() != null)
                {
                    child.SetAsLastSibling();
                }
            }
        }

        /// <summary>清空容器内所有 Cell（池内行回收 → 隐藏根；其余直接销毁）。</summary>
        private void ClearCells()
        {
            for (int i = m_Cells.Count - 1; i >= 0; i--)
            {
                var view = m_Cells[i];
                if (view == null) { continue; }
                var go = view.gameObject;
                view.Bind(default(SlotRef));   // 先解绑，避免回收后还被刷新
                if (m_Pool != null) { m_Pool.Recycle(go); } else { Destroy(go); }
            }
            m_Cells.Clear();

            if (m_Rect != null)
            {
                for (int i = m_Rect.childCount - 1; i >= 0; i--)
                {
                    var child = m_Rect.GetChild(i);
                    if (child == null) { continue; }
                    // 嵌套子容器（武器槽 → 法术槽子列表）不属于本容器的 Cell/残留，保留
                    if (child.GetComponent<UiSlotContainer>() != null) { continue; }
                    Destroy(child.gameObject);   // 模板/残留一律清掉
                }
            }
        }

        private void Subscribe()
        {
            if (m_Subscribed) { return; }
            var service = ItemSystem.Service;
            if (service == null) { return; }
            service.SlotDirty += OnSlotDirty;
            m_Subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed) { return; }
            var service = ItemSystem.Service;
            if (service != null) { service.SlotDirty -= OnSlotDirty; }
            m_Subscribed = false;
        }

        /// <summary>只刷新变化的那一格（避免整列表重建）。</summary>
        private void OnSlotDirty(SlotRef slot)
        {
            if (!string.Equals(slot.ContainerId, m_ContainerId, System.StringComparison.Ordinal)) { return; }
            if (slot.Index < 0 || slot.Index >= m_Cells.Count) { return; }
            var view = m_Cells[slot.Index];
            if (view != null) { view.Refresh(); }
        }

        /// <summary>全量刷新（面板打开时调用）。</summary>
        public void RefreshAll()
        {
            for (int i = 0; i < m_Cells.Count; i++)
            {
                if (m_Cells[i] != null) { m_Cells[i].Refresh(); }
            }
        }
    }
}
