using UnityEngine;
using UnityEngine.UI;
using Game.Data;

namespace UI.Choose
{
    // 单个角色选择页面：展示名字、图标/预览、简介
    public class CharacterSelectionPage : MonoBehaviour
    {
        [Header("UI 引用")]
        public Text nameText;
        public Text descriptionText;
        public Image iconImage;
        public RawImage previewImage;       // 可选：绑定一个 RenderTexture 用于动态图预览

        [Header("动态图预览（可选）")]
        public Transform previewSpawnRoot;  // 动态预览的生成根节点（例如一个 3D 预览区域）
        public Camera previewCamera;        // 指向一个专用摄像机，其输出到 previewImage.texture

        public CharacterEntry _entry;
        private GameObject _previewInstance;

        public void choose(){
            GameManager.Instance.StartGame(_entry);
        }
        // 用资产数据填充页面
        public void Setup(CharacterEntry entry, Sprite iconSprite)
        {
            _entry = entry;

            if (nameText != null)
            {
                nameText.text = string.IsNullOrEmpty(entry.characterName) ? entry.characterId : entry.characterName;
            }
            if (descriptionText != null)
            {
                descriptionText.text = entry.description;
            }

            // 静态图内容填充（不改变其他 UI 属性）
            if (iconImage != null)
            {
                iconImage.sprite = iconSprite ?? entry.icon;
            }

            // 动态预览条件检查：不改变 UI 激活/启用状态，仅在显示时实例化预览
        }

        // 页面显示时，如果支持动态图预览则实例化预览对象
        public void OnShown()
        {
            if (previewImage == null || previewImage.texture == null || previewSpawnRoot == null) return;
            if (_previewInstance != null) return;

            GameObject prefab = _entry.prefab;
            if (prefab == null && !string.IsNullOrEmpty(_entry.prefabPath))
            {
                prefab = Resources.Load<GameObject>(_entry.prefabPath);
            }
            if (prefab == null) return;

            _previewInstance = Instantiate(prefab, previewSpawnRoot);
            _previewInstance.transform.localPosition = Vector3.zero;
            _previewInstance.transform.localRotation = Quaternion.identity;
            _previewInstance.transform.localScale = Vector3.one;
        }

        // 页面隐藏时，清理预览对象
        public void OnHidden()
        {
            if (_previewInstance != null)
            {
                Destroy(_previewInstance);
                _previewInstance = null;
            }
        }

        // 若模板缺少字段，可自动从子节点查找常见命名
        public void AutoWireFromChildren()
        {
            if (nameText == null)
            {
                var t = transform.Find("Name");
                if (t != null) nameText = t.GetComponent<Text>();
            }
            if (descriptionText == null)
            {
                var t = transform.Find("Description");
                if (t != null) descriptionText = t.GetComponent<Text>();
            }
            if (iconImage == null)
            {
                var t = transform.Find("Icon");
                if (t != null) iconImage = t.GetComponent<Image>();
            }
            if (previewImage == null)
            {
                var t = transform.Find("Preview");
                if (t != null) previewImage = t.GetComponent<RawImage>();
            }
        }
    }
}