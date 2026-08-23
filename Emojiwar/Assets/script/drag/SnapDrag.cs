using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;

public class SnapDrag : drag
{
    [Header("吸附设置")]
    public float snapDistance = 50f;  // 吸附距离（仅在目标未启用边界距离时使用）
    public bool autoSnap = true;  // 是否自动吸附
    public float snapSpeed = 10f;  // 吸附速度
    public bool returnToOriginal = true;  // 无法吸附时是否返回原位置
    public bool useOverlapSelection = true; // 是否使用矩形重叠选择目标
    
    [Header("偏移设置")]
    [Tooltip("仅在拖拽过程中：存在吸附目标时，向其施加轻微靠拢偏移。")]
    public bool enableGuidanceOffset = true; // 拖拽过程的引导偏移开关
    [Tooltip("引导偏移的像素幅度（Canvas局部坐标空间）。")]
    public float guidanceOffsetPixels = 8f; // 引导偏移像素量
    [Tooltip("引导偏移的插值因子，越大越快速接近偏移目标。")]
    [Range(0.05f, 1f)]
    public float guidanceSmoothFactor = 0.35f; // 引导偏移的平滑系数
    [Tooltip("有目标时偏移进度每秒增长量（越大越快逼近1）。")]
    public float guidanceProgressIncrease = 4f; // 进度递增速度
    [Tooltip("无目标时偏移进度每秒衰减量（越大越快回到0）。")]
    public float guidanceProgressDecrease = 6f; // 进度递减速度
    // 运行时状态：偏移进度(0-1)与最近方向
    private float guidanceProgress = 0f;
    private Vector2 guidanceDirection = Vector2.zero;
    // 与布局组协作：拖拽期间忽略布局，落地后恢复
    private LayoutElement layoutElement;
    private bool originalIgnoreLayout = false;
    private ContentSizeFitter contentSizeFitter;
    private bool contentSizeFitterWasEnabled = false;
    // 锚点与枢轴的快照与控制
    private Vector2 originalAnchorMin;
    private Vector2 originalAnchorMax;
    private Vector2 originalPivot;
    private bool anchorsAdjustedDuringDrag = false;
    
    [Header("吸附目标")]
    public List<DragTarget> validTargets = new List<DragTarget>();  // 有效的吸附目标
    
    private DragTarget currentTarget;  // 当前吸附目标
    public DragTarget lastTarget;  // 上次吸附目标
    // 记录上一次被接收的目标，用于在拖拽开始时可靠移除
    private DragTarget lastAcceptedTarget = null;
    
    private bool isSnapping = false;  // 是否正在吸附
    private Vector2 snapPosition;  // 吸附位置

    [Header("目标额外限定")]
    [Tooltip("启用后仅允许吸附到具有指定Tag的目标。")]
    public bool onlyAllowTargetsWithTag = false;
    [Tooltip("允许的目标Tag（留空则不生效）。")]
    public string allowedTargetTag = "";
    [Tooltip("当外部手动设置目标列表时，是否也应用Tag过滤。")]
    public bool filterManualTargets = false;
    [Tooltip("在每次拖拽开始时是否重新应用Tag过滤（适用于动态生成/激活的目标）。")]
    public bool reapplyTagFilterOnDragBegin = false;

    [Header("初始化登记")]
    [Tooltip("启动时若在某个DragTarget层级下，将其登记为已接受（静默）并记录为lastAcceptedTarget。")]
    public bool registerExistingOnStart = true;

    [Header("生命周期事件")]
    public UnityEvent<SnapDrag> onDragBegin = new UnityEvent<SnapDrag>();
    public UnityEvent<SnapDrag> onDragEnd = new UnityEvent<SnapDrag>();
    public UnityEvent<SnapDrag, DragTarget> onSnapStarted = new UnityEvent<SnapDrag, DragTarget>();
    public UnityEvent<SnapDrag, DragTarget> onSnapSucceeded = new UnityEvent<SnapDrag, DragTarget>();
    public UnityEvent<SnapDrag, DragTarget> onSnapFailed = new UnityEvent<SnapDrag, DragTarget>();
    public UnityEvent<SnapDrag> onReturnStarted = new UnityEvent<SnapDrag>();
    public UnityEvent<SnapDrag> onReturnCompleted = new UnityEvent<SnapDrag>();
    
