using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class drag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("拖拽设置")]
    public bool canDrag = true;  // 是否可以拖拽
    public bool bringToFront = true;  // 拖拽时是否置于最前
    
    protected RectTransform rectTransform;
    protected Canvas canvas;
    protected CanvasGroup canvasGroup;
    protected Vector2 originalPosition;
    protected Transform originalParent;
    protected int originalSiblingIndex;
    protected virtual void Awake(){
        // 获取必要的组件
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        canvasGroup = GetComponent<CanvasGroup>();
        
        // 如果没有CanvasGroup，添加一个
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }
    protected virtual void Start()
    {
        // 获取必要的组件
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        canvasGroup = GetComponent<CanvasGroup>();
        
        // 如果没有CanvasGroup，添加一个
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public virtual void OnBeginDrag(PointerEventData eventData)
    {
        if (!canDrag) return;
        
        // 记录原始位置和父对象
        originalPosition = rectTransform.anchoredPosition;
        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();
        
        // 如果设置为拖拽时置于最前
        if (bringToFront && canvas != null)
        {
            transform.SetParent(canvas.transform, true);
            transform.SetAsLastSibling();
        }
        
        // 设置透明度，表示正在拖拽
        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;
    }

    public virtual void OnDrag(PointerEventData eventData)
    {
        if (!canDrag) return;
        
        // 将屏幕坐标转换为Canvas坐标
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform, 
            eventData.position, 
            eventData.pressEventCamera, 
            out localPoint))
        {
            rectTransform.anchoredPosition = localPoint;
        }
    }

    public virtual void OnEndDrag(PointerEventData eventData)
    {
        if (!canDrag) return;
        
        // 恢复透明度
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        
        // 检查是否拖拽到了有效区域
        if (eventData.pointerEnter != null)
        {
            // 可以在这里添加放置逻辑
            // 例如检查是否拖拽到了特定的UI元素上
        }
        else
        {
            // 如果没有有效的放置目标，可以选择是否回到原位置
            // transform.SetParent(originalParent, true);
            // transform.SetSiblingIndex(originalSiblingIndex);
        }
    }
}
