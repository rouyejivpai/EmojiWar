using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Data;

namespace UI.Choose
{
    // 角色选择 UI 管理器：根据 CharacterCatalog 自动生成页面与导航按钮
    public class CharacterSelectionManager : MonoBehaviour
    {
        [Header("资产引用")]
        private CharacterCatalog _characterCatalog;
        public CharacterCatalog characterCatalog{
            get{
                if(_characterCatalog == null)
                {
                    return GameManager.Instance.characterCatalog;
                }
                return _characterCatalog;
            }
            set{
                _characterCatalog = value;
            }
        }
        // 可在 Inspector 设置；若为空则从 GameManager 获取

        [Header("容器引用")]
        public RectTransform pagesContainer;      // 放置页面的容器（如一个 Content 下）
        public RectTransform navButtonsContainer; // 放置导航按钮的容器（如水平布局）

        [Header("模板")]
        public GameObject pageTemplate;           // 需要：包含 CharacterSelectionPage 组件
        public GameObject navButtonTemplate;      // 需要：包含 Button + Text

        private readonly List<CharacterSelectionPage> _pages = new List<CharacterSelectionPage>();
        private readonly List<Button> _navButtons = new List<Button>();
        private int _currentIndex = -1;

        private void Awake()
        {
            if (characterCatalog == null && GameManager.Instance != null)
            {
                characterCatalog = GameManager.Instance.characterCatalog;
            }

            GameManager.Instance.OnPlayerReady+= (player) =>
            {
                // 玩家准备好后禁用选择界面
                pagesContainer.gameObject.SetActive(false);
                navButtonsContainer.gameObject.SetActive(false);
            };
        }

        private void Start()
        {
            BuildUI();
            ShowPage(0);
        }

        [ContextMenu("重建角色选择UI")]
        public void BuildUI()
        {
            if (characterCatalog == null)
            {
                Debug.LogWarning("[CharacterSelectionManager] 未设置 CharacterCatalog");
                return;
            }

            // 清理旧内容
            ClearContainer(pagesContainer);
            ClearContainer(navButtonsContainer);
            _pages.Clear();
            _navButtons.Clear();

            // 按资产生成页面与按钮
            for (int i = 0; i < characterCatalog.characters.Count; i++)
            {
                var entry = characterCatalog.characters[i];

                // 页面
                if (pageTemplate == null)
                {
                    Debug.LogWarning("[CharacterSelectionManager] 未设置 pageTemplate，跳过页面生成");
                    continue;
                }
                var pageGO = Instantiate(pageTemplate, pagesContainer);

                var page = pageGO.GetComponent<CharacterSelectionPage>();
                if (page == null)
                {
                    page = pageGO.AddComponent<CharacterSelectionPage>();
                    page.AutoWireFromChildren();
                }

                // 图标使用资产直接拖拽的 Sprite
                Sprite iconSprite = entry.icon;

                page.Setup(entry, iconSprite);
                pageGO.SetActive(false);
                _pages.Add(page);

                // 按钮
                if (navButtonTemplate == null)
                {
                    Debug.LogWarning("[CharacterSelectionManager] 未设置 navButtonTemplate，跳过按钮生成");
                    continue;
                }
                var btnGO = Instantiate(navButtonTemplate, navButtonsContainer);

                var button = btnGO.GetComponent<Button>();
                if (button == null) button = btnGO.AddComponent<Button>();
                var btnText = btnGO.GetComponentInChildren<Text>();
                if (btnText != null)
                {
                    btnText.text = string.IsNullOrEmpty(entry.characterName) ? entry.characterId : entry.characterName;
                }

                int pageIndex = i;
                button.onClick.AddListener(() => ShowPage(pageIndex));
                _navButtons.Add(button);
            }
        }

        public void ShowPage(int index)
        {
            if (index < 0 || index >= _pages.Count)
            {
                Debug.LogWarning($"[CharacterSelectionManager] 页面索引越界: {index}");
                return;
            }
            if (_currentIndex == index) return;

            // 隐藏当前
            if (_currentIndex >= 0 && _currentIndex < _pages.Count)
            {
                _pages[_currentIndex].gameObject.SetActive(false);
                _pages[_currentIndex].OnHidden();
            }

            // 显示目标
            _pages[index].gameObject.SetActive(true);
            _pages[index].OnShown();
            _currentIndex = index;
        }

        private void ClearContainer(RectTransform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
            }
        }

        // 移除默认样式生成：仅依赖模板，不设置 UI 属性
    }
}