    protected override void Start()
    {
        base.Start();
        
        // 如果没有指定目标，自动查找场景中的所有DragTarget
        if (validTargets.Count == 0)
        {
            DragTarget[] allTargets = FindObjectsOfType<DragTarget>();
            List<DragTarget> list = new List<DragTarget>(allTargets);
            if (onlyAllowTargetsWithTag && !string.IsNullOrEmpty(allowedTargetTag))
            {
                int before = list.Count;
                list = list.FindAll(t => t != null && t.CompareTag(allowedTargetTag));
               // Debug.Log($"按Tag '{allowedTargetTag}' 过滤目标，保留 {list.Count}/{before}");
            }
            validTargets.AddRange(list);
            ///Debug.Log($"自动找到 {list.Count} 个DragTarget目标");
        }
        else
        {
            if (filterManualTargets && onlyAllowTargetsWithTag && !string.IsNullOrEmpty(allowedTargetTag))
            {
                int before = validTargets.Count;
                validTargets = validTargets.FindAll(t => t != null && t.CompareTag(allowedTargetTag));
                //Debug.Log($"对手动指定目标应用Tag过滤，保留 {validTargets.Count}/{before}");
            }
            else
            {
                //Debug.Log($"使用手动指定的 {validTargets.Count} 个目标");
            }
        }
        
        // 验证所有目标
        for (int i = 0; i < validTargets.Count; i++)
        {
            if (validTargets[i] == null)
            {
                Debug.LogWarning($"目标 {i} 为空引用！");
            }
            else
            {
                Debug.Log($"目标 {i}: {validTargets[i].name}");
            }
        }

        // 启动时：若本物体处于某个DragTarget层级下，进行静默登记，确保移除事件链条能工作
        if (registerExistingOnStart)
        {
            DragTarget parentTarget = GetComponentInParent<DragTarget>();
            if (parentTarget != null)
            {
                if (!parentTarget.ContainsItem(this))
                {
                    //Debug.Log($"初始化：在父层级目标 {parentTarget.name} 下，登记为已接受（静默）。");
                    parentTarget.RegisterExistingItem(this, false);
                }
                // 记录为最近的接受目标，便于拖拽开始时可靠移除
                lastAcceptedTarget = parentTarget;
            }
        }
    }
    
    public override void OnBeginDrag(PointerEventData eventData)
    {
        lastTarget = transform.parent.GetComponent<DragTarget>();
        base.OnBeginDrag(eventData);
        
        // 重置吸附状态
        isSnapping = false;
        currentTarget = null;
        
        // 清理所有目标的预览状态，避免残留
        foreach (DragTarget target in validTargets)
        {
            if (target != null)
            {
                target.HideSnapPreview();
            }
        }
        // 重置偏移进度与方向
        guidanceProgress = 0f;
        guidanceDirection = Vector2.zero;
        
        // 注意：originalPosition 已经在基类中记录了，不需要重新记录
        //Debug.Log($"拖拽开始 - 原始位置: {originalPosition}, 原始父对象: {originalParent?.name}");
        //Debug.Log($"当前父对象: {transform.parent?.name}, 当前位置: {rectTransform.anchoredPosition}");
        
        // 优先从上次接受的目标中移除（不受Tag过滤与目标列表变化影响）
        if (lastAcceptedTarget != null && lastAcceptedTarget.ContainsItem(this))
        {
            //Debug.Log($"从上次接受的目标 {lastAcceptedTarget.name} 中移除物品");
            lastAcceptedTarget.RemoveDragItem(this);
            lastAcceptedTarget = null;
        }
        
        // 如果当前在某个目标中，先从目标中移除
        foreach (DragTarget target in validTargets)
        {
            if (target.ContainsItem(this))
            {
                ///Debug.Log($"从目标 {target.name} 中移除物品");
                target.RemoveDragItem(this);
            }
        }
        
        // 通知所有目标开始拖拽
        foreach (DragTarget target in validTargets)
        {
            target.OnDragStart(this);
        }

        // 拖拽期间：忽略布局以避免与布局组冲突
        layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = gameObject.AddComponent<LayoutElement>();
        }
        originalIgnoreLayout = layoutElement.ignoreLayout;
        layoutElement.ignoreLayout = true;

