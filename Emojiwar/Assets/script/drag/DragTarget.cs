using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragTarget : MonoBehaviour
{
    [Header("目标设置")]
    public bool canAcceptDrag = true;  // 是否可以接收拖拽物
    public bool showSnapPreview = true;  // 是否显示吸附预览
    public Color previewColor = Color.yellow;  // 预览颜色
    public float previewAlpha = 0.5f;  // 预览透明度
    
    [Header("接收设置")]
    public bool allowMultipleItems = false;  // 是否允许多个物品
    public int maxItems = 1;  // 最大物品数量
    public List<SnapDrag> acceptedItems = new List<SnapDrag>();  // 已接受的物品

    [Header("类型过滤")]
    [Tooltip("启用后，仅接收指定 ObjType 类型的物品（来自 UI/Grid/Obj.cs 的 type 字段）。")]
    public bool enableItemTypeFilter = false;
    [Tooltip("允许的物品类型列表（留空表示不限制）。")]
    public List<ObjType> allowedItemTypes = new List<ObjType>();
    
    [Header("吸附距离设置")]
    public bool useBoundaryBasedDistance = true;  // 是否使用基于边界的吸附距离
    public bool includeChildrenBounds = false;  // 是否包含子元素边界
    public float boundaryDistanceMultiplier = 1.5f;  // 边界距离倍数
    public float minSnapDistance = 30f;  // 最小吸附距离
    public float maxSnapDistance = 200f;  // 最大吸附距离
    
    [Header("事件")]
    public UnityEngine.Events.UnityEvent<GameObject> OnItemAccepted;  // 物品被接受时的事件
    public UnityEngine.Events.UnityEvent<GameObject> OnItemRemoved;  // 物品被移除时的事件
    
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private SpriteRenderer spriteRenderer;
    private Image image;
    private Color originalColor;
    private bool isShowingPreview = false;
    
    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        image = GetComponent<Image>();
        
        // 保存原始颜色
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;
        else if (image != null)
            originalColor = image.color;
    }
    
    // 获取吸附位置
    public virtual Vector2 GetSnapPosition()
    {
        // 返回目标在Canvas坐标系中的位置
        // 如果目标有RectTransform，返回其anchoredPosition
        if (rectTransform != null)
        {
            return rectTransform.anchoredPosition;
        }
        
        // 如果没有RectTransform，返回Transform的localPosition
        return transform.localPosition;
    }
    
    // 获取基于UI边界的吸附距离
    public virtual float GetSnapDistance()
    {
        if (!useBoundaryBasedDistance)
        {
            // 如果不使用边界距离，返回默认值
            return minSnapDistance;
        }
        
        if (rectTransform == null)
        {
            return minSnapDistance;
        }
        
        // 获取UI的完整边界
        Bounds bounds = GetUIBounds();
        
        // 获取边界的最大尺寸（宽度或高度的较大值）
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y);
        
        // 计算基于边界的吸附距离
        float calculatedDistance = maxDimension * boundaryDistanceMultiplier;
        
        // 限制在最小和最大值之间
        calculatedDistance = Mathf.Clamp(calculatedDistance, minSnapDistance, maxSnapDistance);
        
        return calculatedDistance;
    }
    
    // 获取UI元素的完整边界（包含子元素）
    private Bounds GetUIBounds()
    {
        if (rectTransform == null)
            return new Bounds();
        
        if (!includeChildrenBounds)
        {
            // 只计算当前元素的边界
            Rect rect = rectTransform.rect;
            Vector3 center = rectTransform.TransformPoint(rect.center);
            Vector3 size = new Vector3(rect.width * rectTransform.lossyScale.x, 
                                     rect.height * rectTransform.lossyScale.y, 0);
            return new Bounds(center, size);
        }
        
        // 计算包含所有子元素的边界
        Bounds bounds = new Bounds();
        bool boundsInitialized = false;
        
        // 获取所有RectTransform组件（包括自己和子元素）
        RectTransform[] rectTransforms = GetComponentsInChildren<RectTransform>();
        
        foreach (RectTransform rt in rectTransforms)
        {
            if (rt.gameObject.activeInHierarchy)
            {
                Rect rect = rt.rect;
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                
                foreach (Vector3 corner in corners)
                {
                    if (!boundsInitialized)
                    {
                        bounds = new Bounds(corner, Vector3.zero);
                        boundsInitialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(corner);
                    }
                }
            }
        }
        
        return bounds;
    }

    // 获取目标在屏幕坐标系下的矩形边界（可选包含子元素）
    public Rect GetScreenRect(Camera camera)
    {
        if (rectTransform == null)
        {
            return new Rect();
        }
        
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        
        if (!includeChildrenBounds)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                if (sp.x < minX) minX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y > maxY) maxY = sp.y;
            }
        }
        else
        {
            RectTransform[] rts = GetComponentsInChildren<RectTransform>();
            foreach (RectTransform rt in rts)
            {
                if (!rt.gameObject.activeInHierarchy) continue;
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 sp = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                    if (sp.x < minX) minX = sp.x;
                    if (sp.y < minY) minY = sp.y;
                    if (sp.x > maxX) maxX = sp.x;
                    if (sp.y > maxY) maxY = sp.y;
                }
            }
        }
        
        if (float.IsInfinity(minX) || float.IsInfinity(minY) || float.IsInfinity(maxX) || float.IsInfinity(maxY))
        {
            return new Rect();
        }
        
        return new Rect(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
    }
    
    // 检查点是否在吸附范围内
    public virtual bool IsWithinSnapRange(Vector2 worldPosition, Camera camera = null)
    {
        if (rectTransform == null) return false;
        
        // 获取目标的屏幕坐标
        Vector2 targetScreenPos = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.position);
        Vector2 dragScreenPos = RectTransformUtility.WorldToScreenPoint(camera, worldPosition);
        
        // 计算距离
        float distance = Vector2.Distance(dragScreenPos, targetScreenPos);
        float snapDistance = GetSnapDistance();
        
        return distance <= snapDistance;
    }
    
    // 检查是否可以接受拖拽物
    public virtual bool CanAcceptDrag(SnapDrag dragItem)
    {
        if (!canAcceptDrag || dragItem == null) 
        {
            Debug.Log($"目标 {name}: 不可接受 - canAcceptDrag={canAcceptDrag}, dragItem={dragItem != null}");
            return false;
        }
        
        // 检查是否已满
        if (!allowMultipleItems && acceptedItems.Count >= maxItems)
        {
            Debug.Log($"目标 {name}: 不可接受 - 已满 (当前:{acceptedItems.Count}, 最大:{maxItems})");
            return false;
        }
        
        // 检查是否已经包含这个物品
        if (acceptedItems.Contains(dragItem))
        {
            Debug.Log($"目标 {name}: 不可接受 - 已包含此物品");
            return false;
        }

        // 类型过滤：基于 UI/Grid/Obj.cs 的 Obj.type
        if (enableItemTypeFilter)
        {
            Obj obj = dragItem.GetComponent<Obj>();
            if (obj == null)
            {
                // 尝试在父层级或子层级查找，以提升兼容性
                obj = dragItem.GetComponentInParent<Obj>();
                if (obj == null)
                {
                    obj = dragItem.GetComponentInChildren<Obj>();
                }
            }

            if (obj == null)
            {
                Debug.Log($"目标 {name}: 不可接受 - 拖拽物缺少 Obj 组件，无法判定类型");
                return false;
            }

            // 若配置了允许类型列表，则必须包含当前物品类型
            if (allowedItemTypes != null && allowedItemTypes.Count > 0 && !allowedItemTypes.Contains(obj.type))
            {
                Debug.Log($"目标 {name}: 不可接受 - 类型 {obj.type} 未在允许列表中");
                return false;
            }
        }

//        Debug.Log($"目标 {name}: 可以接受物品");
        return true;
    }
    
    // 拖拽开始时的回调
    public virtual void OnDragStart(SnapDrag dragItem)
    {
        // 子类可以重写此方法
    }
    
    // 拖拽结束时的回调
    public virtual void OnDragEnd(SnapDrag dragItem)
    {
        // 子类可以重写此方法
    }
    
    // 接受拖拽物
    public virtual void OnAcceptDrag(SnapDrag dragItem)
    {
        if (!CanAcceptDrag(dragItem)) return;
        
        // 添加到已接受列表
        acceptedItems.Add(dragItem);
        
        // 隐藏预览
        HideSnapPreview();
        
        // 触发事件
        OnItemAccepted?.Invoke(dragItem.gameObject);
        
        // 子类可以重写此方法添加自定义逻辑
        OnItemAcceptedCustom(dragItem);
    }
    
    // 移除拖拽物
    public virtual void RemoveDragItem(SnapDrag dragItem)
    {
        if (acceptedItems.Contains(dragItem))
        {
            acceptedItems.Remove(dragItem);
            // 触发事件
            OnItemRemoved?.Invoke(dragItem.gameObject);
            // 子类可以重写此方法添加自定义逻辑
            OnItemRemovedCustom(dragItem);
        }
    }

    // 运行时静默登记已存在的物品（可选触发事件）
    public void RegisterExistingItem(SnapDrag dragItem, bool fireEvents = false)
    {
        if (dragItem == null) return;
        if (!acceptedItems.Contains(dragItem))
        {
            acceptedItems.Add(dragItem);
        }
        // 默认不触发事件；如需触发则调用事件与自定义逻辑
        if (fireEvents)
        {
            OnItemAccepted?.Invoke(dragItem.gameObject);
            OnItemAcceptedCustom(dragItem);
        }
    }
    
    // 显示吸附预览
    public virtual void ShowSnapPreview(SnapDrag dragItem)
    {
        if (!showSnapPreview || !CanAcceptDrag(dragItem)) return;
        
        isShowingPreview = true;
        
        // 改变颜色显示预览
        Color previewColorWithAlpha = previewColor;
        previewColorWithAlpha.a = previewAlpha;
        
        if (spriteRenderer != null)
        {
            spriteRenderer.color = previewColorWithAlpha;
        }
        else if (image != null)
        {
            image.color = previewColorWithAlpha;
        }
        else if (canvasGroup != null)
        {
            canvasGroup.alpha = previewAlpha;
        }
    }
    
    // 隐藏吸附预览
    public virtual void HideSnapPreview()
    {
        if (!isShowingPreview) return;
        
        isShowingPreview = false;
        
        // 恢复原始颜色
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
        else if (image != null)
        {
            image.color = originalColor;
        }
        else if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
    }
    
    // 检查是否已满
    public virtual bool IsFull()
    {
        return acceptedItems.Count >= maxItems;
    }
    
    // 检查是否为空
    public virtual bool IsEmpty()
    {
        return acceptedItems.Count == 0;
    }
    
    // 获取当前物品数量
    public virtual int GetItemCount()
    {
        return acceptedItems.Count;
    }
    
    // 清空所有物品
    public virtual void ClearAllItems()
    {
        List<SnapDrag> itemsToRemove = new List<SnapDrag>(acceptedItems);
        foreach (SnapDrag item in itemsToRemove)
        {
            RemoveDragItem(item);
        }
    }
    
    // 子类可以重写的自定义方法
    protected virtual void OnItemAcceptedCustom(SnapDrag dragItem)
    {
        // 子类重写此方法添加自定义逻辑
    }
    
    protected virtual void OnItemRemovedCustom(SnapDrag dragItem)
    {
        // 子类重写此方法添加自定义逻辑
    }
    
    // 获取所有已接受的物品
    public List<SnapDrag> GetAcceptedItems()
    {
        return new List<SnapDrag>(acceptedItems);
    }
    
    // 检查是否包含特定物品
    public bool ContainsItem(SnapDrag dragItem)
    {
        return acceptedItems.Contains(dragItem);
    }
    
    // 设置是否可以接受拖拽
    public void SetCanAcceptDrag(bool canAccept)
    {
        canAcceptDrag = canAccept;
    }
    
    // 设置最大物品数量
    public void SetMaxItems(int max)
    {
        maxItems = max;
        
        // 如果当前物品数量超过新的最大值，移除多余的物品
        while (acceptedItems.Count > maxItems)
        {
            SnapDrag itemToRemove = acceptedItems[acceptedItems.Count - 1];
            RemoveDragItem(itemToRemove);
        }
    }
}
