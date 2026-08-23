using UnityEngine;

public class DragDebugHelper : MonoBehaviour
{
    [Header("调试设置")]
    public SnapDrag snapDrag;
    public DragTarget dragTarget;
    
    [Header("测试参数")]
    public float testSnapDistance = 100f;
    public bool testCanAcceptDrag = true;
    
    void Start()
    {
        if (snapDrag == null)
            snapDrag = FindObjectOfType<SnapDrag>();
        if (dragTarget == null)
            dragTarget = FindObjectOfType<DragTarget>();
    }
    
    [ContextMenu("测试吸附距离")]
    void TestSnapDistance()
    {
        if (snapDrag == null || dragTarget == null)
        {
            Debug.LogError("缺少SnapDrag或DragTarget引用！");
            return;
        }
        
        RectTransform snapDragRect = snapDrag.GetComponent<RectTransform>();
        RectTransform targetRect = dragTarget.GetComponent<RectTransform>();
        Canvas canvas = snapDrag.GetComponentInParent<Canvas>();
        
        // AnchoredPosition距离
        Vector2 snapDragPos = snapDragRect.anchoredPosition;
        Vector2 targetPos = targetRect.anchoredPosition;
        float anchoredDistance = Vector2.Distance(snapDragPos, targetPos);
        
        // 屏幕坐标距离
        Vector2 snapDragScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, snapDragRect.position);
        Vector2 targetScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, targetRect.position);
        float screenDistance = Vector2.Distance(snapDragScreenPos, targetScreenPos);
        
        Debug.Log("=== 距离计算测试 ===");
        Debug.Log($"SnapDrag AnchoredPosition: {snapDragPos}");
        Debug.Log($"DragTarget AnchoredPosition: {targetPos}");
        Debug.Log($"AnchoredPosition距离: {anchoredDistance:F2}");
        Debug.Log($"SnapDrag屏幕坐标: {snapDragScreenPos}");
        Debug.Log($"DragTarget屏幕坐标: {targetScreenPos}");
        Debug.Log($"屏幕坐标距离: {screenDistance:F2}");
        Debug.Log($"吸附距离设置: {snapDrag.snapDistance}");
        Debug.Log($"AnchoredPosition在范围内: {anchoredDistance <= snapDrag.snapDistance}");
        Debug.Log($"屏幕坐标在范围内: {screenDistance <= snapDrag.snapDistance}");
        
        // 检查父对象
        Debug.Log($"SnapDrag父对象: {snapDragRect.parent?.name}");
        Debug.Log($"DragTarget父对象: {targetRect.parent?.name}");
        Debug.Log($"父对象相同: {snapDragRect.parent == targetRect.parent}");
    }
    
    [ContextMenu("测试目标接受")]
    void TestTargetAccept()
    {
        if (dragTarget == null)
        {
            Debug.LogError("缺少DragTarget引用！");
            return;
        }
        
        Debug.Log($"目标可以接受拖拽: {dragTarget.canAcceptDrag}");
        Debug.Log($"目标是否已满: {dragTarget.IsFull()}");
        Debug.Log($"目标当前物品数量: {dragTarget.GetItemCount()}");
        Debug.Log($"目标最大物品数量: {dragTarget.maxItems}");
    }
    
    [ContextMenu("强制设置吸附距离")]
    void ForceSetSnapDistance()
    {
        if (snapDrag == null)
        {
            Debug.LogError("缺少SnapDrag引用！");
            return;
        }
        
        snapDrag.snapDistance = testSnapDistance;
        Debug.Log($"设置吸附距离为: {testSnapDistance}");
    }
    
    void Update()
    {
        // 按F1键测试吸附距离
        if (Input.GetKeyDown(KeyCode.F1))
        {
            TestSnapDistance();
        }
        
        // 按F2键测试目标接受
        if (Input.GetKeyDown(KeyCode.F2))
        {
            TestTargetAccept();
        }
        
        // 按F3键强制设置吸附距离
        if (Input.GetKeyDown(KeyCode.F3))
        {
            ForceSetSnapDistance();
        }
    }
}