        // 禁用子物体上的 ContentSizeFitter，避免其继续驱动 RectTransform
        contentSizeFitter = GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
        {
            contentSizeFitterWasEnabled = contentSizeFitter.enabled;
            if (contentSizeFitter.enabled)
            {
                contentSizeFitter.enabled = false;
            }
        }

        // 强制将拖拽物体置于 Canvas 下，彻底脱离布局组驱动
        if (canvas != null && transform.parent != canvas.transform)
        {
            Transform prevParent = transform.parent;
            transform.SetParent(canvas.transform, true);
            // 标记并强制重建上一父容器的布局，释放驱动器
            RectTransform prevParentRect = prevParent as RectTransform;
            if (prevParentRect != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(prevParentRect);
                LayoutRebuilder.ForceRebuildLayoutImmediate(prevParentRect);
            }
        }

        // 快照并临时改为非拉伸锚点（居中），避免拉伸导致 sizeDelta/anchoredPosition 非直观
        originalAnchorMin = rectTransform.anchorMin;
        originalAnchorMax = rectTransform.anchorMax;
        originalPivot = rectTransform.pivot;
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        // 保持原始枢轴，减少视觉偏移
        rectTransform.pivot = originalPivot;
        anchorsAdjustedDuringDrag = true;

        // 如需在拖拽开始时重应用Tag过滤（目标可能在运行时变化）
        if (reapplyTagFilterOnDragBegin && onlyAllowTargetsWithTag && !string.IsNullOrEmpty(allowedTargetTag))
        {
            int before = validTargets.Count;
            validTargets = validTargets.FindAll(t => t != null && t.CompareTag(allowedTargetTag));
            //Debug.Log($"拖拽开始时重应用Tag过滤，保留 {validTargets.Count}/{before}");
        }

