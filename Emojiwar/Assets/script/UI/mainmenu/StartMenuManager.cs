using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


    // 角色选择 UI 管理器：根据 CharacterCatalog 自动生成页面与导航按钮
    public class StartMenuManager : MonoBehaviour
    {
        public GameObject StartMenu;

        private void Awake()
        {
            GameManager.Instance.onGameStateChanged+= (GameState s) =>
            {
                if (s == GameState.Menu)
                {
                    SetActive(true);
                }
                else
                {
                    SetActive(false);
                }
            };  
        }

        private void Start()
        {
            
        }

        public void SetActive(bool active)
        {
            StartMenu.SetActive(active);
        }

     
        // 移除默认样式生成：仅依赖模板，不设置 UI 属性
    }
