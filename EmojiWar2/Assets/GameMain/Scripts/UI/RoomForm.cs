//------------------------------------------------------------
// EmojiWar GameMain - 联机房间界面（QuickBind 示例）
// UI 引用由 QuickBind 自动生成绑定（RoomForm.QuickBind.cs）：
//   根物体挂 QuickBind + 子物体按约定命名（btn_/txt_/...），
//   Inspector 点 [Scan & Generate] 生成字段绑定代码。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>房间事件（准备/离开）。</summary>
    public static class RoomFormEvents
    {
        public static event Action OnReadyRequested;
        public static event Action OnLeaveRequested;

        public static void RequestReady() { OnReadyRequested?.Invoke(); }
        public static void RequestLeave() { OnLeaveRequested?.Invoke(); }
    }

    /// <summary>
    /// 联机房间界面（partial：UI 字段由 QuickBind 生成）。
    /// 布局：停靠屏幕右侧边缘（窄条），包含：标题、玩家列表（PlayerCell 子物体列表：
    ///   每行 = {角色emoji, 玩家名, 准备状态}，GameFramework 对象池复用）、
    /// 准备/离开按钮等（btn_ChangeChar 已随手动布局移除，角色面板默认展开）。
    /// </summary>
    public partial class RoomForm : UGuiForm
    {
        private bool m_LocalReady = false;

        /// <summary>本机玩家名（RoomForm 打开前由流程注入，用于列表高亮自己）。</summary>
        public static string LocalPlayerName = string.Empty;

        /// <summary>本机玩家名（用于列表高亮自己的行）。</summary>
        public string LocalName { get { return LocalPlayerName ?? string.Empty; } }

        /// <summary>最近一次玩家列表字符串（测试/诊断用）。</summary>
        public string LastPlayerList { get; private set; } = string.Empty;

        /// <summary>本机是否已准备。</summary>
        public bool LocalReady { get { return m_LocalReady; } }

        /// <summary>角色抽屉当前是否展开。</summary>
        public bool IsCharDockOpen { get; private set; } = false;

        /// <summary>角色抽屉（右侧，与房间面板共存；打开时滑入，关闭时滑出）。</summary>
        private CharacterDockForm m_CharDock = null;

        // ---- 玩家列表：列表项 = PlayerCell 预制体子物体，经 GameFramework 对象池(GfUiItemPool)复用 ----
        // PlayerCell.prefab 位于 Assets/GameMain/UI/items/（见 Constant.UIItemAssetPath），
        // 属 'game' AssetBundle，经 UiPrefab(GameEntry.Resource) 按全路径加载。
        private const string PlayerCellPoolName = "RoomPlayerCells";
        private const string PlayerListContainerName = "PlayerListContainer";   // 原 txt_PlayerList 转成的纯容器节点名
        private const float PlayerRowGap = 4f;
        private const float PlayerRowMaxH = 120f;
        private const float PlayerRowMinH = 54f;

        private GfUiItemPool m_CellPool = null;      // GameFramework 对象池封装
        private GameObject m_CellPrefab = null;
        private RectTransform m_ListContainer = null;   // 原 txt_PlayerList 节点（仅当容器矩形，保留手动布局）
        private VerticalLayoutGroup m_RowLayout = null; // 容器上的布局组（存在时由其排布并尊重子物体尺寸）
        private readonly List<GameObject> m_PlayerRows = new List<GameObject>();   // 当前活动行（顺序与玩家一致）

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            // QuickBind：应用生成的 UI 引用绑定
            var bind = GetComponent<QuickBind>();
            if (bind != null)
            {
                QuickBindApplyBindings(bind);
            }
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            RoomEvents.OnPlayerListUpdated += OnPlayerListUpdated;
            RoomEvents.OnLocalReadyChanged += OnLocalReadyChanged;
            CharacterDockEvents.OnDockOpened += OnCharDockOpened;
            CharacterDockEvents.OnCharacterChanged += OnDockCharChanged;

            // 诊断：验证按钮引用是否在构建版正确绑定（QuickBind 绑定表 → m_BtnReady）
            // 若为 null，说明 AssetBundle(game.dat) 里的 prefab 绑定表为空（未重建资源），
            // 点击监听不会挂载 → 按钮无反应。
            var bindComp = GetComponent<QuickBind>();
            WriteProbe("[roomform] OnOpen: m_BtnReady=" + (m_BtnReady != null ? "OK" : "NULL")
                + " m_BtnLeave=" + (m_BtnLeave != null ? "OK" : "NULL")
                + " quickBind=" + (bindComp != null ? "YES" : "NO")
                + " bindCount=" + (bindComp != null ? bindComp.Bindings.Count.ToString() : "-"));

            if (m_TxtTitle != null)
            {
                m_TxtTitle.text = "联机房间";
            }

            if (m_BtnReady != null)
            {
                m_BtnReady.onClick.RemoveAllListeners();
                m_BtnReady.onClick.AddListener(OnReadyClick);
            }

            if (m_BtnLeave != null)
            {
                m_BtnLeave.onClick.RemoveAllListeners();
                m_BtnLeave.onClick.AddListener(OnLeaveClick);
            }

            if (m_BtnChangeChar != null)
            {
                m_BtnChangeChar.onClick.RemoveAllListeners();
                m_BtnChangeChar.onClick.AddListener(OnChangeCharClick);
            }

            // 准备阶段列表容器就绪（把单 Text 节点仅当容器；保留用户手动布局矩形区）
            PreparePlayerList();
            RefreshPlayerList(LastPlayerList);

            // 请求加载 PlayerCell 预制体（items 目录，'game' AssetBundle 全路径）。
            // 首次进房为异步：加载完成后回调里会再刷一次列表，行立即可见。
            EnsureCellPrefab();

            // 准备阶段房间 UI = "角色选择面板(Dock) + 房间准备条(RoomForm)" 同屏共存互不干扰：
            // Dock(全屏滑入选角) 放 Popup 组、RoomForm(右侧悬浮条) 放 Top(depth100) 组——
            // 异组互不 Pause，RoomForm Canvas 排序最高浮于 Dock 之上。
            // 因此进房间**默认展开** Dock（懒创建 → OpenUIForm → Dock 自身 ShowImmediate）。
            // 历史：曾 ToggleCharDock(true) 全屏展开 + 同组 → 房间条被 Dock 遮挡（用户反馈"看不到"）；
            //       已通过 异组 + Top 悬浮 修复，收起兜底不再需要。
            if (!IsCharDockOpen)
            {
                ToggleCharDock(true);   // 进入房间即展开全屏角色面板，与房间条共存
            }

            // Host：进房时玩家列表广播发生在 RoomForm 打开前（订阅未生效会被漏掉），
            // 打开后再补发一次 → 本机玩家行立即可见（客户端收 S2CPlayerList 本身就会刷）。
            RefreshInitialPlayerListFromHost();

            WriteProbe("[roomform] OnOpen: 房间条(Top) 与 角色面板(Popup) 共存；Dock 默认展开 IsCharDockOpen=" + IsCharDockOpen);
        }

        /// <summary>Host 主动补发当前玩家列表（RoomForm 打开后调用，让本机列表立即显示已有玩家行）。</summary>
        private void RefreshInitialPlayerListFromHost()
        {
            if (GameEntry.NetworkService == null || GameEntry.NetworkService.Mode != Network.NetMode.Host)
            {
                return;
            }
            var hostLogic = GameEntry.Instance != null
                ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                : null;
            if (hostLogic != null)
            {
                hostLogic.BroadcastPlayerListNow();
            }
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            RoomEvents.OnPlayerListUpdated -= OnPlayerListUpdated;
            RoomEvents.OnLocalReadyChanged -= OnLocalReadyChanged;
            CharacterDockEvents.OnDockOpened -= OnCharDockOpened;
            CharacterDockEvents.OnCharacterChanged -= OnDockCharChanged;

            // 关闭房间时一并收起/关闭角色抽屉。
            // 必须走 UI 框架 CloseUIForm（角色抽屉经 OpenUIForm 打开、注册在 UIManager 中）——
            // 直接 Destroy(gameObject) 会留下已销毁引用，下一帧 UIManager.Update 访问 → NRE。
            CloseCharDockForm();

            // 关闭房间时整池释放玩家列表 Cell（GameFramework 对象池）
            CleanupPlayerList();

            base.OnClose(isShutdown, userData);
        }

        /// <summary>经 UI 框架关闭角色抽屉窗体（幂等：未打开/已关闭时兜底清理实例）。</summary>
        private void CloseCharDockForm()
        {
            if (GameEntry.UI == null)
            {
                return;
            }
            bool closed = false;
            var groups = GameEntry.UI.GetAllUIGroups();
            foreach (var group in groups)
            {
                foreach (var form in group.GetAllUIForms())
                {
                    var uiForm = form as UnityGameFramework.Runtime.UIForm;
                    if (uiForm != null && uiForm.Logic != null &&
                        uiForm.Logic.Name == "CharacterDockForm(Clone)")
                    {
                        GameEntry.UI.CloseUIForm(uiForm);
                        closed = true;
                    }
                }
            }
            // 兜底：窗体已关闭但引用残留（防御），直接清实例
            if (!closed && m_CharDock != null && m_CharDock.gameObject != null)
            {
                UnityEngine.Object.Destroy(m_CharDock.gameObject);
            }
            m_CharDock = null;
        }

        private void OnReadyClick()
        {
            RoomFormEvents.RequestReady();
        }

        private void OnLeaveClick()
        {
            RoomFormEvents.RequestLeave();
        }

        /// <summary>切换角色按钮：展开/收起角色抽屉（DOTween 滑入/滑出）。</summary>
        private void OnChangeCharClick()
        {
            ToggleCharDock(!IsCharDockOpen);
        }

        /// <summary>展开/收起角色抽屉（懒创建，挂到本窗体下保持共存）。</summary>
        public void ToggleCharDock(bool show)
        {
            IsCharDockOpen = show;

            if (show)
            {
                if (m_CharDock == null)
                {
                    m_CharDock = CreateCharDock();
                }
                if (m_CharDock != null)
                {
                    m_CharDock.gameObject.SetActive(true);
                    m_CharDock.TogglePanel(true, true);
                }
            }
            else
            {
                if (m_CharDock != null)
                {
                    m_CharDock.TogglePanel(false, true);
                }
            }
        }

        /// <summary>
        /// 创建角色抽屉：经 UI 框架加载 CharacterDockForm.prefab。
        /// 注意：必须放入独立 UI 组（Popup），不能与 RoomForm 同属 Default——
        /// GameFramework 同一 UI 组内仅最顶层窗体可见（后开的会把先开的 Pause/SetActive(false)），
        /// 若 Dock 与房间条同组，Dock 一打开房间准备 UI 就消失（历史 bug："房间准备 UI 看不到"）。
        /// </summary>
        private CharacterDockForm CreateCharDock()
        {
            if (GameEntry.UI != null)
            {
                if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Popup))
                {
                    GameEntry.UI.AddUIGroup(Constant.UIGroup.Popup, Constant.UIGroup.DepthPopup);
                }
                GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.CharacterDockForm, Constant.UIGroup.Popup, this);
            }
            return null;   // 实例在打开完成后经事件绑定
        }

        /// <summary>角色抽屉打开完成回调（DockOpened 事件）。</summary>
        private void OnCharDockOpened(CharacterDockForm dock)
        {
            if (dock == null)
            {
                return;
            }
            m_CharDock = dock;
            dock.RefreshCurrentLabel(Procedure.ProcedureBattle.SelectedCharacterId);
            dock.TogglePanel(true, true);
            IsCharDockOpen = true;
        }

        /// <summary>
        /// 角色面板内切换角色：更新面板标签，并**立即**把本机玩家行的 Cell Icon 同步为新角色
        /// （不等网络广播回包——Host 也会广播，最终两端一致）。
        /// </summary>
        private void OnDockCharChanged(int characterId)
        {
            if (m_CharDock != null)
            {
                m_CharDock.RefreshCurrentLabel(characterId);
            }

            SyncLocalRowIcon(characterId);

            // 注意：不自动收起——用户可在面板停留查看/继续换（收起由顶部按钮/Tab 控制）
        }

        /// <summary>把本机玩家所在行的 Cell 图标立即更新为指定角色。</summary>
        private void SyncLocalRowIcon(int characterId)
        {
            if (characterId <= 0 || string.IsNullOrEmpty(LocalName))
            {
                return;
            }
            var rows = ParsePlayerRows(LastPlayerList);
            int count = Math.Min(rows.Count, m_PlayerRows.Count);
            for (int i = 0; i < count; i++)
            {
                if (!rows[i].IsMe)
                {
                    continue;
                }
                var rowGo = m_PlayerRows[i];
                if (rowGo == null)
                {
                    break;
                }
                var cell = rowGo.GetComponent<PlayerCell>();
                if (cell != null)
                {
                    cell.SetContent(GetCharacterSprite(characterId), rows[i].Name, rows[i].Ready, true);
                    WriteProbe(string.Format("[roomlist] local icon sync row={0} -> char={1} icon={2}",
                        i, characterId, cell.CurrentIconCode));
                }
                break;
            }
        }

        /// <summary>关闭角色抽屉（供抽屉自身"收起"按钮回调）。</summary>
        public void CloseCharDock()
        {
            ToggleCharDock(false);
        }

        private void OnPlayerListUpdated(string players)
        {
            LastPlayerList = players;
            RefreshPlayerList(players);
        }

        /// <summary>
        /// 玩家列表（遵循 UI 列表规范）：
        ///   容器 = txt_PlayerList 的矩形区（Text 永久禁用）；每行 = PlayerCell.prefab 子物体。
        ///   刷新 = **先清空现有全部 Cell（回收进池并移出容器）→ 再按数据顺序逐个生成**，
        ///   保证列表节点下无残留（无旧行/多余行/隐藏残留子物体）。
        /// </summary>
        private void RefreshPlayerList(string players)
        {
            if (m_ListContainer == null)
            {
                PreparePlayerList();   // 内部会把原 txt_PlayerList 文本节点转成纯容器
                if (m_ListContainer == null)
                {
                    return;
                }
            }

            var rows = ParsePlayerRows(players);

            // 1) 清空：回收全部活动行（进池 → 隐藏挂点），容器回到空
            ClearAllPlayerRows();

            // 2) 重建：按数据逐行获取 Cell 子物体（池复用或新建）并填充/定位
            var pool = GetCellPool();
            var prefab = GetCellPrefab();
            int count = rows.Count;
            for (int i = 0; i < count; i++)
            {
                var info = rows[i];
                GameObject go = null;
                if (pool != null && prefab != null)
                {
                    go = pool.Acquire(() => Instantiate(prefab), m_ListContainer);
                }
                if (go == null && prefab != null)
                {
                    go = Instantiate(prefab, m_ListContainer);
                    go.SetActive(true);
                }
                if (go == null)
                {
                    // 预制体未加载完成：静默等待，加载回调/下次列表事件会重刷
                    break;
                }
                go.name = "PlayerCell_" + i;
                m_PlayerRows.Add(go);
                var cell = go.GetComponent<PlayerCell>();
                if (cell != null)
                {
                    cell.SetContent(GetCharacterSprite(info.CharId), info.Name, info.Ready, info.IsMe);
                }
                PositionRow(go, i, count);
            }

            int containerChildren = m_ListContainer != null ? m_ListContainer.childCount : -1;
            WriteProbe(string.Format("[roomlist] RefreshPlayerList rows={0} containerChildren={1} poolTotal={2} poolActive={3}",
                count, containerChildren,
                m_CellPool != null ? m_CellPool.TotalCount : 0,
                m_CellPool != null ? m_CellPool.ActiveCount : 0));
            if (m_PlayerRows.Count > 0)
            {
                var sample = m_PlayerRows[0].GetComponent<PlayerCell>();
                if (sample != null)
                {
                    WriteProbe(string.Format("[roomlist] row0 sprite={0}", sample.CurrentIconSpriteName));
                }
            }
        }

        /// <summary>清空：把全部活动行回收进池（空闲项移至隐藏挂点），列表容器归零、无残留。</summary>
        private void ClearAllPlayerRows()
        {
            var pool = GetCellPool();
            foreach (var go in m_PlayerRows)
            {
                if (go == null)
                {
                    continue;
                }
                if (pool != null)
                {
                    pool.Recycle(go);
                }
                else
                {
                    Destroy(go);
                }
            }
            m_PlayerRows.Clear();

            // 兜底：容器内其余任何子物体（预制体里预留的模板、历史残留等）一律销毁 → 容器 100% 清空
            if (m_ListContainer != null)
            {
                for (int i = m_ListContainer.childCount - 1; i >= 0; i--)
                {
                    var child = m_ListContainer.GetChild(i);
                    if (child != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }

        private struct PlayerRowInfo
        {
            public string Name;
            public bool Ready;
            public int CharId;
            public bool IsMe;
        }

        private List<PlayerRowInfo> ParsePlayerRows(string players)
        {
            var rows = new List<PlayerRowInfo>();
            if (string.IsNullOrEmpty(players))
            {
                return rows;
            }
            string[] entries = players.Split(';');
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }
                string[] kv = entry.Split(':');
                var row = new PlayerRowInfo();
                row.Name = kv.Length > 0 ? kv[0] : "玩家";
                row.Ready = kv.Length > 1 && kv[1] == "1";
                row.CharId = 0;
                if (kv.Length > 2)
                {
                    int.TryParse(kv[2], out row.CharId);
                }
                row.IsMe = !string.IsNullOrEmpty(LocalName) && row.Name == LocalName;
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// 取/建列表容器（规范：列表区域 = 纯容器，绝无"单文本残留"）：
        /// 保留原 txt_PlayerList 节点的矩形（作者手动布局的参考区），但把该节点改名并
        /// 清空/销毁其 Text 组件 —— Hierarchy 里不再存在 txt_ 文本节点，只剩容器 + Cell 子物体。
        /// 幂等：已转换过的实例（节点名=PlayerListContainer）直接复用，Text 已销毁不再触碰。
        /// </summary>
        private void PreparePlayerList()
        {
            if (m_ListContainer != null)
            {
                return;
            }

            var find = transform.Find(PlayerListContainerName) as RectTransform;
            RectTransform rt = find;
            Text textComp = null;
            if (rt == null && m_TxtPlayerList != null)
            {
                rt = m_TxtPlayerList.rectTransform;
                textComp = m_TxtPlayerList;
            }
            if (rt == null)
            {
                return;
            }

            rt.name = PlayerListContainerName;
            if (textComp == null)
            {
                textComp = rt.GetComponent<Text>();
            }
            if (textComp != null)
            {
                textComp.text = string.Empty;
                textComp.enabled = false;
                Destroy(textComp);   // 运行时销毁文本组件 → 列表节点不再渲染/不再算"文本残留"
            }

            // 清理 Text 销毁后遗留的孤儿 CanvasRenderer（无 Graphic 时不渲染，但层级不干净）
            var orphanRenderer = rt.GetComponent<CanvasRenderer>();
            if (orphanRenderer != null && rt.GetComponent<Graphic>() == null)
            {
                Destroy(orphanRenderer);
            }

            // 布局组：若容器上挂了 VerticalLayoutGroup，让它"尊重子物体自身尺寸"——
            // 关掉 Control/Force Expand，纵向排布交给布局组，代码不再覆盖子物体尺寸/位置
            m_RowLayout = rt.GetComponent<VerticalLayoutGroup>();
            if (m_RowLayout != null)
            {
                m_RowLayout.childControlWidth = false;
                m_RowLayout.childControlHeight = false;
                m_RowLayout.childForceExpandWidth = false;
                m_RowLayout.childForceExpandHeight = false;
                if (m_RowLayout.spacing <= 0f)
                {
                    m_RowLayout.spacing = PlayerRowGap;
                }
                WriteProbe("[roomform] PlayerListContainer 使用布局组（尊重子物体尺寸，关 Control/ForceExpand）");
            }
            m_ListContainer = rt;
        }

        private GfUiItemPool GetCellPool()
        {
            if (m_CellPool == null)
            {
                m_CellPool = GfUiItemPool.Create(PlayerCellPoolName);
            }
            return m_CellPool;
        }

        /// <summary>取已加载的 PlayerCell 预制体（未加载完成返回 null，由 EnsureCellPrefab 回调驱动重建）。</summary>
        private GameObject GetCellPrefab()
        {
            return m_CellPrefab;
        }

        /// <summary>确保 PlayerCell 预制体已按全路径加载（items 目录）；异步完成后重刷一次列表。</summary>
        private void EnsureCellPrefab()
        {
            if (m_CellPrefab == null)
            {
                m_CellPrefab = UiPrefab.GetCached(Constant.UIItemAssetPath.PlayerCell);
            }
            if (m_CellPrefab != null)
            {
                return;
            }
            UiPrefab.Load(Constant.UIItemAssetPath.PlayerCell, prefab =>
            {
                if (prefab == null)
                {
                    WriteProbe("[roomform] PlayerCell prefab 加载失败: " + Constant.UIItemAssetPath.PlayerCell);
                    return;
                }
                m_CellPrefab = prefab;
                WriteProbe("[roomform] PlayerCell prefab 已加载 (items)");
                RefreshPlayerList(LastPlayerList);
            });
        }

        /// <summary>
        /// 行定位：容器挂了 VerticalLayoutGroup 时**不做任何手工定位/改尺寸**——
        /// 由布局组排布并尊重子物体自身（预制体）尺寸；否则按容器矩形手工居中排布。
        /// </summary>
        private void PositionRow(GameObject rowGo, int index, int count)
        {
            if (m_ListContainer == null || rowGo == null)
            {
                return;
            }
            var rt = rowGo.transform as RectTransform;
            if (rt == null)
            {
                return;
            }

            if (m_RowLayout != null)
            {
                // 布局组接管：保留子物体尺寸设置（不覆盖 sizeDelta / anchoredPosition）
                return;
            }

            var area = m_ListContainer.rect;
            float rowW = Mathf.Max(40f, area.width - 4f);
            float availH = Mathf.Max(10f, area.height - 8f);
            float rowH = count > 0
                ? Mathf.Clamp((availH - (count - 1) * PlayerRowGap) / count, PlayerRowMinH, PlayerRowMaxH)
                : 0f;
            float totalH = count > 0 ? count * rowH + (count - 1) * PlayerRowGap : 0f;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(rowW, rowH);
            float topY = totalH * 0.5f - rowH * 0.5f;
            rt.anchoredPosition = new Vector2(0f, topY - index * (rowH + PlayerRowGap));
        }

        /// <summary>整池释放并清空行引用（RoomForm 关闭时）。</summary>
        private void CleanupPlayerList()
        {
            if (m_CellPool != null)
            {
                m_CellPool.Destroy();
                m_CellPool = null;
            }
            m_PlayerRows.Clear();
            m_CellPrefab = null;
            m_RowLayout = null;
            m_ListContainer = null;
        }

        /// <summary>角色 ID → CharacterSO.IconSprite（资产引用）。</summary>
        private static Sprite GetCharacterSprite(int characterId)
        {
            var character = characterId > 0 && GameEntry.Data != null
                ? GameEntry.Data.GetCharacter(characterId)
                : null;
            return character != null ? character.IconSprite : null;
        }

        /// <summary>本机准备状态变化（点准备/取消后按钮文案与列表刷新）。</summary>
        private void OnLocalReadyChanged(bool ready)
        {
            SetLocalReadyState(ready);
            RefreshPlayerList(LastPlayerList);
        }

        /// <summary>更新本机准备按钮文案（流程/事件调用）。</summary>
        public void SetLocalReadyState(bool ready)
        {
            m_LocalReady = ready;
            if (m_BtnReady != null)
            {
                var label = m_BtnReady.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = ready ? "取消准备" : "准备";
                }
            }
        }

        /// <summary>显示状态提示（如"即将开始..."）。</summary>
        public void ShowStatus(string message)
        {
            if (m_TxtStatus != null)
            {
                m_TxtStatus.text = message;
            }
        }
    }
}
