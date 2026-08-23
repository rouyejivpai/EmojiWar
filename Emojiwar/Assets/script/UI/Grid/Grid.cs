using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class Grid : MonoBehaviour
{
    public enum state
    {
        pack,
        weapon,
        none
    };
    
    [Header("吸附设置")]
    public float snapRadius = 1f;           // 吸附半径
    public bool canAcceptMultiple = false;   // 是否可接受多个物体
    public int maxItems = 1;                 // 最大容纳数量
    public bool allowSwap = true;            // 是否允许交换
    public bool empty=true;//
    [Header("视觉效果")]
    public Color normalColor = Color.white;  // 正常颜色
    public Color highlightColor = Color.yellow; // 高亮颜色
    public Color occupiedColor = Color.red;  // 已占用颜色
    
    [FormerlySerializedAs("containedItems")] 
    public List<Obj> Objs = new List<Obj>();
    private Image zoneImage;
    private bool isHighlighted = false;
    
    void Start()
    {
        zoneImage = GetComponent<Image>();
        if (zoneImage != null)
        {
            zoneImage.color = normalColor;
        }
        
        // 添加碰撞器
        if (GetComponent<Collider2D>() == null)
        {
            BoxCollider2D collider = gameObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
        }
        GetComponent<DragTarget>().OnItemAccepted.AddListener(OnItemEnter);
        GetComponent<DragTarget>().OnItemRemoved.AddListener(OnItemExit);
    }
    
    
    // 当物体尝试进入
    public virtual void OnItemEnter(GameObject item = null)
    {
        
    }
    
    // 当物体尝试退出
    public virtual void OnItemExit(GameObject item = null)
    {
        
    }
    
   
    
  
    
    // 清空格子
    public void ClearGrid()
    {
        Objs.Clear();
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        if (zoneImage == null) return;
        
        if (Objs.Count >= maxItems)
        {
            zoneImage.color = occupiedColor; // 已满时显示红色
        }
        else if (isHighlighted)
        {
            zoneImage.color = highlightColor;
        }
        else
        {
            zoneImage.color = normalColor;
        }
    }
    
    // 获取当前包含的物品数量
    public int GetItemCount()
    {
        return Objs.Count;
    }
    
    // 检查是否包含特定物品
    public bool ContainsItem(Obj item)
    {
        return Objs.Contains(item);
    }

    public virtual bool CheckLegal(Obj item)
    {
        return true;
    }
}