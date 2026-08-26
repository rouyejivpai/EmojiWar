//------------------------------------------------------------
// EmojiWar GameMain - QuickBind 生成器（Editor）
// 为挂载 QuickBind 的 UI 根自动完成：
//   1. 扫描子物体，按命名约定识别控件（btn_/txt_/img_/input_/slot_）
//   2. 生成 partial C# 绑定代码（字段 + QuickBindApplyBindings 方法）
// 用法：
//   1. 控件脚本声明为 partial（如 public partial class RoomForm : UGuiForm）
//   2. UI 根挂 QuickBind，子物体按约定命名
//   3. Inspector 点 [Scan & Generate]
//------------------------------------------------------------

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// QuickBind 生成器与 Inspector 面板。
    /// </summary>
    [CustomEditor(typeof(QuickBind))]
    public class QuickBindGenerator : UnityEditor.Editor
    {
        private const string PrefixButton = "btn_";
        private const string PrefixText = "txt_";
        private const string PrefixImage = "img_";
        private const string PrefixInput = "input_";
        private const string PrefixSlot = "slot_";

        public override void OnInspectorGUI()
        {
            var bind = (QuickBind)target;

            EditorGUILayout.LabelField("QuickBind 绑定表", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Scan & Generate 绑定代码"))
            {
                ScanAndGenerate(bind);
            }
            if (GUILayout.Button("仅扫描（填充绑定表）"))
            {
                ScanBindings(bind);
                EditorUtility.SetDirty(bind);
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        /// <summary>扫描子物体填充绑定表 + 生成代码。</summary>
        private void ScanAndGenerate(QuickBind bind)
        {
            Process(bind.gameObject);
        }

        /// <summary>
        /// 静态入口：扫描指定 UI 根（需挂 QuickBind）并生成绑定代码。
        /// 供其他编辑器工具（场景/prefab 构建器）调用。
        /// </summary>
        public static void Process(GameObject uiRoot)
        {
            var bind = uiRoot != null ? uiRoot.GetComponent<QuickBind>() : null;
            if (bind == null)
            {
                Debug.LogWarning("[QuickBind] 目标物体未挂 QuickBind 组件，跳过: " + (uiRoot != null ? uiRoot.name : "null"));
                return;
            }

            ScanBindings(bind);
            GenerateCode(bind);
            EditorUtility.SetDirty(bind);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[QuickBind] 扫描并生成完成，绑定数 " + bind.Bindings.Count);
        }

        /// <summary>遍历子物体，按命名约定识别控件并填充绑定表。</summary>
        private static void ScanBindings(QuickBind bind)
        {
            bind.Bindings.Clear();

            foreach (Transform child in bind.transform)
            {
                ScanChild(bind, child);
            }
        }

        private static void ScanChild(QuickBind bind, Transform node)
        {
            string name = node.name;

            // 按命名约定识别控件类型
            string typeName = null;
            if (name.StartsWith(PrefixButton))
            {
                typeName = "UnityEngine.UI.Button";
            }
            else if (name.StartsWith(PrefixText))
            {
                typeName = "UnityEngine.UI.Text";
            }
            else if (name.StartsWith(PrefixImage))
            {
                typeName = "UnityEngine.UI.Image";
            }
            else if (name.StartsWith(PrefixInput))
            {
                typeName = "UnityEngine.UI.InputField";
            }
            else if (name.StartsWith(PrefixSlot))
            {
                typeName = "UnityEngine.UI.Image";
            }

            if (typeName != null)
            {
                // 字段名：m_ + 帕斯卡（btn_start -> m_BtnStart）
                string fieldName = ToFieldName(name);
                bind.SetBinding(fieldName, node.gameObject, typeName);
            }

            // 递归子物体（只扫一层容器内的控件）
            foreach (Transform child in node)
            {
                ScanChild(bind, child);
            }
        }

        /// <summary>
        /// 把控件名转为字段名（保留类型前缀，转帕斯卡）：btn_start -> m_BtnStart。
        /// </summary>
        private static string ToFieldName(string raw)
        {
            var sb = new StringBuilder("m_");
            bool upperNext = true;
            foreach (char c in raw)
            {
                if (c == '_')
                {
                    upperNext = true;
                    continue;
                }
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            return sb.ToString();
        }

        /// <summary>为控件脚本生成 partial 绑定代码。</summary>
        private static void GenerateCode(QuickBind bind)
        {
            if (bind.Bindings.Count == 0)
            {
                Debug.LogWarning("[QuickBind] 绑定表为空，未生成代码");
                return;
            }

            // 控件脚本：与 QuickBind 同物体的第一个 MonoBehaviour（排除 QuickBind 自身）
            var controlScript = FindControlScript(bind);
            if (controlScript == null)
            {
                Debug.LogWarning("[QuickBind] 未找到控件脚本（UI 根上应同时挂控件脚本与 QuickBind）");
                return;
            }

            var monoScript = MonoScript.FromMonoBehaviour(controlScript);
            var classType = monoScript.GetClass();
            if (classType == null)
            {
                Debug.LogWarning("[QuickBind] 无法解析控件脚本类型: " + controlScript.name);
                return;
            }

            string className = classType.Name;
            string namespaceName = classType.Namespace;
            string scriptPath = AssetDatabase.GetAssetPath(monoScript);
            if (string.IsNullOrEmpty(scriptPath))
            {
                Debug.LogWarning("[QuickBind] 无法定位控件脚本路径");
                return;
            }

            string outputPath = scriptPath.Replace(".cs", ".QuickBind.cs");
            var sb = new StringBuilder();
            sb.AppendLine("//------------------------------------------------------------");
            sb.AppendLine("// Auto-generated by QuickBind. Do not edit manually.");
            sb.AppendLine("//------------------------------------------------------------");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("namespace " + namespaceName);
                sb.AppendLine("{");
            }
            sb.AppendLine("public partial class " + className);
            sb.AppendLine("{");
            foreach (var entry in bind.Bindings)
            {
                sb.AppendLine("    [SerializeField] private " + entry.typeName + " " + entry.fieldName + " = null;");
            }
            sb.AppendLine();
            sb.AppendLine("    /// <summary>由 QuickBind 生成：应用绑定引用。</summary>");
            sb.AppendLine("    public void QuickBindApplyBindings(EmojiWar.GameMain.UI.QuickBind bind)");
            sb.AppendLine("    {");
            foreach (var entry in bind.Bindings)
            {
                sb.AppendLine("        var go_" + entry.fieldName + " = bind.GetTarget(\"" + entry.fieldName + "\");");
                sb.AppendLine("        if (go_" + entry.fieldName + " != null)");
                sb.AppendLine("        {");
                sb.AppendLine("            " + entry.fieldName + " = go_" + entry.fieldName + ".GetComponent<" + entry.typeName + ">();");
                sb.AppendLine("        }");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[QuickBind] 生成绑定代码: " + outputPath + "（" + bind.Bindings.Count + " 条绑定）");
        }

        /// <summary>
        /// 查找控件脚本（UI 根上与 QuickBind 同物体/子物体上、
        /// 属于本项目命名空间（EmojiWar）的 MonoBehaviour，排除 QuickBind 自身）。
        /// </summary>
        private static MonoBehaviour FindControlScript(QuickBind bind)
        {
            var root = bind.transform;
            // 优先根上的脚本
            foreach (var comp in root.GetComponents<MonoBehaviour>())
            {
                if (comp != null && comp.GetType() != typeof(QuickBind) && IsProjectScript(comp))
                {
                    return comp;
                }
            }
            // 其次子物体上的脚本（如控件脚本挂在子物体）
            foreach (var comp in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp != null && comp.GetType() != typeof(QuickBind) && IsProjectScript(comp))
                {
                    return comp;
                }
            }
            return null;
        }

        /// <summary>是否为本项目（EmojiWar）脚本，排除 UnityEngine/UnityEditor 内置组件。</summary>
        private static bool IsProjectScript(MonoBehaviour comp)
        {
            var ns = comp.GetType().Namespace;
            return !string.IsNullOrEmpty(ns) && (ns.StartsWith("EmojiWar") || ns.StartsWith("UnityGameFramework"));
        }
    }
}
