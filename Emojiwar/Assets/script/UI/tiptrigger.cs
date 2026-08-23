using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class tiptrigger : MonoBehaviour,IPointerEnterHandler, IPointerExitHandler
{
    private bool _isActive;
    public bool isActive
    {
        get
        {
            return _isActive;
        }
        set
        {
            _isActive = value;
            if (_isActive == false)
            {
                hoverPanel.HideInfo();
            }
        }
    }
    private HoverInfoPanel hoverPanel;

    private void Awake()
    {
        hoverPanel = HoverInfoPanel.Ins;
    }
    
    // 鼠标悬停事件
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isActive || hoverPanel == null) return;

        Obj obj = GetComponent<Obj>();
        hoverPanel.ShowInfo(obj);
    }

    // 鼠标离开事件
    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoverPanel == null) return;
        hoverPanel.HideInfo();
    }
}
