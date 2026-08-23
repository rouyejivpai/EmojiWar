using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 测试新的基于UI边界的吸附距离计算功能
/// </summary>
public class SnapDistanceTest : MonoBehaviour
{
    [Header("测试设置")]
    public DragTarget[] testTargets;  // 测试目标
    public Text debugText;  // 调试文本显示
    
    void Start()
    {
        // 自动查找所有DragTarget
        if (testTargets == null || testTargets.Length == 0)
        {
            testTargets = FindObjectsOfType<DragTarget>();
        }
        
        // 显示测试信息
        DisplayTargetInfo();
    }
    
    void Update()
    {
        // 实时更新显示信息
        if (Input.GetKeyDown(KeyCode.Space))
        {
            DisplayTargetInfo();
        }
    }
    
    /// <summary>
    /// 显示所有目标的吸附距离信息
    /// </summary>
    void DisplayTargetInfo()
    {
        string info = "=== 吸附距离测试信息 ===\n";
        info += "按空格键刷新信息\n\n";
        
        foreach (DragTarget target in testTargets)
        {
            if (target != null)
            {
                RectTransform rect = target.GetComponent<RectTransform>();
                Vector2 size = rect != null ? rect.sizeDelta : Vector2.zero;
                float snapDistance = target.GetSnapDistance();
                
                info += $"目标: {target.name}\n";
                info += $"  sizeDelta: {size.x:F1} x {size.y:F1}\n";
                
                // 获取实际边界信息
                if (rect != null)
                {
                    Rect actualRect = rect.rect;
                    Vector3 scale = rect.lossyScale;
                    float actualWidth = actualRect.width * Mathf.Abs(scale.x);
                    float actualHeight = actualRect.height * Mathf.Abs(scale.y);
                    info += $"  实际边界: {actualWidth:F1} x {actualHeight:F1}\n";
                    info += $"  缩放: {scale.x:F2} x {scale.y:F2}\n";
                }
                
                info += $"  使用边界距离: {target.useBoundaryBasedDistance}\n";
                info += $"  包含子元素: {target.includeChildrenBounds}\n";
                info += $"  距离倍数: {target.boundaryDistanceMultiplier:F1}\n";
                info += $"  计算的吸附距离: {snapDistance:F1}\n";
                info += $"  最小/最大限制: {target.minSnapDistance:F1} / {target.maxSnapDistance:F1}\n\n";
            }
        }
        
        if (debugText != null)
        {
            debugText.text = info;
        }
        
        Debug.Log(info);
    }
    
    /// <summary>
    /// 测试不同尺寸目标的吸附距离
    /// </summary>
    [ContextMenu("测试吸附距离")]
    public void TestSnapDistances()
    {
        Debug.Log("=== 开始测试吸附距离 ===");
        
        foreach (DragTarget target in testTargets)
        {
            if (target != null)
            {
                // 测试不同的边界距离倍数
                float[] multipliers = { 0.5f, 1.0f, 1.5f, 2.0f, 3.0f };
                
                Debug.Log($"\n目标: {target.name}");
                RectTransform rect = target.GetComponent<RectTransform>();
                Vector2 size = rect != null ? rect.sizeDelta : Vector2.zero;
                Debug.Log($"尺寸: {size.x} x {size.y}");
                
                foreach (float multiplier in multipliers)
                {
                    target.boundaryDistanceMultiplier = multiplier;
                    float snapDistance = target.GetSnapDistance();
                    Debug.Log($"  倍数 {multiplier:F1}: 吸附距离 = {snapDistance:F1}");
                }
                
                // 恢复默认值
                target.boundaryDistanceMultiplier = 1.5f;
            }
        }
        
        Debug.Log("=== 测试完成 ===");
    }
}