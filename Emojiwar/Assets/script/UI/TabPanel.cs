using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.UI;

public class TabPanel : MonoBehaviour
{
    [Header("装备页面设置")]
    public GameObject equipmentPanel;    // 装备页面面板
    public GameObject sequipmentPanel;  //快捷显示
    public KeyCode toggleKey = KeyCode.Tab;  // 切换按键，默认Tab
    public GameObject PACK;
    private bool isPanelVisible = false; // 当前面板是否可见

    private void Awake()
    {
        GameManager.Instance.PACK = PACK;
    }

    void Start()
    {
        // 确保装备页面初始状态为隐藏
        if (equipmentPanel != null)
        {
            equipmentPanel.SetActive(false);
            isPanelVisible = false;
        }
    }
    
    void Update()
    {
        // 检测Tab键按下
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleEquipmentPanel();
        }
        
        // 可选：按ESC键关闭装备页面
        if (Input.GetKeyDown(KeyCode.Escape) && isPanelVisible)
        {
            HideEquipmentPanel();
        }
    }
    
    // 切换装备页面显示/隐藏
    public void ToggleEquipmentPanel()
    {
        if (equipmentPanel != null)
        {
            isPanelVisible = !isPanelVisible;
            equipmentPanel.SetActive(isPanelVisible);
            sequipmentPanel.SetActive(!isPanelVisible);
            // 可选：暂停/恢复游戏
            if (isPanelVisible)
            {
                Time.timeScale = 0; // 暂停游戏
                GameManager.Instance.player.GetComponent<PlayerController>().isActive = false;
                Debug.Log("装备页面已打开");
            }
            else
            {
                Time.timeScale = 1; // 恢复游戏
                GameManager.Instance.player.GetComponent<PlayerController>().isActive = true;
                Debug.Log("装备页面已关闭");
            }
        }
    }
    
    // 显示装备页面
    public void ShowEquipmentPanel()
    {
        if (equipmentPanel != null && !isPanelVisible)
        {
            equipmentPanel.SetActive(true);
            isPanelVisible = true;
            Time.timeScale = 0; // 暂停游戏
            Debug.Log("装备页面已打开");
        }
    }
    
    // 隐藏装备页面
    public void HideEquipmentPanel()
    {
        if (equipmentPanel != null && isPanelVisible)
        {
            equipmentPanel.SetActive(false);
            isPanelVisible = false;
            Time.timeScale = 1; // 恢复游戏
            Debug.Log("装备页面已关闭");
        }
    }
    
    // 公共方法：检查面板是否可见
    public bool IsPanelVisible()
    {
        return isPanelVisible;
    }
    
    // 公共方法：设置切换按键
    public void SetToggleKey(KeyCode newKey)
    {
        toggleKey = newKey;
    }
}
