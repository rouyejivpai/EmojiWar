//------------------------------------------------------------
// EmojiWar GameMain - 拖拽管理器（P3）
//
// 方案：doc/物品与法杖系统设计.md §5（手感细节照旧版已验证项，见 §5.2）
//   唯一拖拽会话：Begin → UpdatePointer → End。
//   · 幽灵 = 独立顶层 Canvas 上的图标副本（raycastTarget=false，不挡落点判定）；
//   · 落点判定：EventSystem 射线命中 UiDropTarget 优先，未命中则回落到"最近可接受插槽"（旧版重叠/距离思路）；
//   · 放下成功 → 通知来源（OnDragCommitted，由 Cell 调 InventoryService）；失败 → 回弹 + OnDragCancelled；
//   · 全程 Time.unscaledDeltaTime（暂停面板 timeScale=0 时也必须能拖）。
//------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>拖拽管理器（常驻单例；由 GameEntry 创建）。</summary>
    public sealed class UiDragManager : MonoBehaviour
    {
        private const string GhostCanvasName = "UiDragGhostCanvas";
        private const string GhostName = "UiDragGhost";
        private const int GhostSortingOrder = 30000;
        private const float NearestTargetMaxPixels = 90f;   // 射线未命中时的兜底半径（屏幕像素）

        private static UiDragManager s_Instance = null;

        [SerializeField] private float m_SnapDuration = 0.1f;    // 吸附动画时长（旧版 1/snapSpeed ≈ 0.1s）
        [SerializeField] private float m_ReturnDuration = 0.12f; // 回弹动画时长
        [SerializeField] private float m_GhostAlpha = 0.9f;
        [SerializeField] private float m_SourceDimAlpha = 0.35f;

        private Canvas m_GhostCanvas = null;
        private RectTransform m_GhostRect = null;
        private Image m_GhostImage = null;
        private CanvasGroup m_GhostGroup = null;

        private IDragSource m_Source = null;
        private DragPayload m_Payload = DragPayload.None;
        private UiDropTarget m_Target = null;
        private UiDropTarget m_LastValidTarget = null;
        private RectTransform m_SourceRect = null;
        private Vector2 m_GrabOffset = Vector2.zero;   // 指针 - 来源格中心（屏幕像素）
        private float m_SourceOriginalAlpha = 1f;
        private Coroutine m_Anim = null;

        public static UiDragManager Instance { get { return s_Instance; } }
        public bool IsDragging { get { return m_Source != null; } }
        public DragPayload Current { get { return m_Payload; } }
        public UiDropTarget CurrentTarget { get { return m_Target; } }

        /// <summary>确保存在（GameEntry 调用一次）。</summary>
        public static UiDragManager Ensure(GameObject host)
        {
            if (s_Instance != null) { return s_Instance; }
            if (host == null) { return null; }
            var mgr = host.GetComponent<UiDragManager>();
            if (mgr == null) { mgr = host.AddComponent<UiDragManager>(); }
            return s_Instance;
        }

        private void Awake()
        {
            s_Instance = this;
        }

        private void OnDestroy()
        {
            if (s_Instance == this) { s_Instance = null; }
        }

        // ==================== 会话 ====================

        /// <summary>开始拖拽（返回 false 表示不允许）。</summary>
        public bool Begin(IDragSource source, PointerEventData e, Sprite icon, Vector2 size, RectTransform sourceRect)
        {
            if (source == null || IsDragging) { return false; }
            string reason;
            if (!source.CanBeginDrag(out reason)) { return false; }

            m_Source = source;
            m_Payload = source.GetPayload();
            if (!m_Payload.IsValid) { m_Source = null; return false; }

            m_SourceRect = sourceRect;
            m_LastValidTarget = null;
            m_Target = null;

            // 抓取点偏移（屏幕像素）：以来源格中心为基准
            m_GrabOffset = Vector2.zero;
            if (m_SourceRect != null)
            {
                Vector2 center = RectTransformUtility.WorldToScreenPoint(null, m_SourceRect.TransformPoint(m_SourceRect.rect.center));
                m_GrabOffset = e.position - center;
            }

            CreateGhost(icon, size);
            if (m_SourceRect != null)
            {
                var group = m_SourceRect.GetComponent<CanvasGroup>();
                if (group == null) { group = m_SourceRect.gameObject.AddComponent<CanvasGroup>(); }
                m_SourceOriginalAlpha = group.alpha;
                group.alpha = m_SourceDimAlpha;
            }

            source.OnDragBegin();
            UpdatePointer(e);
            return true;
        }

        /// <summary>拖拽中：幽灵跟随 + 落点高亮。</summary>
        public void UpdatePointer(PointerEventData e)
        {
            if (!IsDragging || m_Anim != null) { return; }

            MoveGhostToPointer(e);

            var target = FindTarget(e);
            if (target != m_Target)
            {
                if (m_Target != null) { m_Target.SetHighlight(DropHighlight.None); }
                m_Target = target;
                if (m_Target != null)
                {
                    m_LastValidTarget = m_Target;
                    m_Target.SetHighlight(DropHighlight.Valid);
                }
            }
        }

        /// <summary>结束拖拽：命中则提交，否则回弹。</summary>
        public void End(PointerEventData e)
        {
            if (!IsDragging) { return; }

            var source = m_Source;
            var target = m_Target;
            m_Source = null;
            m_Target = null;
            ClearHighlights();
            RestoreSourceAlpha();

            Vector2 from = m_GhostRect != null ? m_GhostRect.anchoredPosition : Vector2.zero;
            Vector2 to = from;

            if (target != null && target.CanAccept(m_Payload, out _))
            {
                var slot = target.Slot;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(), target.GetWorldCenter());
                to = ScreenToGhostLocal(screen, e);
                StartGhostAnim(to, m_SnapDuration, () =>
                {
                    if (source != null) { source.OnDragCommitted(slot); }
                    DestroyGhost();
                });
                return;
            }

            // 回弹：回到来源槽（失败也给用户明确反馈，绝不静默）
            if (m_SourceRect != null)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(), m_SourceRect.TransformPoint(m_SourceRect.rect.center));
                to = ScreenToGhostLocal(screen, e);
            }
            StartGhostAnim(to, m_ReturnDuration, () =>
            {
                if (source != null) { source.OnDragCancelled(); }
                DestroyGhost();
            });
            UiTooltip.ShowToast("不能放在这里");
        }

        /// <summary>取消（面板关闭/场景切换时调用）。</summary>
        public void Cancel()
        {
            var source = m_Source;
            m_Source = null;
            m_Target = null;
            ClearHighlights();
            RestoreSourceAlpha();
            DestroyGhost();
            if (source != null) { source.OnDragCancelled(); }
        }

        // ==================== 落点判定 ====================

        /// <summary>指针下的插槽：EventSystem 射线优先，命中不到则取最近的可接受插槽。</summary>
        public UiDropTarget FindTarget(PointerEventData e)
        {
            if (e == null) { return null; }

            // 1) 射线命中
            if (EventSystem.current != null)
            {
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(e, results);
                for (int i = 0; i < results.Count; i++)
                {
                    var go = results[i].gameObject;
                    if (go == null) { continue; }
                    var target = go.GetComponentInParent<UiDropTarget>();
                    if (target != null && target.IsActiveTarget) { return target; }
                }
            }

            // 2) 兜底：屏幕距离最近的可接受插槽（旧版"最近目标"思路，带半径上限）
            UiDropTarget best = null;
            float bestDist = NearestTargetMaxPixels;
            var registry = UiDropTarget.Registry;
            for (int i = 0; i < registry.Count; i++)
            {
                var t = registry[i];
                if (t == null || !t.IsActiveTarget) { continue; }
                string reason;
                if (!t.CanAccept(m_Payload, out reason)) { continue; }
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(), t.GetWorldCenter());
                float dist = Vector2.Distance(screen, e.position);
                if (dist < bestDist) { bestDist = dist; best = t; }
            }
            return best;
        }

        private void ClearHighlights()
        {
            var registry = UiDropTarget.Registry;
            for (int i = 0; i < registry.Count; i++)
            {
                if (registry[i] != null) { registry[i].SetHighlight(DropHighlight.None); }
            }
        }

        // ==================== 幽灵 ====================

        private void CreateGhost(Sprite icon, Vector2 size)
        {
            if (m_GhostCanvas == null)
            {
                var canvasGo = new GameObject(GhostCanvasName, typeof(Canvas), typeof(CanvasGroup));
                canvasGo.transform.SetParent(transform, false);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = GhostSortingOrder;
                m_GhostCanvas = canvas;
            }

            var go = new GameObject(GhostName, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(m_GhostCanvas.transform, false);
            m_GhostRect = go.GetComponent<RectTransform>();
            // 锚点必须是 (0.5,0.5)：ScreenPointToLocalPointInRectangle 返回的是相对 rect 枢轴的坐标，
            // 之前用 (0,0) 左下角锚点会导致"拖拽物偏出半个屏幕"。
            m_GhostRect.anchorMin = new Vector2(0.5f, 0.5f);
            m_GhostRect.anchorMax = new Vector2(0.5f, 0.5f);
            m_GhostRect.pivot = new Vector2(0.5f, 0.5f);
            m_GhostRect.sizeDelta = size;

            m_GhostImage = go.GetComponent<Image>();
            m_GhostImage.sprite = icon;
            m_GhostImage.preserveAspect = true;
            m_GhostImage.raycastTarget = false;   // 幽灵不挡落点判定

            m_GhostGroup = go.GetComponent<CanvasGroup>();
            m_GhostGroup.alpha = m_GhostAlpha;
            m_GhostGroup.blocksRaycasts = false;
            m_GhostGroup.interactable = false;
        }

        private void DestroyGhost()
        {
            m_Anim = null;
            var ghost = m_GhostRect != null ? m_GhostRect.gameObject : null;
            m_GhostRect = null;
            m_GhostImage = null;
            m_GhostGroup = null;
            if (ghost != null) { Destroy(ghost); }
        }

        private Camera GetCanvasCamera()
        {
            return m_GhostCanvas != null && m_GhostCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? m_GhostCanvas.worldCamera
                : null;
        }

        private Vector2 ScreenToGhostLocal(Vector2 screen, PointerEventData e)
        {
            if (m_GhostCanvas == null) { return screen; }
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                m_GhostCanvas.transform as RectTransform, screen,
                e != null ? e.pressEventCamera : GetCanvasCamera(), out local);
            return local;
        }

        private void MoveGhostToPointer(PointerEventData e)
        {
            if (m_GhostRect == null) { return; }
            // 保留抓取点：拖拽物相对指针的偏移在按下时固定，避免"一跳就贴到指针中心"
            Vector2 screen = e.position - m_GrabOffset;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                m_GhostCanvas.transform as RectTransform, screen, e.pressEventCamera, out local))
            {
                m_GhostRect.anchoredPosition = local;
            }
        }

        private void StartGhostAnim(Vector2 to, float duration, System.Action onDone)
        {
            if (m_GhostRect == null)
            {
                if (onDone != null) { onDone(); }
                return;
            }
            m_Anim = StartCoroutine(AnimGhost(to, Mathf.Max(0.01f, duration), onDone));
        }

        private IEnumerator AnimGhost(Vector2 to, float duration, System.Action onDone)
        {
            Vector2 from = m_GhostRect.anchoredPosition;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;   // 暂停面板（timeScale=0）下也要能动
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                m_GhostRect.anchoredPosition = Vector2.Lerp(from, to, k);
                if (m_GhostGroup != null) { m_GhostGroup.alpha = Mathf.Lerp(m_GhostAlpha, 0f, k * 0.5f); }
                yield return null;
            }
            m_GhostRect.anchoredPosition = to;
            m_Anim = null;
            if (onDone != null) { onDone(); }
        }

        private void RestoreSourceAlpha()
        {
            if (m_SourceRect == null) { return; }
            var group = m_SourceRect.GetComponent<CanvasGroup>();
            if (group != null) { group.alpha = m_SourceOriginalAlpha; }
            m_SourceRect = null;
        }
    }
}
