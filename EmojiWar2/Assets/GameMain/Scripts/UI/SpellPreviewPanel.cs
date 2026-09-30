//------------------------------------------------------------
// EmojiWar GameMain - 法术序列预览面板（SpellPreviewPanel，D17 / S6）
//
// 依据：doc/法术编程系统-执行文档.md D17 + §3 流程 J；设计文档 §8「UI 与呈现」
//   「预览面板：本次施法的**总耗蓝 / 总延迟 / 总充能**预估 + 触发顺序预览（含被动触发点）」
//
// 口径（P7 已定）：**只做主序列静态预估**
//   · 总耗蓝 / 总延迟 / 总充能 / 周期 全部来自 `LoadoutCompiler.Preview`（dry-run，不落账）；
//   · 耗蓝与 `CastResolver` 实算**逐项一致**（已由 CastSelfTest 断言）；
//   · 被动只标"可能触发"，**不参与数值**。
//
// 工程约定：
//   · `BackpackForm.prefab` 是**用户手改过的资产**（AGENTS.md 第五条：不得被生成器重建），
//     因此本面板**完全由代码构建**（不新增预制体节点、不改 QuickBind），
//     既不动手工布局，也不需要重打 AssetBundle；
//   · 进度/文本类刷新只走"给目标值"，不每帧写布局（与 UiBarSmoother 同精神）。
//
// [探针] 每次刷新写一行 `[spellpreview] ...`（-autospell / -autodrag 可断言）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>序列预览面板：两手各自一行预算 + 触发顺序。</summary>
    public sealed class SpellPreviewPanel : MonoBehaviour
    {
        /// <summary>面板节点名（挂在背包面板下；探针与自动化用它找实例）。</summary>
        public const string NodeName = "spell_Preview";

        private Text m_Text = null;
        private readonly StringBuilder m_Sb = new StringBuilder(512);
        private InventoryService m_Service = null;
        private bool m_Subscribed = false;

        // 缓存上次文本：内容不变就不写 Text（避免无谓的 Canvas 重建）
        private string m_LastText = null;

        /// <summary>最近一次刷新的探针行（供自动化断言）。</summary>
        public string LastProbe { get; private set; }

        /// <summary>
        /// 在指定父节点下取用（不存在则**代码构建**）预览面板。
        /// </summary>
        public static SpellPreviewPanel Attach(RectTransform parent, InventoryService service)
        {
            if (parent == null) { return null; }

            var existing = parent.Find(NodeName) as RectTransform;
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(NodeName, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                // 左下角停靠：锚点 (0,0)，偏移留出边距（分辨率无关）
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(24f, 24f);
                rt.sizeDelta = new Vector2(760f, 190f);

                var bg = go.AddComponent<Image>();
                bg.color = UIStyle.BackgroundColor;
                bg.raycastTarget = false;   // 面板不吃拖拽射线，避免挡住背包拖拽
            }

            var panel = go.GetComponent<SpellPreviewPanel>();
            if (panel == null) { panel = go.AddComponent<SpellPreviewPanel>(); }
            panel.BuildText();
            panel.Bind(service);
            return panel;
        }

        /// <summary>构建/取用文本节点（幂等）。</summary>
        private void BuildText()
        {
            if (m_Text != null) { return; }
            var t = transform.Find("txt_Preview") as RectTransform;
            if (t == null)
            {
                var go = new GameObject("txt_Preview", typeof(RectTransform));
                t = (RectTransform)go.transform;
                t.SetParent(transform, false);
                t.anchorMin = Vector2.zero;
                t.anchorMax = Vector2.one;
                t.offsetMin = new Vector2(14f, 10f);
                t.offsetMax = new Vector2(-14f, -10f);
            }
            m_Text = t.GetComponent<Text>();
            if (m_Text == null) { m_Text = t.gameObject.AddComponent<Text>(); }
            m_Text.font = UIStyleFont();
            m_Text.fontSize = UIStyle.FontBody;
            m_Text.color = UIStyle.TextColor;
            m_Text.alignment = TextAnchor.UpperLeft;
            m_Text.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_Text.verticalOverflow = VerticalWrapMode.Overflow;
            m_Text.raycastTarget = false;
            m_Text.supportRichText = true;
        }

        /// <summary>取一个可用字体（优先内置 Arial；失败则由 Unity 默认字体兜底）。</summary>
        private static Font UIStyleFont()
        {
            var f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f == null) { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            return f;
        }

        /// <summary>绑定物品服务并订阅槽位变化（先松后订）。</summary>
        public void Bind(InventoryService service)
        {
            if (m_Service == service && m_Subscribed) { return; }
            if (m_Service != null && m_Subscribed) { m_Service.SlotDirty -= OnSlotDirty; }
            m_Service = service;
            if (m_Service != null)
            {
                m_Service.SlotDirty += OnSlotDirty;
                m_Subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (m_Service != null && m_Subscribed) { m_Service.SlotDirty -= OnSlotDirty; }
            m_Subscribed = false;
        }

        private void OnSlotDirty(SlotRef slot) { Refresh(); }

        /// <summary>重新计算并刷新文本（关闭时也可安全调用）。</summary>
        public void Refresh()
        {
            BuildText();
            if (m_Text == null) { return; }

            m_Sb.Length = 0;
            var table = ConfigItemTable.Instance;

            AppendHand("左手 · 左键", ItemSystem.HandLeftId, table);
            m_Sb.Append('\n');
            AppendHand("右手 · 右键", ItemSystem.HandRightId, table);

            string text = m_Sb.ToString();
            if (text != m_LastText)
            {
                m_Text.text = text;
                m_LastText = text;
            }

            LastProbe = "[spellpreview] " + text.Replace('\n', '|');
            WriteProbe(LastProbe);
        }

        /// <summary>一只手的预估行：法杖 + 已装/槽数 + 总耗蓝/延迟/充能/周期 + 触发顺序。</summary>
        private void AppendHand(string label, string handId, IItemTable table)
        {
            var program = LoadoutCompiler.CompileHand(handId, table, m_Service);
            if (!program.IsValid)
            {
                m_Sb.Append("<b>").Append(label).Append("</b>：<color=#C0C4CC>无法杖（该手不施法）</color>");
                return;
            }

            var wandRow = ConfigService.GetWand(program.WandId);
            string wandName = wandRow != null ? wandRow.DisplayName : ("法杖#" + program.WandId);
            m_Sb.Append("<b>").Append(label).Append("</b> · ").Append(wandName)
                .Append("  <color=#C0C4CC>").Append(program.LoadedCount).Append('/').Append(program.SlotCount).Append(" 槽</color>");

            var pv = LoadoutCompiler.Preview(program);
            if (pv.ItemCount <= 0)
            {
                m_Sb.Append("\n  <color=#C0C4CC>空序列（不施法、不进冷却）</color>");
                return;
            }

            m_Sb.Append("\n  总耗蓝 <b>").Append(pv.ManaTotal).Append("</b>/").Append(program.ManaMax)
                .Append("  ·  序列时长 <b>").Append(pv.SequenceDuration.ToString("F2")).Append("s</b>")
                .Append("  ·  总充能 <b>").Append(pv.TotalRecharge.ToString("F2")).Append("s</b>")
                .Append("  ·  周期 <b>").Append(pv.CycleSeconds.ToString("F2")).Append("s</b>");

            if (pv.ManaExceedsPool)
            {
                m_Sb.Append("  <color=#D94040>（超魔力池：将在槽 ").Append(pv.AbortSlot).Append(" 触发 Q2 中止）</color>");
            }

            m_Sb.Append("\n  触发：").Append(BuildOrder(program));
        }

        /// <summary>触发顺序预览（主序列；被动物品标"被动·事件驱动"，不占主指针）。</summary>
        private string BuildOrder(in CastProgram program)
        {
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < program.SlotCount; i++)
            {
                var sp = program.SpellAt(i);
                if (sp.IsEmpty) { continue; }

                if (shown > 0) { sb.Append(" <color=#8A8F99>→</color> "); }
                shown++;

                var itemRow = ConfigService.GetItem(sp.ItemId);
                string name = itemRow != null ? itemRow.DisplayName : ("法术#" + sp.SpellId);
                bool passive = sp.SkipsMainCursor;

                if (passive)
                {
                    sb.Append("<color=#C0C4CC>[").Append(name).Append(" 被动]</color>");
                }
                else
                {
                    sb.Append(shown).Append('.').Append(name);
                }
            }
            if (shown == 0) { sb.Append("<color=#C0C4CC>—</color>"); }
            return sb.ToString();
        }

        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
