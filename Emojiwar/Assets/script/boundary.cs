using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class boundary : MonoBehaviour
{
    [Header("销毁时机")]
    public bool destroyOnEnter = true;   // 进入边界即销毁
    public bool destroyOnExit = false;   // 离开边界即销毁（用于PlayArea）

    [Header("过滤设置")]
    public bool requireProfileComponent = true; // 仅销毁带有profile组件的物体
    public bool useTagFilter = false;           // 是否使用Tag过滤
    public string projectileTag = "Untagged";   // 需要匹配的Tag
    public bool useLayerFilter = false;         // 是否使用Layer过滤
    public LayerMask projectileLayers = ~0;     // 允许的Layer

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.tag == "profile")
        {
           Destroy( other.gameObject);
        }
    }
    
}