        // 事件：拖拽开始
        onDragBegin?.Invoke(this);
    }
    
    public override void OnDrag(PointerEventData eventData)
    {
        // 如果正在吸附，不执行拖拽
        if (isSnapping) return;
        // 子类自行计算鼠标局部坐标，并在末尾统一写入位置（鼠标+偏移）
        if (!canDrag) return;
        RectTransform canvasRect = canvas != null ? (canvas.transform as RectTransform) : null;
        if (canvasRect == null) return;
        Camera cam = canvas != null ? canvas.worldCamera : null;
        Vector2 pointerLocal;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            eventData.position,
            eventData.pressEventCamera,
            out pointerLocal))
        {
            return;
        }

        // 临时将位置设为鼠标局部坐标，以便选择逻辑基于最新位置
        rectTransform.anchoredPosition = pointerLocal;

        // 检查是否有可吸附的目标（基于当前帧已更新位置）
        DragTarget nearestTarget = GetNearestValidTarget();
        
        if (nearestTarget != null && autoSnap)
        {
            // 仅保留当前最佳目标的预览，其他全部隐藏
            foreach (DragTarget t in validTargets)
            {
                if (t != null && t != nearestTarget)
                {
                    t.HideSnapPreview();
                }
            }
            
            // 显示当前最佳目标的预览
            nearestTarget.ShowSnapPreview(this);
            currentTarget = nearestTarget;
            // 仅拖拽过程偏移：按进度系数逐渐增加偏移（不直接插值到目标）
            if (enableGuidanceOffset && guidanceOffsetPixels > 0f)
            {
                RectTransform targetRect = nearestTarget.GetComponent<RectTransform>();
                if (targetRect != null)
                {
                    // 计算目标中心的Canvas局部坐标
                    Vector2 targetScreenPos = RectTransformUtility.WorldToScreenPoint(cam, targetRect.position);
                    Vector2 targetLocalPoint;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, targetScreenPos, cam, out targetLocalPoint))
                    {
                        Vector2 dir = (targetLocalPoint - pointerLocal);
                        if (dir.sqrMagnitude > 0.0001f)
                        {
                            guidanceDirection = dir.normalized;
                        }
                        // 增加进度（向1逼近）
                        guidanceProgress = Mathf.Min(1f, guidanceProgress + guidanceProgressIncrease * Time.unscaledDeltaTime);
                    }
                }
            }
            
            // 调试信息
            float distance = Vector2.Distance(rectTransform.anchoredPosition, nearestTarget.GetSnapPosition());
            //Debug.Log($"拖拽中 - 当前位置: {rectTransform.anchoredPosition}, 目标位置: {nearestTarget.GetSnapPosition()}, 距离: {distance}");
        }
        else
        {
            // 无可选目标时，隐藏所有预览
            foreach (DragTarget t in validTargets)
            {
                if (t != null)
                {
                    t.HideSnapPreview();
                }
            }
            currentTarget = null;
            // 无目标：进度逐渐衰减到0
            if (enableGuidanceOffset && guidanceOffsetPixels > 0f)
            {
                guidanceProgress = Mathf.Max(0f, guidanceProgress - guidanceProgressDecrease * Time.unscaledDeltaTime);
            }
        }

        // 统一应用最终位置：鼠标局部坐标 + 偏移量
        if (enableGuidanceOffset && guidanceOffsetPixels > 0f)
        {
            Vector2 offset = guidanceDirection * guidanceOffsetPixels * guidanceProgress;
            rectTransform.anchoredPosition = pointerLocal + offset;
        }
        else
        {
            rectTransform.anchoredPosition = pointerLocal;
        }
    }
    
    public override void OnEndDrag(PointerEventData eventData)
    {
        base.OnEndDrag(eventData);
        
//        Debug.Log($"拖拽结束 - 当前目标: {(currentTarget != null ? currentTarget.name : "无")}");
        
        // 尝试吸附到目标
        if (currentTarget != null && CanSnapToTarget(currentTarget))
        {
            //Debug.Log($"开始吸附到目标: {currentTarget.name}");
            SnapToTarget(currentTarget);
        }
        else if (returnToOriginal)
        {
            //Debug.Log("无法吸附，返回原位置");
            // 返回原位置
            // 事件：吸附失败（尝试返回）
            onSnapFailed?.Invoke(this, currentTarget);
            SnapToTarget(lastTarget);
            //StartCoroutine(ReturnToOriginalPosition());
        }
        else
        {
            Debug.Log("无法吸附且不返回原位置");
            // 兜底：未吸附且不返回，恢复 ignoreLayout 到拖拽前状态
            if (layoutElement == null)
            {
                layoutElement = GetComponent<LayoutElement>();
            }
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = originalIgnoreLayout;
            }
            // 恢复 ContentSizeFitter 状态
            if (contentSizeFitter != null)
            {
                contentSizeFitter.enabled = contentSizeFitterWasEnabled;
            }
            // 事件：吸附失败（不返回）
            onSnapFailed?.Invoke(this, currentTarget);
        }
        
        // 通知所有目标结束拖拽
        foreach (DragTarget target in validTargets)
        {
            target.OnDragEnd(this);
        }
        
        // 结束时统一隐藏所有预览，避免残留
        foreach (DragTarget target in validTargets)
        {
            if (target != null)
            {
                target.HideSnapPreview();
            }
        }

        // 事件：拖拽结束
        onDragEnd?.Invoke(this);
    }
    
    // 获取最近的有效目标
    private DragTarget GetNearestValidTarget()
    {
//        Debug.Log($"检查 {validTargets.Count} 个目标");

        if (useOverlapSelection)
        {
            DragTarget bestTarget = null;
            float bestOverlapArea = 0f;
            Rect dragRect = GetDragScreenRect();
//            Debug.Log($"拖拽物屏幕Rect: {dragRect}");
            foreach (DragTarget target in validTargets)
            {
                // 跳过空引用或未激活/未启用的目标
                if (target == null)
                {
                    Debug.LogWarning("发现空的目标引用！");
                    continue;
                }
                if (!target.isActiveAndEnabled || !target.gameObject.activeInHierarchy)
                {
                    continue;
                }

                bool canAccept = target.CanAcceptDrag(this);
                Rect targetRect = target.GetScreenRect(canvas.worldCamera);
                float overlapArea = ComputeOverlapArea(dragRect, targetRect);
//                Debug.Log($"目标 {target.name}: 可接受={canAccept}, 重叠面积={overlapArea:F2}");
                if (canAccept && overlapArea > 0f && overlapArea > bestOverlapArea)
                {
                    bestOverlapArea = overlapArea;
                    bestTarget = target;
                }
            }
//            Debug.Log($"最终选择的目标: {(bestTarget != null ? bestTarget.name : "无")}, 最大重叠: {bestOverlapArea:F2}");
            return bestTarget;
        }
        else
        {
            DragTarget nearestTarget = null;
            float nearestDistance = float.MaxValue;
            Vector2 dragScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rectTransform.position);
            foreach (DragTarget target in validTargets)
            {
                // 跳过空引用或未激活/未启用的目标
                if (target == null)
                {
                    Debug.LogWarning("发现空的目标引用！");
                    continue;
                }
                if (!target.isActiveAndEnabled || !target.gameObject.activeInHierarchy)
                {
                    continue;
                }

                bool canAccept = target.CanAcceptDrag(this);
                RectTransform targetRect = target.GetComponent<RectTransform>();
                Vector2 targetScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, targetRect.position);
                float screenDistance = Vector2.Distance(dragScreenPos, targetScreenPos);
                float targetSnapDistance = target.GetSnapDistance();
                if (canAccept && screenDistance <= targetSnapDistance && screenDistance < nearestDistance)
                {
                    nearestTarget = target;
                    nearestDistance = screenDistance;
                }
            }
            Debug.Log($"最终选择的目标(距离): {(nearestTarget != null ? nearestTarget.name : "无")}");
            return nearestTarget;
        }
    }
    
    // 检查是否可以吸附到目标
    private bool CanSnapToTarget(DragTarget target)
    {
        if (target == null) return false;
        if (useOverlapSelection)
        {
            Rect dragRect = GetDragScreenRect();
            Rect targetRect = target.GetScreenRect(canvas.worldCamera);
            float overlapArea = ComputeOverlapArea(dragRect, targetRect);
            bool canSnap = overlapArea > 0f && target.CanAcceptDrag(this);
//            Debug.Log($"CanSnapToTarget检查(重叠): {target.name}, 重叠面积: {overlapArea:F2}, 可吸附: {canSnap}");
            return canSnap;
        }
        else
        {
            Vector2 dragScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rectTransform.position);
            RectTransform targetRect = target.GetComponent<RectTransform>();
            Vector2 targetScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, targetRect.position);
            float screenDistance = Vector2.Distance(dragScreenPos, targetScreenPos);
            float targetSnapDistance = target.GetSnapDistance();
            bool canSnap = screenDistance <= targetSnapDistance && target.CanAcceptDrag(this);
            //Debug.Log($"CanSnapToTarget检查(距离): {target.name}, 屏幕距离: {screenDistance:F2}, 目标吸附距离: {targetSnapDistance:F2}, 可吸附: {canSnap}");
            return canSnap;
        }
    }

    // 计算拖拽物的屏幕Rect
    private Rect GetDragScreenRect()
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[i]);
            if (sp.x < minX) minX = sp.x;
            if (sp.y < minY) minY = sp.y;
            if (sp.x > maxX) maxX = sp.x;
            if (sp.y > maxY) maxY = sp.y;
        }
        return new Rect(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
    }

    // 计算两个Rect的重叠面积
    private float ComputeOverlapArea(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        float w = Mathf.Max(0, xMax - xMin);
        float h = Mathf.Max(0, yMax - yMin);
        return w * h;
    }
    
    // 吸附到目标
    public void SnapToTarget(DragTarget target,bool animation=true)
    {
        if (target == null) return;
        
        // 获取目标的吸附位置（在目标坐标系中）
        snapPosition = Vector2.zero; // 吸附到目标的中心位置
        isSnapping = true;
        
        //Debug.Log($"开始吸附到目标: {target.name}, 目标位置: {snapPosition}");
        //Debug.Log($"当前父对象: {transform.parent?.name}");
        
        // 通知目标接收了这个拖拽物
        target.OnAcceptDrag(this);
        // 记录上次接受的目标，便于下次拖拽开始时可靠移除
        lastAcceptedTarget = target;

        // 事件：开始吸附
        onSnapStarted?.Invoke(this, target);

        // 目标是否含布局组：含则交由布局组排布，跳过吸附动画
        LayoutGroup targetLayout = target.GetComponent<LayoutGroup>();
        Transform targetTransform = target.GetComponent<Transform>();
        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = gameObject.AddComponent<LayoutElement>();
            }
        }
        layoutElement.ignoreLayout = false;

        if (targetLayout != null)
        {
            // 恢复原始锚点与枢轴，交由布局组管理
            if (anchorsAdjustedDuringDrag)
            {
                rectTransform.anchorMin = originalAnchorMin;
                rectTransform.anchorMax = originalAnchorMax;
                rectTransform.pivot = originalPivot;
                anchorsAdjustedDuringDrag = false;
            }
            // 直接归位到容器，由布局组决定最终位置
            transform.SetParent(targetTransform, false);
            transform.SetAsLastSibling();
            isSnapping = false;
            //Debug.Log("目标含布局组，跳过吸附动画，交由布局组排布");
            // 事件：吸附成功（由布局组排布）
            onSnapSucceeded?.Invoke(this, target);
            return;
        }

        if(animation){
            // 不含布局组：执行吸附动画
            Debug.Log($"开始吸附动画到目标: {target.name}, 目标位置: {snapPosition}");
        StartCoroutine(SnapToPosition(target));
        }
        else{
            // 不含布局组：直接归位到目标
            transform.SetParent(targetTransform, false);
            transform.SetAsLastSibling();
            isSnapping = false;
            //Debug.Log("目标不含布局组，直接归位到目标");
            // 事件：吸附成功（直接归位）
            onSnapSucceeded?.Invoke(this, target);
        }
    }
    
    // 吸附动画协程
    private IEnumerator SnapToPosition(DragTarget target)
    {
        Vector2 startPosition = rectTransform.anchoredPosition;
        float elapsedTime = 0f;
        float duration = 1f / snapSpeed;
        
        //Debug.Log($"开始吸附动画: 从 {startPosition} 到 {snapPosition}, 持续时间: {duration}秒");
        
        // 先设置父对象为目标
        Transform targetTransform = target.GetComponent<Transform>();
        transform.SetParent(targetTransform, true);
        
        // 等待一帧确保父对象变化生效
        yield return null;
        
        // 重新获取当前位置（因为父对象变化可能影响坐标）
        startPosition = rectTransform.anchoredPosition;
        //Debug.Log($"设置父对象后当前位置: {startPosition}");
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsedTime / duration);
            
            // 使用平滑的插值曲线
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
            rectTransform.anchoredPosition = Vector2.Lerp(startPosition, snapPosition, smoothProgress);
            
            yield return null;
        }
        
        rectTransform.anchoredPosition = snapPosition;
        isSnapping = false;
        
        //Debug.Log($"吸附完成: 最终位置 {snapPosition}, 父对象: {transform.parent?.name}");
        // 事件：吸附成功（动画完成）
        onSnapSucceeded?.Invoke(this, target);
    }
    
    // 返回原位置协程
    private IEnumerator ReturnToOriginalPosition()
    {
        // 事件：开始返回原位
        onReturnStarted?.Invoke(this);
        Vector2 startPosition = rectTransform.anchoredPosition;
        float elapsedTime = 0f;
        float duration = 1f / snapSpeed;
        
        //Debug.Log($"返回原位置: 从 {startPosition} 到 {originalPosition}, 持续时间: {duration}秒");
        //Debug.Log($"原始父对象: {originalParent?.name}, 当前父对象: {transform.parent?.name}");
        
        // 先恢复父对象和层级，确保坐标系统一致
        if (originalParent != null && transform.parent != originalParent)
        {
            transform.SetParent(originalParent, true);
            transform.SetSiblingIndex(originalSiblingIndex);
            
            // 等待一帧确保父对象变化生效
            yield return null;
            
            // 重新获取当前位置（因为父对象变化可能影响坐标）
            startPosition = rectTransform.anchoredPosition;
            //Debug.Log($"恢复父对象后当前位置: {startPosition}");
        }
        
        // 如果父对象已经是原始父对象，直接使用当前位置
        if (transform.parent == originalParent)
        {
            //Debug.Log($"父对象已正确，直接返回原位置");
        }

        // 若原始父对象含布局组且原始状态不忽略布局，则交由布局组排布
        LayoutGroup originalLayout = originalParent != null ? originalParent.GetComponent<LayoutGroup>() : null;
        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = gameObject.AddComponent<LayoutElement>();
            }
        }
        if (originalLayout != null && !originalIgnoreLayout)
        {
            layoutElement.ignoreLayout = false;
            transform.SetSiblingIndex(originalSiblingIndex);
            // 恢复原始锚点与枢轴
            if (anchorsAdjustedDuringDrag)
            {
                rectTransform.anchorMin = originalAnchorMin;
                rectTransform.anchorMax = originalAnchorMax;
                rectTransform.pivot = originalPivot;
                anchorsAdjustedDuringDrag = false;
            }
            Debug.Log("原始父对象含布局组，跳过返回动画，交由布局组排布");
            // 即使跳过动画也应视为返回完成，触发事件以便上层逻辑（如失败自毁）继续执行
            onReturnCompleted?.Invoke(this);
            yield break;
        }
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsedTime / duration);
            
            // 使用平滑的插值曲线
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
            rectTransform.anchoredPosition = Vector2.Lerp(startPosition, originalPosition, smoothProgress);
            
            yield return null;
        }
        
        rectTransform.anchoredPosition = originalPosition;
        // 恢复原始锚点与枢轴
        if (anchorsAdjustedDuringDrag)
        {
            rectTransform.anchorMin = originalAnchorMin;
            rectTransform.anchorMax = originalAnchorMax;
            rectTransform.pivot = originalPivot;
            anchorsAdjustedDuringDrag = false;
        }
        // 恢复ignoreLayout到原始状态
        layoutElement.ignoreLayout = originalIgnoreLayout;

        Debug.Log($"返回原位置完成: 最终位置 {originalPosition}");
        // 事件：返回原位完成
        onReturnCompleted?.Invoke(this);
    }
    
    // 获取当前吸附目标
    public DragTarget GetCurrentTarget()
    {
        return currentTarget;
    }
    
    // 是否正在吸附
    public bool IsSnapping()
    {
        return isSnapping;
    }
    
    // 手动设置吸附目标
    public void SetValidTargets(List<DragTarget> targets)
    {
        validTargets = targets ?? new List<DragTarget>();
        if (filterManualTargets && onlyAllowTargetsWithTag && !string.IsNullOrEmpty(allowedTargetTag))
        {
            int before = validTargets.Count;
            validTargets = validTargets.FindAll(t => t != null && t.CompareTag(allowedTargetTag));
           // Debug.Log($"SetValidTargets: 应用Tag过滤，保留 {validTargets.Count}/{before}");
        }
    }
    
    // 添加有效目标
    public void AddValidTarget(DragTarget target)
    {
        if (!validTargets.Contains(target))
        {
            validTargets.Add(target);
        }
    }
    
    // 移除有效目标
    public void RemoveValidTarget(DragTarget target)
    {
        validTargets.Remove(target);
    }

    // 运行时配置：设置目标Tag限定，并立即对当前列表应用一次
    public void SetTargetTagRestriction(string tag, bool enable, bool filterOnManual = true, bool reapplyOnBegin = false)
    {
        allowedTargetTag = tag;
        onlyAllowTargetsWithTag = enable;
        filterManualTargets = filterOnManual;
        reapplyTagFilterOnDragBegin = reapplyOnBegin;
        if (onlyAllowTargetsWithTag && !string.IsNullOrEmpty(allowedTargetTag))
        {
            int before = validTargets.Count;
            validTargets = validTargets.FindAll(t => t != null && t.CompareTag(allowedTargetTag));
            //Debug.Log($"SetTargetTagRestriction: 立即应用Tag过滤，保留 {validTargets.Count}/{before}");
        }
    }
}
