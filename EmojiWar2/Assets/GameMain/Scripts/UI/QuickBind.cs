//------------------------------------------------------------
// EmojiWar GameMain - QuickBind 运行时组件
// 挂在 UI 根物体上，维护「字段名 ↔ UI 物体 ↔ 类型」的绑定表。
// Editor 侧由 QuickBindGenerator 扫描子物体生成绑定代码并填充引用；
// 运行时控件脚本通过生成的序列化字段直接使用 UI 引用（零反射开销）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// QuickBind：UI 绑定表组件。
    /// </summary>
    [DisallowMultipleComponent]
    public class QuickBind : MonoBehaviour
    {
        /// <summary>单条绑定：字段名 → UI 物体。</summary>
        [Serializable]
        public class BindEntry
        {
            public string fieldName = "";       // 生成的 C# 字段名（如 m_BtnStart）
            public GameObject target = null;    // 绑定的 UI 物体
            public string typeName = "";        // 控件类型全名（UnityEngine.UI.Button 等）

            public BindEntry() { }

            public BindEntry(string name, GameObject obj, string type)
            {
                fieldName = name;
                target = obj;
                typeName = type;
            }
        }

        [SerializeField]
        private List<BindEntry> m_Bindings = new List<BindEntry>();

        /// <summary>绑定列表（Inspector / 生成器使用）。</summary>
        public List<BindEntry> Bindings { get { return m_Bindings; } }

        /// <summary>按字段名查找绑定物体。</summary>
        public GameObject GetTarget(string fieldName)
        {
            foreach (var entry in m_Bindings)
            {
                if (entry != null && entry.fieldName == fieldName)
                {
                    return entry.target;
                }
            }
            return null;
        }

        /// <summary>绑定表中是否已存在指定字段。</summary>
        public bool HasField(string fieldName)
        {
            foreach (var entry in m_Bindings)
            {
                if (entry != null && entry.fieldName == fieldName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>添加或更新一条绑定。</summary>
        public void SetBinding(string fieldName, GameObject target, string typeName)
        {
            foreach (var entry in m_Bindings)
            {
                if (entry != null && entry.fieldName == fieldName)
                {
                    entry.target = target;
                    entry.typeName = typeName;
                    return;
                }
            }
            m_Bindings.Add(new BindEntry(fieldName, target, typeName));
        }
    }
}
