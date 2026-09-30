//------------------------------------------------------------
// EmojiWar GameMain - 物品/法杖详情面板（ItemDetailPanel）
//
// 依据：doc/法术编程系统-执行文档.md D17/D18 + 设计文档 §8「UI 与呈现」；
//       数值口径 = 设计 §2.2 / §2.4 / §3.6 / §12.2（与 CastResolver / LoadoutCompiler.Preview 同源）
//
// 打开方式：`UiSlotView` 悬停 → `ItemDetailPanel.ShowItem(row, instance, screenPos)`。
// 面板资产：`Assets/GameMain/UI/items/ItemDetailPanel.prefab`
//   （生成器 Editor/ItemDetailPanelBuilder.cs；登记 Constant.UIItemAssetPath + GameResourceBuilder.AssetPaths）
//
// 显示内容（作者确认的四项）：
//   · 法术完整字段：触发类型 / 效果 / 标签 / 基础耗蓝 / 自身施法延迟 / 自身充能代价
//   · **"给后续物品的修正"独立成段**（耗蓝 ×/＋、延迟 ×/＋、作用后续 N 个）——这是法术编程的核心
//   · buff 施加、被动监听（事件 / 每次发射限次 / 冷却 / 触发目标）
//   · 法杖：槽数 / 魔力池 / 回魔 / 基础施法延迟 / 基础充能 + **已装序列清单**
//   · 预算预估：总耗蓝 / 序列时长 / 总充能 / 周期（`LoadoutCompiler.Preview`，纯 dry-run 不落账）
//   · 图标与色块：行首物品图标、标签按性质着色（元素=主色 / 结构=次色）
//
// 规范（doc/UI列表与Cell规范.md，AGENTS.md 一）：
//   · 每个动态段落 = 一个列表容器 + 作者态 Cell 模板（`UiListCell.Resolve` 取用，模板移到全局隐藏根）
//   · 刷新 = 先清空再生成：池内行 `Recycle`、非池残留 `Destroy`，容器内只有"当前应显示的行"
//   · 关闭 = `ClearContainer` + `pool.Destroy()` + `UiListCell.Release`
// 表现层定位：纯 UI，不参与模拟/网络（设计 §7）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.UI
{
    /// <summary>物品/法杖悬停详情面板。</summary>
    public sealed class ItemDetailPanel : MonoBehaviour
    {
        private const string PrefabPath = "Assets/GameMain/UI/items/ItemDetailPanel.prefab";
        private const float PanelWidth = 430f;

        // ---- 固定文本（预制体节点） ----
        [SerializeField] private Image m_Icon = null;
        [SerializeField] private Text m_Title = null;
        [SerializeField] private Text m_Subtitle = null;
        [SerializeField] private Text m_SectionFollow = null;
        [SerializeField] private Text m_SectionBuff = null;
        [SerializeField] private Text m_SectionForecast = null;
        [SerializeField] private Text m_SectionLoaded = null;

        // ---- 动态段落容器（各自一个 Cell 模板） ----
        [SerializeField] private RectTransform m_AttrContainer = null;
        [SerializeField] private RectTransform m_FollowContainer = null;
        [SerializeField] private RectTransform m_BuffContainer = null;
        [SerializeField] private RectTransform m_ForecastContainer = null;
        [SerializeField] private RectTransform m_LoadedContainer = null;

        private Canvas m_Canvas = null;
        private RectTransform m_Rect = null;

        private GameObject m_AttrCellSource = null;
        private GameObject m_FollowCellSource = null;
        private GameObject m_BuffCellSource = null;
        private GameObject m_ForecastCellSource = null;
        private GameObject m_LoadedCellSource = null;

        private GfUiItemPool m_AttrPool = null;
        private GfUiItemPool m_FollowPool = null;
        private GfUiItemPool m_BuffPool = null;
        private GfUiItemPool m_ForecastPool = null;
        private GfUiItemPool m_LoadedPool = null;

        private readonly StringBuilder m_Sb = new StringBuilder(256);

        // ==================== 生命周期（静态入口） ====================

        private static ItemDetailPanel s_Instance = null;
        private static bool s_Loading = false;

        /// <summary>当前是否已显示。</summary>
        public static bool IsVisible
        {
            get { return s_Instance != null && s_Instance.gameObject.activeSelf; }
        }

        /// <summary>
        /// 显示某个物品卡的详情（sprite/数值全部来自 SO 资产引用，禁止字符串路径）。
        /// 预制体未加载完成时先发起加载、本次不显示（下次悬停即出）。
        /// </summary>
        public static void ShowItem(ItemSO row, ItemInstance instance, Vector2 screenPos)
        {
            if (row == null) { Hide(); return; }
            var panel = EnsureInstance();
            if (panel == null) { return; }
            panel.Build(row, instance);
            panel.PlaceAt(screenPos);
            panel.gameObject.SetActive(true);
        }

        /// <summary>隐藏。</summary>
        public static void Hide()
        {
            if (s_Instance != null) { s_Instance.gameObject.SetActive(false); }
        }

        private static ItemDetailPanel EnsureInstance()
        {
            if (s_Instance != null) { return s_Instance; }

            var prefab = UiPrefab.GetCached(PrefabPath);
            if (prefab == null)
            {
                if (!s_Loading)
                {
                    s_Loading = true;
                    UiPrefab.Load(PrefabPath, p =>
                    {
                        s_Loading = false;
                        if (p == null) { WriteProbe("[itemdetail] 预制体加载失败: " + PrefabPath); }
                    });
                }
                return null;
            }

            var go = Object.Instantiate(prefab);
            go.name = "ItemDetailPanel";
            s_Instance = go.GetComponent<ItemDetailPanel>();
            if (s_Instance == null) { s_Instance = go.AddComponent<ItemDetailPanel>(); }
            s_Instance.AttachToOverlay();
            WriteProbe("[itemdetail] 预制体就绪: " + PrefabPath);
            return s_Instance;
        }

        /// <summary>挂到独立顶层 Canvas（不吃射线、不参与窗体布局）。</summary>
        private void AttachToOverlay()
        {
            var canvasGo = new GameObject("ItemDetailCanvas", typeof(Canvas), typeof(CanvasGroup));
            Object.DontDestroyOnLoad(canvasGo);
            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.overrideSorting = true;
            m_Canvas.sortingOrder = 30000;   // 与拖拽幽灵同层，高于普通窗体

            var group = canvasGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;    // 详情面板永不挡拖拽/点击
            group.interactable = false;

            m_Rect = transform as RectTransform;
            m_Rect.SetParent(canvasGo.transform, false);
            m_Rect.anchorMin = new Vector2(0.5f, 0.5f);
            m_Rect.anchorMax = new Vector2(0.5f, 0.5f);
            m_Rect.pivot = new Vector2(0f, 1f);
            m_Rect.sizeDelta = new Vector2(PanelWidth, 320f);

            gameObject.SetActive(false);
        }

        /// <summary>贴到指针旁（靠右/靠下越界则翻转，避免出屏）。</summary>
        private void PlaceAt(Vector2 screenPos)
        {
            if (m_Canvas == null || m_Rect == null) { return; }
            var canvasRect = m_Canvas.transform as RectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out local)) { return; }

            // 高度随内容自适应（布局组已算好 → 取 preferredHeight）
            float h = LayoutUtility.GetPreferredHeight(m_Rect);
            if (h < 80f) { h = 80f; }
            m_Rect.sizeDelta = new Vector2(PanelWidth, h);

            float x = local.x + 20f;
            float y = local.y - 20f;
            float halfW = canvasRect.rect.width * 0.5f;
            float halfH = canvasRect.rect.height * 0.5f;
            if (x + PanelWidth > halfW) { x = local.x - 20f - PanelWidth; }
            if (y - h < -halfH) { y = local.y + 20f + h; }
            m_Rect.anchoredPosition = new Vector2(x, y);
        }

        // ==================== 内容装配 ====================

        private void Build(ItemSO row, ItemInstance instance)
        {
            EnsureCellSources();

            if (m_Icon != null) { m_Icon.sprite = row.IconSprite; m_Icon.enabled = row.IconSprite != null; }
            if (m_Title != null) { m_Title.text = row.DisplayName; }
            if (m_Subtitle != null) { m_Subtitle.text = BuildSubtitle(row); }

            ClearAll();

            if (row.Category == ItemCategory.Spell && row.Spell != null)
            {
                BuildSpell(row.Spell, instance);
            }
            else if (row.Category == ItemCategory.Wand && row.Wand != null)
            {
                BuildWand(row.Wand, instance);
            }
            else if (m_AttrContainer != null)
            {
                AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "（无行为数据）", "", UIStyle.SubTextColor);
            }

            SetSectionVisible(m_SectionFollow, m_FollowContainer);
            SetSectionVisible(m_SectionBuff, m_BuffContainer);
            SetSectionVisible(m_SectionForecast, m_ForecastContainer);
            SetSectionVisible(m_SectionLoaded, m_LoadedContainer);
        }

        private static string BuildSubtitle(ItemSO row)
        {
            if (row.Category == ItemCategory.Wand) { return "法杖 · " + row.Rarity; }
            return "法术物品 · " + row.Rarity;
        }

        // ---------- 法术 ----------

        private void BuildSpell(SpellSO sp, ItemInstance instance)
        {
            // 1) 基础字段
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "触发类型", sp.TriggerType.ToString(), UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "效果", DescribeEffect(sp), UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "标签", DescribeTags(sp), UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "基础耗蓝", sp.ManaCost.ToString(), UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "自身施法延迟", Sign(sp.DelayAdd) + "s", UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "自身充能代价", Sign(sp.RechargeAdd) + "s", UIStyle.TextColor);

            // 条件门 / 延迟 / 序列操作等附加说明
            if (sp.TriggerType == SpellTriggerType.Conditional) { AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "条件", DescribeCondition(sp), UIStyle.SubTextColor); }
            if (sp.TriggerType == SpellTriggerType.Delayed)
            {
                // [W-10a] 配置里的延迟是**毫秒**；顺带把"当前帧率下量化成几帧"也显示出来（便于对照手感）
                int frames = EmojiWar.GameMain.Simulation.CastResolver.FramesOf(sp.DelayMs / 1000f);
                AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "延迟触发",
                    sp.DelayMs + " ms（当前帧率 " + frames + " 帧）", UIStyle.SubTextColor);
            }
            if (sp.SequenceOp != SpellSequenceOp.None) { AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "序列操作", sp.SequenceOp + (sp.Operand > 0 ? (" × " + sp.Operand) : ""), UIStyle.SubTextColor); }

            // 2) 给后续物品的修正（法术编程的核心，单独成段）
            if (sp.IsModifier)
            {
                string scope = sp.TargetScope + (sp.AffectCount > 0 ? ("（" + sp.AffectCount + " 个）") : "");
                if (sp.ManaMul != 1f) { AddRow(m_FollowContainer, m_FollowPool, m_FollowCellSource, null, "后续耗蓝", "× " + sp.ManaMul.ToString("0.##"), UIStyle.SecondaryColor); }
                if (sp.ManaAdd != 0) { AddRow(m_FollowContainer, m_FollowPool, m_FollowCellSource, null, "后续耗蓝", Sign(sp.ManaAdd), UIStyle.SecondaryColor); }
                if (sp.DelayMul != 1f) { AddRow(m_FollowContainer, m_FollowPool, m_FollowCellSource, null, "后续延迟", "× " + sp.DelayMul.ToString("0.##"), UIStyle.SecondaryColor); }
                if (sp.DelayAdd != 0f) { AddRow(m_FollowContainer, m_FollowPool, m_FollowCellSource, null, "后续延迟", Sign(sp.DelayAdd) + "s", UIStyle.SecondaryColor); }
                AddRow(m_FollowContainer, m_FollowPool, m_FollowCellSource, null, "作用范围", scope, UIStyle.SubTextColor);
            }

            // 3) buff（第三层）
            if (sp.AppliesBuff)
            {
                var b = sp.BuffApply;
                AddRow(m_BuffContainer, m_BuffPool, m_BuffCellSource, null,
                    string.IsNullOrEmpty(b.DisplayName) ? b.BuffKey : b.DisplayName,
                    b.Stat + " " + Sign(b.ValuePerStack) + " /层 · 上限 " + b.MaxStacks + " 层 · " + DescribeTiming(b),
                    b.IsNegative ? UIStyle.DangerColor : UIStyle.SecondaryColor);
            }

            // 4) 被动（第四层）
            if (sp.IsPassive)
            {
                var pv = sp.Passive;
                AddRow(m_BuffContainer, m_BuffPool, m_BuffCellSource, null,
                    "监听事件", pv.Event.ToString(), UIStyle.PrimaryColor);
                AddRow(m_BuffContainer, m_BuffPool, m_BuffCellSource, null,
                    "触发目标",
                    pv.Scope + (pv.AffectCount > 0 ? (" × " + pv.AffectCount) : "") + " · " + pv.Order,
                    UIStyle.SubTextColor);
                AddRow(m_BuffContainer, m_BuffPool, m_BuffCellSource, null,
                    "限制",
                    "每次发射 " + pv.LimitPerCast + " 次 · 冷却 " + pv.CooldownMs + " ms · "
                        + (pv.ManaCost > 0 ? ("后扣 " + pv.ManaCost + " 蓝") : "免费"),
                    UIStyle.SubTextColor);
            }

            // 5) 已装填提示（装到槽里之后的具体效果见法杖详情）
            if (instance.ItemId > 0)
            {
                AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null,
                    "该卡已装填", "见法杖详情的预算预估", UIStyle.SubTextColor);
            }
        }

        // ---------- 法杖 ----------

        private void BuildWand(WandSO w, ItemInstance instance)
        {
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "法术槽数", w.SlotCount.ToString(), UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "魔力池", w.ManaMax.ToString(), UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "回魔", w.ManaRegen.ToString("F0") + " /s", UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "基础施法延迟", w.CastDelay.ToString("F2") + "s", UIStyle.TextColor);
            AddRow(m_AttrContainer, m_AttrPool, m_AttrCellSource, null, "基础充能", w.RechargeTime.ToString("F2") + "s", UIStyle.TextColor);

            // 已装序列清单（图标 + 名称 + 槽位）
            var spells = instance.SpellSlots;
            int loaded = instance.LoadedSpellCount();
            AddRow(m_LoadedContainer, m_LoadedPool, m_LoadedCellSource, null,
                "已装填", loaded + " / " + w.SlotCount, UIStyle.TextColor);

            for (int i = 0; i < w.SlotCount; i++)
            {
                int cardId = (spells != null && i < spells.Length) ? spells[i] : 0;
                var cardRow = cardId > 0 ? ConfigService.GetItem(cardId) : null;
                string name = cardRow != null ? cardRow.DisplayName : "空槽";
                Sprite icon = cardRow != null ? cardRow.IconSprite : null;
                AddRow(m_LoadedContainer, m_LoadedPool, m_LoadedCellSource, icon,
                    (i + 1) + ". " + name,
                    cardRow != null ? (cardRow.Spell != null ? cardRow.Spell.ManaCost + " 蓝" : "") : "",
                    cardRow != null ? UIStyle.TextColor : UIStyle.SubTextColor);
            }

            // 预算预估：用实际装填编译一把杖（dry-run，不落账）
            var table = ConfigItemTable.Instance;
            int[] slotIds = new int[w.SlotCount];
            bool any = false;
            for (int i = 0; i < w.SlotCount; i++)
            {
                int cardId = (spells != null && i < spells.Length) ? spells[i] : 0;
                slotIds[i] = cardId;
                if (cardId > 0) { any = true; }
            }

            if (!any)
            {
                AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null, "空序列", "不施法、不进冷却", UIStyle.SubTextColor);
                return;
            }

            var program = CompileFor(w, slotIds, table);
            var preview = LoadoutCompiler.Preview(program);

            AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null, "总耗蓝", preview.ManaTotal + " / " + w.ManaMax, UIStyle.PrimaryColor);
            AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null, "序列时长", preview.SequenceDuration.ToString("F2") + "s", UIStyle.PrimaryColor);
            AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null, "总充能", preview.TotalRecharge.ToString("F2") + "s", UIStyle.PrimaryColor);
            AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null, "一次发射周期", preview.CycleSeconds.ToString("F2") + "s", UIStyle.PrimaryColor);
            if (preview.ManaExceedsPool)
            {
                AddRow(m_ForecastContainer, m_ForecastPool, m_ForecastCellSource, null,
                    "注意", "超魔力池：将在第 " + (preview.AbortSlot + 1) + " 个物品处按 Q2 中止", UIStyle.DangerColor);
            }
        }

        /// <summary>按"该杖 + 给定槽位装填"编译一把程序（仅用于预览；不写任何存档/状态）。</summary>
        private static CastProgram CompileFor(WandSO w, int[] cardIds, IItemTable table)
        {
            var spells = new CastSpellData[w.SlotCount];
            for (int i = 0; i < w.SlotCount; i++)
            {
                spells[i] = (cardIds != null && i < cardIds.Length && cardIds[i] > 0)
                    ? LoadoutCompiler.CompileSpell(cardIds[i], table, i)
                    : CastSpellData.Empty;
            }
            return new CastProgram(w.Id, w.SlotCount, w.CastDelay, w.RechargeTime,
                w.ManaMax, w.ManaRegen, w.Mode, spells);
        }

        // ==================== 行/段落管理（规范：先清空再生成） ====================

        private void EnsureCellSources()
        {
            m_AttrCellSource = UiListCell.Resolve(m_AttrContainer, "ItemDetail/Attr", PrefabPath, m_AttrCellSource, null);
            m_FollowCellSource = UiListCell.Resolve(m_FollowContainer, "ItemDetail/Follow", PrefabPath, m_FollowCellSource, null);
            m_BuffCellSource = UiListCell.Resolve(m_BuffContainer, "ItemDetail/Buff", PrefabPath, m_BuffCellSource, null);
            m_ForecastCellSource = UiListCell.Resolve(m_ForecastContainer, "ItemDetail/Forecast", PrefabPath, m_ForecastCellSource, null);
            m_LoadedCellSource = UiListCell.Resolve(m_LoadedContainer, "ItemDetail/Loaded", PrefabPath, m_LoadedCellSource, null);
        }

        private void AddRow(RectTransform container, GfUiItemPool pool, GameObject cellSource,
            Sprite icon, string name, string value, Color nameColor)
        {
            if (container == null || cellSource == null) { return; }
            if (pool == null) { return; }

            var go = pool.Acquire(() => Object.Instantiate(cellSource), container);
            if (go == null) { return; }
            var cell = go.GetComponent<ItemDetailCell>();
            if (cell == null) { cell = go.AddComponent<ItemDetailCell>(); }
            cell.Set(icon, name, value, nameColor);
        }

        /// <summary>清空一个容器：池内行回收，非池残留（多放的模板/历史残留）销毁。</summary>
        private static void ClearContainer(RectTransform container, GfUiItemPool pool)
        {
            if (container == null) { return; }
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (child == null) { continue; }
                var go = child.gameObject;
                if (pool != null && pool.IsTracked(go)) { pool.Recycle(go); }
                else if (Application.isPlaying) { Object.Destroy(go); }
                else { Object.DestroyImmediate(go); }
            }
        }

        private void ClearAll()
        {
            ClearContainer(m_AttrContainer, m_AttrPool);
            ClearContainer(m_FollowContainer, m_FollowPool);
            ClearContainer(m_BuffContainer, m_BuffPool);
            ClearContainer(m_ForecastContainer, m_ForecastPool);
            ClearContainer(m_LoadedContainer, m_LoadedPool);
        }

        private static void SetSectionVisible(Text header, RectTransform container)
        {
            bool show = container != null && container.childCount > 0;
            if (header != null) { header.gameObject.SetActive(show); }
            if (container != null) { container.gameObject.SetActive(show); }
        }

        private void OnDestroy()
        {
            ReleasePools();
            s_Instance = null;
        }

        private void ReleasePools()
        {
            if (m_AttrPool != null) { m_AttrPool.Destroy(); m_AttrPool = null; }
            if (m_FollowPool != null) { m_FollowPool.Destroy(); m_FollowPool = null; }
            if (m_BuffPool != null) { m_BuffPool.Destroy(); m_BuffPool = null; }
            if (m_ForecastPool != null) { m_ForecastPool.Destroy(); m_ForecastPool = null; }
            if (m_LoadedPool != null) { m_LoadedPool.Destroy(); m_LoadedPool = null; }

            UiListCell.Release(ref m_AttrCellSource);
            UiListCell.Release(ref m_FollowCellSource);
            UiListCell.Release(ref m_BuffCellSource);
            UiListCell.Release(ref m_ForecastCellSource);
            UiListCell.Release(ref m_LoadedCellSource);
        }

        private void EnsurePools()
        {
            string key = GetInstanceID().ToString();
            if (m_AttrPool == null) { m_AttrPool = GfUiItemPool.Create("ItemDetailAttr_" + key, 32); }
            if (m_FollowPool == null) { m_FollowPool = GfUiItemPool.Create("ItemDetailFollow_" + key, 16); }
            if (m_BuffPool == null) { m_BuffPool = GfUiItemPool.Create("ItemDetailBuff_" + key, 16); }
            if (m_ForecastPool == null) { m_ForecastPool = GfUiItemPool.Create("ItemDetailForecast_" + key, 16); }
            if (m_LoadedPool == null) { m_LoadedPool = GfUiItemPool.Create("ItemDetailLoaded_" + key, 16); }
        }

        // ==================== 文案辅助 ====================

        private void Awake()
        {
            EnsurePools();
        }

        private static string Sign(float v)
        {
            return (v >= 0f ? "+" : "") + v.ToString("0.##");
        }

        private static string Sign(int v)
        {
            return (v >= 0 ? "+" : "") + v;
        }

        private static string DescribeEffect(SpellSO sp)
        {
            switch (sp.EffectKind)
            {
                case SpellEffectKind.FireProjectile:
                    return sp.Projectile != null
                        ? ("发射投射物（伤害 " + sp.Projectile.Damage.ToString("0.#") + " / 速度 " + sp.Projectile.Speed.ToString("0.#") + "）")
                        : "发射投射物（未配弹道）";
                case SpellEffectKind.ApplyBuff: return "施加 Buff";
                case SpellEffectKind.SequenceOp: return "序列操作";
                case SpellEffectKind.ModifyResource: return "资源" + Sign(sp.ManaDelta) + " 蓝";
                case SpellEffectKind.TriggerItem: return "触发其他物品";
                default: return "无直接效果（纯修饰/控制）";
            }
        }

        private static string DescribeTags(SpellSO sp)
        {
            m_TagSb.Length = 0;
            AppendTags(sp.Tags, SpellTag.Fire, "火焰");
            AppendTags(sp.Tags, SpellTag.Ice, "冰");
            AppendTags(sp.Tags, SpellTag.Physical, "物理");
            AppendTags(sp.Tags, SpellTag.Lightning, "闪电");
            AppendTags(sp.Tags, SpellTag.Energy, "能量");
            AppendTags(sp.Tags, SpellTag.Void, "虚空");
            AppendTags(sp.Tags, SpellTag.Soul, "灵魂");
            AppendTags(sp.Tags, SpellTag.Time, "时间");
            AppendTags(sp.Tags, SpellTag.Summon, "召唤");
            AppendStructTags(sp.StructTags, SpellStructTag.Projectile, "投射物");
            AppendStructTags(sp.StructTags, SpellStructTag.Modifier, "修饰");
            AppendStructTags(sp.StructTags, SpellStructTag.Terminate, "终止");
            AppendStructTags(sp.StructTags, SpellStructTag.Passive, "被动");
            AppendStructTags(sp.StructTags, SpellStructTag.Control, "控制");
            AppendStructTags(sp.StructTags, SpellStructTag.Interaction, "交互");
            AppendStructTags(sp.StructTags, SpellStructTag.Buff, "buff");
            return m_TagSb.Length == 0 ? "—" : m_TagSb.ToString();
        }

        private static readonly StringBuilder m_TagSb = new StringBuilder(64);

        private static void AppendTags(SpellTag all, SpellTag one, string name)
        {
            if ((all & one) == 0) { return; }
            if (m_TagSb.Length > 0) { m_TagSb.Append(" · "); }
            m_TagSb.Append(name);
        }

        private static void AppendStructTags(SpellStructTag all, SpellStructTag one, string name)
        {
            if ((all & one) == 0) { return; }
            if (m_TagSb.Length > 0) { m_TagSb.Append(" · "); }
            m_TagSb.Append(name);
        }

        private static string DescribeCondition(SpellSO sp)
        {
            var c = sp.Condition;
            m_TagSb.Length = 0;
            if (c.RequiredTags != SpellTag.None) { m_TagSb.Append("需要元素标签 ").Append(c.RequiredTags); }
            if (c.RequiredStructTags != SpellStructTag.None)
            {
                if (m_TagSb.Length > 0) { m_TagSb.Append(" · "); }
                m_TagSb.Append("需要结构标签 ").Append(c.RequiredStructTags);
            }
            if (c.MinManaCost > 0)
            {
                if (m_TagSb.Length > 0) { m_TagSb.Append(" · "); }
                m_TagSb.Append("耗蓝 ≥ ").Append(c.MinManaCost);
            }
            if (c.Invert) { m_TagSb.Append("（反转）"); }
            return m_TagSb.Length == 0 ? "—" : m_TagSb.ToString();
        }

        private static string DescribeTiming(BuffApplyDef b)
        {
            switch (b.Timing)
            {
                case BuffTiming.ByTriggerCount: return "再触发 " + b.Duration + " 次";
                case BuffTiming.ByCastCount: return "再施法 " + b.Duration + " 次";
                // [W-10a] 配置里的时间型时长是**毫秒**（运行时才量化成帧）
                case BuffTiming.BySeconds: return (b.DurationMs / 1000f).ToString("F2") + "s";
                default: return b.Duration.ToString();
            }
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
