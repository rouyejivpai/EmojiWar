//------------------------------------------------------------
// EmojiWar GameMain - 自动化联调辅助（构建版）
// 启动参数含 -autocreate 时自动执行：开始游戏 → 创建房间 → 进入战斗，
// 并把战斗实体/美术加载结果写入 Builds/Logs/runtime_probe.txt。
// 用途：沙箱无法操作 exe GUI，通过该参数验证构建版完整战斗流程。
//------------------------------------------------------------

using System.Collections;
using UnityEngine;

namespace EmojiWar.GameMain
{
    /// <summary>
    /// 自动化流程辅助（仅 -autocreate/-autojoin 启动参数时激活）。
    /// 默认只自动走到"进入房间"；**不自动点准备**（避免单人房间 30 秒后自动开战，
    /// 以及"意外进入战斗"的观感）。仅当额外传 -autoready 参数时才自动准备/自动开战
    /// （供双实例一致性等需要全自动验证的场景）。
    /// </summary>
    public class AutoPlay : MonoBehaviour
    {
        private static bool s_Started = false;
        private static bool s_IsJoiner = false;
        private static bool s_AutoReady = false;
        private static bool s_IsSettingsTest = false;
        private static bool s_AutoCardClick = false;   // -autocard：进房间后自动模拟点角色卡片（复现 Client 点卡 bug）
        private static bool s_AutoFire = false;        // -autofire：Host 自动按住左键（验证无限释放/连发）
        private static bool s_AutoBag = false;         // -autobag：战斗开始后自动开关背包（Tab）验证
        private static bool s_AutoDrag = false;        // -autodrag：背包拖拽集成探针（合成指针事件）
        private static bool s_AutoShop = false;        // -autoshop：波间商店回归探针（不得自动开波）
        private static bool s_AutoItems = false;       // -autoitems：启动即跑物品事务自检（P2）并写探针
        private static bool s_ItemsTestOnly = false;   // -autoitems 单独使用：只跑自检，不跑完整自动流程
        private static bool s_AutoSpell = false;       // -autospell：施法自检（S2/D23）+ 序列/预算打印并写探针
        private static bool s_AutoBuff = false;        // -autobuff：临时 Buff 自检（S3/D24）并写探针
        private static bool s_AutoPassive = false;     // -autopassive：被动触发自检（S4/D25）并写探针
        private static bool s_IsMenuFlowTest = false;  // -automenu：主界面四按钮 → 多人游戏 → 创建（验证新流程）
        private static bool s_MenuFlowJoin = false;    // -automenujoin：多人页选"加入游戏"→房间列表（局域网发现）

        /// <summary>AutoPlay 是否激活（流程据此决定是否保留自动移动，用于回环测试）。</summary>
        public static bool IsActive
        {
            get { return s_Started; }
        }

        /// <summary>是否自动准备/自动开战（-autoready 参数开启）。</summary>
        public static bool AutoReady
        {
            get { return s_AutoReady; }
        }

        private static int s_AutoReadyPlayers = 0;             // -autoreadyplayers N：等到 N 人再准备
        private static float s_AutoReadyWaitSeconds = 0f;      // -autoreadywait S：最长等 S 秒

        /// <summary>-autoreadyplayers 设定的期望人数（0 = 未设定）。</summary>
        public static int AutoReadyPlayers { get { return s_AutoReadyPlayers; } }

        /// <summary>-autoreadywait 设定的最长等待秒数（0 = 不等待）。</summary>
        public static float AutoReadyWaitSeconds { get { return s_AutoReadyWaitSeconds; } }

        public static void TryStart()
        {
            if (s_Started)
            {
                return;
            }

            var args = System.Environment.GetCommandLineArgs();

            // 带值参数：-autoreadywait <秒> / -autoreadyplayers <N>
            // （其余开关都是 arg == "x" 精确匹配，带值参数需按下标取下一个）
            for (int i = 0; i < args.Length - 1; i++)
            {
                float w;
                if (args[i] == "-autoreadywait" && float.TryParse(args[i + 1], out w))
                {
                    s_AutoReadyWaitSeconds = Mathf.Max(0f, w);
                    Debug.Log("[AutoPlay] -autoreadywait " + s_AutoReadyWaitSeconds + " 秒");
                }
                int p;
                if (args[i] == "-autoreadyplayers" && int.TryParse(args[i + 1], out p))
                {
                    s_AutoReadyPlayers = Mathf.Max(0, p);
                    Debug.Log("[AutoPlay] -autoreadyplayers " + s_AutoReadyPlayers);
                }
            }

            // 第一遍：前置扫描独立开关（-autocard / -autosettings / -autofire），
            // 避免被 -autocreate/-autojoin 分支的 break 跳过（args 顺序任意）。
            foreach (var arg in args)
            {
                if (arg == "-autocard")
                {
                    s_AutoCardClick = true;
                    Debug.Log("[AutoPlay] -autocard 参数检测到，进房间后自动模拟点角色卡片");
                }
                if (arg == "-autofire")
                {
                    s_AutoFire = true;
                    Debug.Log("[AutoPlay] -autofire 参数检测到，Host 自动开火（验证无限释放）");
                }
                if (arg == "-autobag")
                {
                    s_AutoBag = true;
                    Debug.Log("[AutoPlay] -autobag 参数检测到，战斗开始后自动开关背包验证");
                }
                if (arg == "-autodrag")
                {
                    s_AutoDrag = true;
                    s_AutoBag = true;   // 拖拽探针需要背包处于打开状态
                    Debug.Log("[AutoPlay] -autodrag 参数检测到，将执行背包拖拽集成探针");
                }
                if (arg == "-autoshop")
                {
                    s_AutoShop = true;
                    Debug.Log("[AutoPlay] -autoshop 参数检测到，将执行波间商店回归探针");
                }
                if (arg == "-autoitems")
                {
                    s_AutoItems = true;
                    Debug.Log("[AutoPlay] -autoitems 参数检测到，启动后将跑物品事务自检");
                }
                if (arg == "-autospell")
                {
                    s_AutoSpell = true;
                    Debug.Log("[AutoPlay] -autospell 参数检测到，启动后将跑施法自检（S2/D23）");
                }
                if (arg == "-autobuff")
                {
                    s_AutoBuff = true;
                    Debug.Log("[AutoPlay] -autobuff 参数检测到，启动后将跑临时 Buff 自检（S3/D24）");
                }
                if (arg == "-autopassive")
                {
                    s_AutoPassive = true;
                    Debug.Log("[AutoPlay] -autopassive 参数检测到，启动后将跑被动触发自检（S4/D25）");
                }
                if (arg == "-autosettings")
                {
                    s_Started = true;
                    s_IsSettingsTest = true;
                    var go = new GameObject("AutoPlay");
                    go.AddComponent<AutoPlay>();
                    Debug.Log("[AutoPlay] -autosettings 参数检测到，启动设置页自动化验证");
                    return;
                }
                if (arg == "-automenu")
                {
                    s_Started = true;
                    s_IsMenuFlowTest = true;
                    var go = new GameObject("AutoPlay");
                    go.AddComponent<AutoPlay>();
                    Debug.Log("[AutoPlay] -automenu 参数检测到，验证主菜单→多人游戏新流程");
                    return;
                }
                if (arg == "-automenujoin")
                {
                    s_Started = true;
                    s_IsMenuFlowTest = true;
                    s_MenuFlowJoin = true;
                    var go = new GameObject("AutoPlay");
                    go.AddComponent<AutoPlay>();
                    Debug.Log("[AutoPlay] -automenujoin 参数检测到，验证多人→加入游戏→房间列表");
                    return;
                }
            }

            // 第二遍：主流程开关
            foreach (var arg in args)
            {
                if (arg == "-autocreate" || arg == "-autojoin")
                {
                    s_Started = true;
                    s_IsJoiner = arg == "-autojoin";
                    var go = new GameObject("AutoPlay");
                    go.AddComponent<AutoPlay>();
                    Debug.Log("[AutoPlay] " + arg + " 参数检测到，启动自动流程");
                    break;
                }
            }

            // 自动准备为显式开关：默认关闭（不自动点准备/不开战），传 -autoready 才启用
            if (s_Started)
            {
                foreach (var arg in args)
                {
                    if (arg == "-autoready")
                    {
                        s_AutoReady = true;
                        Debug.Log("[AutoPlay] -autoready 参数检测到，启用自动准备/自动开战");
                        break;
                    }
                }
            }

            // -autocard 复现：需要与 -autojoin/-autocreate 同用，进入房间后自动点角色卡片
            if (s_AutoCardClick && !s_Started)
            {
                // 若未同时给 -autojoin/-autocreate，默认按加入者处理（复现"非主机"场景）
                s_Started = true;
                s_IsJoiner = true;
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autocard 单独使用：按加入者流程进入房间后自动点卡片");
            }

            // -autoitems 单独使用：只跑物品事务自检（P2 验收通道）
            if (s_AutoItems && !s_Started)
            {
                s_ItemsTestOnly = true;
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autoitems 单独使用：仅跑物品事务自检");
            }

            // -autospell 单独使用：只跑施法自检（S2/D23 验收通道，不跑完整流程）
            if (s_AutoSpell && !s_Started)
            {
                s_ItemsTestOnly = true;   // 复用"只跑自检"的启动路径（Start 里按 s_AutoSpell 分流）
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autospell 单独使用：仅跑施法自检（S2/D23）");
            }

            // -autobuff 单独使用：只跑临时 Buff 自检（S3/D24 验收通道，不跑完整流程）
            if (s_AutoBuff && !s_Started)
            {
                s_ItemsTestOnly = true;   // 复用"只跑自检"的启动路径（Start 里按 s_AutoBuff 分流）
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autobuff 单独使用：仅跑临时 Buff 自检（S3/D24）");
            }
            // -autopassive 单独使用：只跑被动触发自检（S4/D25 验收通道，不跑完整流程）
            if (s_AutoPassive && !s_Started)
            {
                s_ItemsTestOnly = true;   // 复用"只跑自检"的启动路径（Start 里按 s_AutoPassive 分流）
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autopassive 单独使用：仅跑被动触发自检（S4/D25）");
            }

            // -autodrag 单独使用：按 Host 全自动流程跑到战斗，再执行拖拽集成探针
            if (s_AutoDrag && !s_Started)
            {
                s_Started = true;
                s_AutoReady = true;
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autodrag 单独使用：自动跑到战斗并执行拖拽探针");
            }

            // -autoshop 单独使用：跑到战斗后执行"商店阶段不得自动开波"回归
            if (s_AutoShop && !s_Started)
            {
                s_Started = true;
                s_AutoReady = true;
                var go = new GameObject("AutoPlay");
                go.AddComponent<AutoPlay>();
                Debug.Log("[AutoPlay] -autoshop 单独使用：自动跑到战斗并执行商店回归探针");
            }
        }

        /// <summary>
        /// 波间商店回归探针（修复验证）：清空本波敌人 → 商店打开 → 观察 10 秒，
        /// **商店期间不得自动开下一波**（旧实现 ShopDuration 计时到就 StartWave）；
        /// 然后触发"继续" → 下一波必须开始。
        /// </summary>
        private IEnumerator AutoShopProbe()
        {
            var hostLogic = GameEntry.Instance != null
                ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                : null;
            var sim = hostLogic != null ? hostLogic.Simulation : (GameEntry.SimView != null ? GameEntry.SimView.Simulation : null);
            if (sim == null)
            {
                WriteProbe("[shoptest] FAIL 找不到确定性模拟实例");
                yield break;
            }

            // 等波次真正开始
            float waitStart = Time.unscaledTime;
            while (!sim.WaveStarted && Time.unscaledTime - waitStart < 20f) { yield return null; }
            int wave0 = sim.WaveIndex;
            WriteProbe("[shoptest] 战斗已开始 wave=" + wave0);

            // 清空本波 → 进入商店（持续清 5s：敌人是分批生成的，单次清场会漏掉尚未生成的）
            float killUntil = Time.unscaledTime + 5f;
            while (Time.unscaledTime < killUntil) { sim.DebugKillAllEnemies(); yield return null; }
            waitStart = Time.unscaledTime;
            while (!sim.ShopOpen && Time.unscaledTime - waitStart < 15f)
            {
                sim.DebugKillAllEnemies();
                yield return null;
            }
            WriteProbe(string.Format("[shoptest] {0} 商店已打开 wave={1} shopOpen={2}",
                sim.ShopOpen ? "PASS" : "FAIL", sim.WaveIndex, sim.ShopOpen));
            if (!sim.ShopOpen) { yield break; }

            // 商店期间静置 10s（> 旧实现 ShopDuration=8s）：波次与敌人数都不许变
            yield return new WaitForSeconds(10f);
            bool stillShop = sim.ShopOpen;
            bool sameWave = sim.WaveIndex == wave0;
            WriteProbe(string.Format("[shoptest] {0} 商店期间未自动开波 wave={1}(期望 {2}) shopOpen={3}",
                stillShop && sameWave ? "PASS" : "FAIL", sim.WaveIndex, wave0, stillShop));

            // 玩家点"继续" → 必须开始下一波
            // ★ 只有 Host 能推进波次（Host 权威 + 广播）。非 Host 以前直接调 sim.RequestNextWave()，
            //   那是"绕过房主改本地模拟"，本身就是脚手架制造的不同步（2026-09-28 发现）。
            if (hostLogic != null)
            {
                hostLogic.RequestShopContinue();
            }
            else
            {
                var shopClientLogic = Object.FindObjectOfType<Network.NetClientLogic>();
                if (shopClientLogic != null)
                {
                    shopClientLogic.RequestShopContinue();   // 走网络请求，由 Host 权威推进并广播
                }
                else
                {
                    WriteProbe("[shoptest] SKIP 非 Host 且无 NetClientLogic，不本地推进波次");
                    yield break;
                }
            }
            yield return new WaitForSeconds(1.5f);
            WriteProbe(string.Format("[shoptest] {0} 继续后开波 wave={1}(期望 {2}) shopOpen={3}",
                sim.WaveIndex == wave0 + 1 && !sim.ShopOpen ? "PASS" : "FAIL", sim.WaveIndex, wave0 + 1, sim.ShopOpen));
            WriteProbe("[shoptest] done");
        }

        /// <summary>
        /// 拖拽集成探针（P4b）：用真实组件（UiSlotView/UiDragManager/InventoryService）走一遍
        /// "背包法术卡 → 手部法杖法术槽" 的拖拽，断言事务结果、幽灵生命周期与高亮复位。
        /// 指针事件由合成 PointerEventData 构造，落点用插槽中心屏幕坐标（未命中射线时走最近目标兜底）。
        /// </summary>
        private IEnumerator AutoDragProbe()
        {
            // Host/联机流程不跑 BattleManager，这里补发初始装备（幂等：手上已有杖则不重发）。
            // P5 会把"初始装备"移到 Host 权威的 loadout 下发里。
            ItemSystem.GrantStartingLoadout();

            var form = UI.BackpackForm.FindOpenInstance();
            if (form == null)
            {
                UI.BackpackHotkey.Toggle();      // 没开就打开
                yield return new WaitForSeconds(2f);
                form = UI.BackpackForm.FindOpenInstance();
            }
            if (form == null)
            {
                WriteProbe("[dragtest] FAIL 找不到已展开的背包实例");
                yield break;
            }
            form.TogglePanel(true, false);        // 重新绑定容器（拿刚发放的法杖法术槽）
            yield return new WaitForSeconds(0.5f);

            var grid = form.ItemGridContainer;
            var wandSpells = form.LeftSpellsContainer;   // 左手法杖槽下的法术槽子列表
            var service = ItemSystem.Service;
            var handLeft = service != null ? service.GetContainer(ItemSystem.HandLeftId) : null;
            WriteProbe(string.Format("[dragtest] form grid={0} wandSpells={1} handL={2} backpack={3}",
                grid != null ? grid.CellCount : -1,
                wandSpells != null ? wandSpells.CellCount : -1,
                handLeft != null ? handLeft.Dump() : "null",
                service != null && service.GetContainer(ItemSystem.BackpackId) != null ? service.GetContainer(ItemSystem.BackpackId).Dump() : "null"));
            if (grid == null || wandSpells == null || grid.CellCount == 0 || wandSpells.CellCount == 0)
            {
                WriteProbe("[dragtest] FAIL 容器未绑定（Cell 数为 0）");
                yield break;
            }

            var wandContainer = wandSpells.Container;
            if (service == null || wandContainer == null)
            {
                WriteProbe("[dragtest] FAIL 物品服务/法杖槽容器为空（手上没有法杖？）");
                yield break;
            }

            // 找一个装着法术卡的背包格
            int fromIndex = -1;
            for (int i = 0; i < grid.CellCount; i++)
            {
                var cell = grid.GetCell(i);
                if (cell == null || cell.Item.IsEmpty) { continue; }
                var row = service.Table.GetItem(cell.Item.ItemId);
                if (row != null && row.Category == Data.ItemCategory.Spell) { fromIndex = i; break; }
            }
            // 找一个空的法杖槽
            int toIndex = -1;
            for (int i = 0; i < wandContainer.Capacity; i++)
            {
                if (wandContainer.Get(i).IsEmpty) { toIndex = i; break; }
            }
            if (fromIndex < 0 || toIndex < 0)
            {
                WriteProbe(string.Format("[dragtest] FAIL 前置条件不足 fromIndex={0} toIndex={1}（格子={2} 法杖槽={3}）",
                    fromIndex, toIndex, grid.CellCount, wandContainer.Dump()));
                yield break;
            }

            var sourceCell = grid.GetCell(fromIndex);
            var targetCell = wandSpells.GetCell(toIndex);
            int movedItemId = sourceCell.Item.ItemId;
            int srcCountBefore = sourceCell.Item.Count;
            string wandDumpBefore = wandContainer.Dump();

            var manager = UI.UiDragManager.Instance;
            if (manager == null)
            {
                WriteProbe("[dragtest] FAIL UiDragManager 实例不存在");
                yield break;
            }

            // 合成指针：从来源格中心拖到目标格中心
            var es = UnityEngine.EventSystems.EventSystem.current;
            var pointer = new UnityEngine.EventSystems.PointerEventData(es);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, sourceCell.transform.position);
            bool begun = manager.Begin(sourceCell, pointer, null, new Vector2(120f, 120f), sourceCell.transform as RectTransform);
            WriteProbe(string.Format("[dragtest] begin={0} from=Backpack[{1}] item#{2} x{3} → wand[{4}]",
                begun, fromIndex, movedItemId, srcCountBefore, toIndex));
            if (!begun) { yield break; }

            // 拖拽偏移断言：指针按在来源格中心时，幽灵必须贴在来源格上（锚点/抓取点错位会让它偏出半个屏幕）
            var ghostObj = GameObject.Find("UiDragGhost");
            float ghostOffset = -1f;
            if (ghostObj != null)
            {
                Vector3 ghostScreen = RectTransformUtility.WorldToScreenPoint(null, ghostObj.transform.position);
                ghostOffset = Vector2.Distance(ghostScreen, pointer.position);
            }
            WriteProbe(string.Format("[dragtest] {0} 拖拽幽灵贴合来源格 offset={1:F1}px",
                ghostOffset >= 0f && ghostOffset <= 8f ? "PASS" : "FAIL", ghostOffset));

            yield return null;

            pointer.position = RectTransformUtility.WorldToScreenPoint(null, targetCell.transform.position);
            manager.UpdatePointer(pointer);
            bool ghostAlive = GameObject.Find("UiDragGhost") != null;
            var current = manager.CurrentTarget;
            WriteProbe(string.Format("[dragtest] drag ghost={0} target={1}",
                ghostAlive, current != null ? current.Slot.ToString() : "null"));

            manager.End(pointer);
            yield return new WaitForSeconds(0.4f);   // 等吸附动画结束（0.1s）

            var after = wandContainer.Get(toIndex);
            bool okMove = after.ItemId == movedItemId;
            bool ghostGone = GameObject.Find("UiDragGhost") == null;
            bool srcDecreased = grid.GetCell(fromIndex) == null || grid.GetCell(fromIndex).Item.Count == srcCountBefore - 1
                || grid.GetCell(fromIndex).Item.IsEmpty;
            WriteProbe(string.Format("[dragtest] wand before={0} after={1}", wandDumpBefore, wandContainer.Dump()));
            WriteProbe(string.Format("[dragtest] {0} 拖拽事务(法术卡进法杖槽) moved={1} ghostCleared={2} srcDecreased={3}",
                okMove && ghostGone && srcDecreased ? "PASS" : "FAIL", okMove, ghostGone, srcDecreased));

            // 再走一次非法拖拽（法杖卡 → 法杖槽）验证拒绝路径与高亮复位
            if (form.HandLeftContainer != null)
            {
                var handCell = form.HandLeftContainer.GetCell(0);
                if (handCell != null && !handCell.Item.IsEmpty)
                {
                    pointer.position = RectTransformUtility.WorldToScreenPoint(null, handCell.transform.position);
                    bool begun2 = manager.Begin(handCell, pointer, null, new Vector2(120f, 120f), handCell.transform as RectTransform);
                    pointer.position = RectTransformUtility.WorldToScreenPoint(null, targetCell.transform.position);
                    manager.UpdatePointer(pointer);
                    string rejectReason = null;
                    if (manager.CurrentTarget != null)
                    {
                        manager.CurrentTarget.CanAccept(handCell.GetPayload(), out rejectReason);
                    }
                    manager.End(pointer);
                    yield return new WaitForSeconds(0.4f);
                    bool handStillThere = !form.HandLeftContainer.GetCell(0).Item.IsEmpty;
                    bool highlightReset = manager.CurrentTarget == null
                        && targetCell.GetComponent<UI.UiDropTarget>() != null
                        && targetCell.GetComponent<UI.UiDropTarget>().Highlight == UI.DropHighlight.None;
                    WriteProbe(string.Format("[dragtest] {0} 非法拖拽(法杖→法术槽) begun={1} reason={2} handIntact={3} highlightReset={4}",
                        begun2 && handStillThere && !string.IsNullOrEmpty(rejectReason) && highlightReset ? "PASS" : "FAIL",
                        begun2, rejectReason ?? "-", handStillThere, highlightReset));
                }
            }

            WriteProbe("[dragtest] done");
        }

        private void Start()
        {
            if (s_ItemsTestOnly)
            {
                if (s_AutoSpell) { StartCoroutine(AutoSpellSelfTestFlow()); }
                else if (s_AutoBuff) { StartCoroutine(AutoBuffSelfTestFlow()); }
                else if (s_AutoPassive) { StartCoroutine(AutoPassiveSelfTestFlow()); }
                else { StartCoroutine(AutoItemSelfTestFlow()); }
                return;
            }
            if (s_IsSettingsTest)
            {
                StartCoroutine(AutoSettingsFlow());
                return;
            }
            if (s_IsMenuFlowTest)
            {
                StartCoroutine(AutoMenuFlowTest());
                return;
            }
            StartCoroutine(s_IsJoiner ? AutoJoinFlow() : AutoFlow());
            StartCoroutine(FrameTimeMonitor());

            // -autofire：Host 自动连发 —— 正好用来观察"每次发射"的明细（物品序列/耗蓝/子弹参数）
            if (s_AutoFire)
            {
                Simulation.CastProbe.Sink = WriteProbe;
                Simulation.CastProbe.Enabled = true;
            }

            if (s_AutoCardClick)
            {
                StartCoroutine(AutoCardClickFlow());
            }
            if (s_AutoItems)
            {
                StartCoroutine(AutoItemSelfTestFlow());   // 与完整流程并用时也跑一遍自检
            }
            if (s_AutoSpell)
            {
                StartCoroutine(AutoSpellSelfTestFlow());  // S2/D23：施法自检 + 预算打印
            }
        }

        /// <summary>
        /// 施法自检（S2/D23）：等配置就绪后跑与编辑器菜单相同的断言套件，
        /// 并打印两手编译后的序列与预算 dry-run，逐行写入运行探针（[SpellTest]/[autospell] 前缀）。
        /// </summary>
        private IEnumerator AutoSpellSelfTestFlow()
        {
            // 把模拟层的 [cast] 探针接到运行探针文件（Q2 中止等断言用）
            Simulation.CastProbe.Sink = WriteProbe;
            // 打开"每次发射明细"（物品序列/本帧耗蓝/子弹参数）：这是验证"改序列是否真的改变子弹"的关键日志
            Simulation.CastProbe.Enabled = true;

            yield return new WaitForSeconds(2.5f);   // 等 ConfigService 建索引完成

            string report;
            try
            {
                report = Items.CastSelfTest.Run();
            }
            catch (System.Exception e)
            {
                WriteProbe("[SpellTest] EXCEPTION " + e.Message);
                Debug.LogError("[SpellTest] exception: " + e);
                yield break;
            }

            var lines = report.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i])) { WriteProbe(lines[i]); }
            }

            // 真实内容：两手编译后的序列 + 预算 dry-run（P7/P2b=A 口径）
            try
            {
                var svc = ItemSystem.Service;
                var table = Items.ConfigItemTable.Instance;
                if (svc != null && table != null)
                {
                    EmitHandPreview("Hand_L", svc, table);
                    EmitHandPreview("Hand_R", svc, table);
                }
                else
                {
                    WriteProbe("[autospell] 物品服务未就绪，跳过序列预览");
                }
            }
            catch (System.Exception e)
            {
                WriteProbe("[autospell] preview EXCEPTION " + e.Message);
            }

            WriteProbe("[autospell] done");
        }

        /// <summary>
        /// 临时 Buff 自检（S3/D24）：跑与编辑器菜单 EmojiWar/Tools/Buff Self-Test 相同的断言套件，
        /// 逐行写入运行探针（[BuffTest] 前缀），供 _tmp/verify_buff.ps1 断言。
        /// 纯 C# 套件（不依赖场景/资产），所以只需等配置就绪即可跑。
        /// </summary>
        private IEnumerator AutoBuffSelfTestFlow()
        {
            yield return new WaitForSeconds(2.5f);   // 等 ConfigService 建索引完成

            string report;
            try
            {
                report = Items.BuffSelfTest.Run();
            }
            catch (System.Exception e)
            {
                WriteProbe("[BuffTest] EXCEPTION " + e.Message);
                Debug.LogError("[BuffTest] exception: " + e);
                yield break;
            }

            var lines = report.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i])) { WriteProbe(lines[i]); }
            }

            // 顺带把运行期实际生效的 Buff 上限打出来（证明来自 SpellSystemConfigSO，而不是硬编码）
            var lim = Items.BuffLimits.FromConfig();
            WriteProbe(string.Format(
                "[autobuff] BuffLimits(配置): GlobalMaxStacks={0} DefaultMaxStacks={1} DetonateDamagePerStack={2}",
                lim.GlobalMaxStacks, lim.DefaultMaxStacks, lim.DetonateDamagePerStack));

            WriteProbe("[autobuff] done");
        }

        /// <summary>
        /// 被动触发自检（S4/D25）：跑与编辑器菜单 EmojiWar/Tools/Passive Self-Test 相同的断言套件，
        /// 逐行写入运行探针（[PassiveTest] 前缀），供 _tmp/verify_passive.ps1 断言。
        /// 纯 C# 套件（自建 CastEventBus 模拟命中/击杀），不依赖场景与战斗。
        /// </summary>
        private IEnumerator AutoPassiveSelfTestFlow()
        {
            yield return new WaitForSeconds(2.5f);   // 等 ConfigService 建索引完成

            string report;
            try
            {
                report = Items.PassiveSelfTest.Run();
            }
            catch (System.Exception e)
            {
                WriteProbe("[PassiveTest] EXCEPTION " + e.Message);
                Debug.LogError("[PassiveTest] exception: " + e);
                yield break;
            }

            var lines = report.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i])) { WriteProbe(lines[i]); }
            }
            WriteProbe("[autopassive] done");
        }

        private static void EmitHandPreview(string handId, Items.InventoryService svc, Items.IItemTable table)
        {
            var program = Items.LoadoutCompiler.CompileHand(handId, table, svc);
            if (!program.IsValid)
            {
                WriteProbe("[autospell] " + handId + " 空手（无法杖程序）");
                return;
            }
            WriteProbe("[autospell] " + handId + " " + program.Dump());

            // 逐个物品 dump 编译后的关键字段（排查"同源码两套数"的唯一可靠手段）
            for (int i = 0; i < program.SlotCount; i++)
            {
                var sp = program.SpellAt(i);
                if (sp.IsEmpty) { continue; }
                WriteProbe(string.Format(
                    "[autospell]   {0} slot{1} spell={2} baseMana={3} isModifier={4} scope={5}/{6} ownDelay={7} selfManaMul={8} selfManaAdd={9} selfDelayAdd={10}",
                    handId, i, sp.SpellId, sp.ManaCost, sp.IsModifier, sp.TargetScope, sp.AffectCount,
                    sp.OwnDelayAdd.ToString("R"), sp.Self.ManaMul.ToString("R"),
                    sp.Self.ManaAdd.ToString("R"), sp.Self.DelayAdd.ToString("R")));
            }

            var pv = Items.LoadoutCompiler.Preview(program);
            WriteProbe(string.Format(
                "[autospell] {0} 预算: items={1} 总耗蓝={2}/{3} Σ延迟={4:F2}s 序列时长={5:F2}s 总充能={6:F2}s 周期={7:F2}s{8}",
                handId, pv.ItemCount, pv.ManaTotal, program.ManaMax, pv.DelaySum,
                pv.SequenceDuration, pv.TotalRecharge, pv.CycleSeconds,
                pv.ManaExceedsPool ? (" (会中止于槽" + pv.AbortSlot + ")") : ""));

            // 同一程序跑一遍真解释器：与 preview 逐项对照
            // （**耗蓝必须一致**：它是 Q2 中止判定与 S6 预览面板的正确性前提）
            var st = Items.CastRuntimeState.For(program);
            var plan = new Simulation.CastPlan();
            int total = 0, trig = 0;
            for (int f = 0; f < 256; f++)
            {
                Simulation.CastResolver.Tick(program, ref st, Simulation.CastResolver.TickSeconds, f == 0, plan, null);
                total += plan.ManaSpent;
                trig += plan.Triggers;
                if (f > 0 && !st.CastActive) { break; }
            }
            WriteProbe(string.Format("[autospell] {0} 实算: triggers={1} manaSpent={2}{3}",
                handId, trig, total,
                total == pv.ManaTotal ? "（与 preview 一致）" : " **与 preview 不一致！**"));
        }

        /// <summary>
        /// 物品事务自检（P2）：等配置就绪后在构建版里跑与编辑器菜单相同的断言套件，
        /// 逐行写入运行探针（[ItemTest] 前缀），供沙箱验证。
        /// </summary>
        private IEnumerator AutoItemSelfTestFlow()
        {
            yield return new WaitForSeconds(2f);   // 等 ConfigService 建索引完成
            string report;
            try
            {
                report = Items.ItemSelfTest.Run(Items.ConfigItemTable.Instance);
            }
            catch (System.Exception e)
            {
                WriteProbe("[ItemTest] EXCEPTION " + e.Message);
                Debug.LogError("[ItemTest] exception: " + e);
                yield break;
            }

            var lines = report.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i])) { WriteProbe(lines[i]); }
            }
            Debug.Log("[ItemTest]\n" + report);
        }

        /// <summary>退出诊断：记录进程退出时刻与上下文（区分崩溃/正常关闭）。</summary>
        private void OnApplicationQuit()
        {
            WriteProbe("[auto] OnApplicationQuit called (graceful quit path)");
        }

        private void OnDisable()
        {
            WriteProbe("[auto] AutoPlay OnDisable (component disabled/destroyed)");
        }

        private void OnDestroy()
        {
            WriteProbe("[auto] AutoPlay OnDestroy");
        }

        /// <summary>
        /// 帧时间监视：每 5 秒报告一次平均帧时间与最大帧时间（定位卡顿尖峰）。
        /// </summary>
        private IEnumerator FrameTimeMonitor()
        {
            var wait = new WaitForSeconds(5f);
            while (true)
            {
                yield return wait;
                int frames = 0;
                float total = 0f;
                float maxDt = 0f;
                for (int i = 0; i < 300; i++)   // 采样 300 帧（约 1-5 秒）
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    frames++;
                    total += dt;
                    if (dt > maxDt) maxDt = dt;
                }
                float avg = frames > 0 ? total / frames : 0f;
                WriteProbe(string.Format("[fps] avg={0:F1}ms ({1:F0}fps) max={2:F1}ms ({3:F0}fps)",
                    avg * 1000f, frames > 0 ? frames / total : 0f,
                    maxDt * 1000f, maxDt > 0f ? 1f / maxDt : 0f));
            }
        }

        /// <summary>加入者自动流程：开始游戏 → 加入 → 房间自动准备 → 全部准备后自动开始。</summary>
        private IEnumerator AutoJoinFlow()
        {
            WriteProbe("[auto] join flow started");

            yield return new WaitForSeconds(4f);
            WriteProbe("[auto] trigger start game");
            UI.MenuForm.TriggerStartGame();

            // 独立选角色流程已移除：角色在房间内选择（默认 1 号）；直接进大厅
            yield return new WaitForSeconds(3f);
            WriteProbe("[auto] goto lobby (character select removed, default char 1)");

            yield return new WaitForSeconds(2f);
            WriteProbe("[auto] trigger join 127.0.0.1:7777");
            UI.LobbyForm.TriggerJoinRoom("127.0.0.1", Network.NetworkService.DefaultPort);

            // 自动准备为显式开关（-autoready）：默认不自动准备，等待手动点"准备"
            if (!s_AutoReady)
            {
                WriteProbe("[auto] joiner 进入房间，等待手动准备（未传 -autoready）");
                yield break;
            }

            // 进入房间后自动准备
            yield return new WaitForSeconds(3f);
            WriteProbe("[auto] joiner trigger ready");
            UI.RoomFormEvents.RequestReady();

            // 等待战斗开始（全部准备 → 自动开始）
            yield return new WaitForSeconds(12f);
            var battleScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Battle");
            WriteProbe("[auto] joiner battleSceneLoaded=" + battleScene.isLoaded);

            // UI 状态探针：列出当前所有 UIForm（验证进战斗后旧 UI 是否已关闭）
            try
            {
                var ui = GameEntry.UI;
                var forms = new System.Collections.Generic.List<string>();
                if (ui != null)
                {
                    foreach (var g in ui.GetAllUIGroups())
                    {
                        foreach (var f in g.GetAllUIForms())
                        {
                            var logic = (f as UnityGameFramework.Runtime.UIForm);
                            if (logic != null && logic.Logic != null)
                            {
                                forms.Add(logic.Logic.Name + "(" + (logic.Logic.gameObject.activeInHierarchy ? "A" : "I") + ")");
                            }
                        }
                    }
                }
                var canvases = Object.FindObjectsOfType<Canvas>(true);
                var overlayList = new System.Collections.Generic.List<string>();
                foreach (var c in canvases)
                {
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy)
                    {
                        overlayList.Add(c.gameObject.name);
                    }
                }
                WriteProbe("[auto] joiner UIForms=" + string.Join(",", forms.ToArray()) +
                    " overlay=" + string.Join(",", overlayList.ToArray()));
            }
            catch (System.Exception e)
            {
                WriteProbe("[auto] joiner UI probe error: " + e.Message);
            }

            // 统计网络实体（确定性模拟玩家）
            var netLogics = Object.FindObjectsOfType<Network.NetClientLogic>();
            int total = 0;
            foreach (var n in netLogics)
            {
                if (n.Simulation != null)
                {
                    total += n.Simulation.Players.Count;
                }
            }
            WriteProbe("[auto] joiner net-players=" + total + " (expect >=1 = host entity)");
            Debug.Log("[AutoPlay] 加入者模拟玩家数=" + total);
        }

        /// <summary>
        /// 复现"非主机点角色卡片"bug（-autocard）：
        /// 等待进入房间且角色面板打开 → 自动模拟点击第 2 张角色卡片
        /// → 周期性列出 UIForms 与记录切换角色消息计数（观察回环/错误打开 UI 现象）。
        /// </summary>
        private IEnumerator AutoCardClickFlow()
        {
            WriteProbe("[card-test] flow started, joiner=" + s_IsJoiner);

            // 等待流程走到房间（自动加入/创建房间大约 9-10 秒后进入房间并默认展开角色面板）
            yield return new WaitForSeconds(12f);

            // 覆盖层验证：进房间后（Dock 默认收起）先记录一次全 UI 组/窗体状态，
            // 确认 RoomForm 在最高层 Top 组可见。
            ProbeOverlayLayout("[overlay] before-dock");

            // 找到 CharacterDockForm 实例（通过 UI 组遍历）
            var dock = FindCharacterDock();
            if (dock == null && !s_IsJoiner)
            {
                // Host：先点房间条"切换角色"展开角色面板（进房间默认收起，需展开后再点卡）
                var roomForm = FindRoomForm();
                if (roomForm != null)
                {
                    WriteProbe("[card-test] open char dock via RoomForm.ToggleCharDock(true)");
                    roomForm.ToggleCharDock(true);
                    yield return new WaitForSeconds(2f);
                    dock = FindCharacterDock();
                }
            }
            if (dock == null)
            {
                WriteProbe("[card-test] CharacterDockForm not found at 12s (host 改用 SetLocalCharacter 切角色)");
                // Host 无 dock 也能本地切角色（等效点第 2 张卡：走同一 HandleChangeCharacter → ApplyCharacter）
                if (!s_IsJoiner)
                {
                    var hostLogic = GameEntry.Instance != null
                        ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                        : null;
                    if (hostLogic != null)
                    {
                        WriteProbe("[card-test] host SetLocalCharacter(2)");
                        hostLogic.SetLocalCharacter(2);
                    }
                    else
                    {
                        WriteProbe("[card-test] FAIL: hostLogic not found");
                    }
                }
                else
                {
                    // 再等 3 秒重试一次（joiner 的 dock 通常更晚出现）
                    yield return new WaitForSeconds(3f);
                    dock = FindCharacterDock();
                    if (dock == null)
                    {
                        WriteProbe("[card-test] FAIL: CharacterDockForm not found after retry");
                        yield break;
                    }
                    WriteProbe("[card-test] CharacterDockForm found (retry), cards=" + dock.CardCount +
                        " selected=" + dock.SelectedCharacterId);
                    dock.SimulateCardClick(dock.CardCount >= 2 ? 2 : 1);
                }
            }
            else
            {
                WriteProbe("[card-test] CharacterDockForm found, cards=" + dock.CardCount +
                    " selected=" + dock.SelectedCharacterId);
                // 模拟点第 2 张卡片（若只有 1 张卡则点第 1 张；直接走 SelectCharacter(notify=true) 与卡片 onClick 同路径）
                dock.SimulateCardClick(dock.CardCount >= 2 ? 2 : 1);
            }

            // 覆盖层验证：Dock 打开后再次记录——RoomForm(Top) 必须仍可见且 Canvas 排序高于 Dock(Popup)。
            ProbeOverlayLayout("[overlay] after-dock");

            // 观察：每秒记录 UIForms（看是否错误打开房间 UI / 反复刷新）与计数
            int last = 0;
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(1f);
                int now = UI.CharacterDockEvents.ChangeCount;
                WriteProbe("[card-test] t+" + (i + 1) + "s ChangeCount=" + now +
                    " (+" + (now - last) + ") UIForms=" + ListUiFormNames());
                last = now;
            }
            WriteProbe("[card-test] done");
        }

        /// <summary>查找当前打开的房间窗体实例。</summary>
        private static UI.RoomForm FindRoomForm()
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    return null;
                }
                foreach (var g in ui.GetAllUIGroups())
                {
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            var rf = logic.Logic as UI.RoomForm;
                            if (rf != null && rf.gameObject.activeInHierarchy)
                            {
                                return rf;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>查找当前打开的角色面板实例。</summary>
        private static UI.CharacterDockForm FindCharacterDock()
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    return null;
                }
                foreach (var g in ui.GetAllUIGroups())
                {
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            var dock = logic.Logic as UI.CharacterDockForm;
                            if (dock != null && dock.gameObject.activeInHierarchy)
                            {
                                return dock;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>列出当前所有 UIForm（观察是否有 RoomForm 被错误打开/重复）。</summary>
        private static string ListUiFormNames()
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    return "no-ui";
                }
                var forms = new System.Collections.Generic.List<string>();
                foreach (var g in ui.GetAllUIGroups())
                {
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            forms.Add(logic.Logic.Name + "(" + (logic.Logic.gameObject.activeInHierarchy ? "A" : "I") + ")");
                        }
                    }
                }
                return string.Join(",", forms.ToArray());
            }
            catch
            {
                return "err";
            }
        }

        /// <summary>
        /// 覆盖层布局探针：列出所有 UI 组（名称/深度）及其中的窗体（名称/可见性/Canvas.sortingOrder）。
        /// 用于验证"房间准备条 = 最高层 Top 组附加悬浮 UI"：
        ///   RoomForm 应在 Top 组可见，且 sortingOrder 高于任何 Default/Popup 窗体（含全屏选角 Dock）。
        /// </summary>
        private static void ProbeOverlayLayout(string tag)
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    WriteProbe(tag + " no-ui");
                    return;
                }
                var lines = new System.Collections.Generic.List<string>();
                foreach (var g in ui.GetAllUIGroups())
                {
                    int depth = -1;
                    try
                    {
                        depth = g.Depth;
                    }
                    catch
                    {
                    }
                    lines.Add("group=" + g.Name + "(depth " + depth + ")");
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            string vis = logic.Logic.gameObject.activeInHierarchy ? "A" : "I";
                            int order = -999;
                            try
                            {
                                var cv = logic.Logic.gameObject.GetComponentInChildren<UnityEngine.Canvas>(true);
                                if (cv != null)
                                {
                                    order = cv.sortingOrder;
                                }
                            }
                            catch
                            {
                            }
                            lines.Add("  form=" + logic.Logic.Name + "(" + vis + ") sort=" + order);
                        }
                    }
                }
                WriteProbe(tag + " UI: " + string.Join(" | ", lines.ToArray()));
            }
            catch (System.Exception e)
            {
                WriteProbe(tag + " probe-err: " + e.Message);
            }
        }

        /// <summary>
        /// 主菜单→多人游戏新流程验证（-automenu）：
        /// 等菜单打开 → 列出 UIForms 确认 MenuForm → 触发"多人游戏"（等效按钮）
        /// → MultiplayerForm 打开 → 触发"创建房间" → 房间页打开。
        /// 供自动化验证主界面四按钮与多人中转流程（不走旧 Lobby）。
        /// </summary>
        private IEnumerator AutoMenuFlowTest()
        {
            WriteProbe("[menu-flow] flow started, join=" + s_MenuFlowJoin);
            yield return new WaitForSeconds(6f);
            WriteProbe("[menu-flow] menu UIForms=" + ListUiFormNames());

            // 等效点"多人游戏"：走 ProcedureMenu 订阅的事件 → ProcedureMultiplayer
            UI.MenuForm.TriggerMultiplayer();
            yield return new WaitForSeconds(3f);
            WriteProbe("[menu-flow] after multiplayer: UIForms=" + ListUiFormNames());

            // 打开 MultiplayerForm 后验证
            var found = FindMultiplayerForm();
            if (found == null)
            {
                WriteProbe("[menu-flow] FAIL: MultiplayerForm not open");
                yield break;
            }

            if (s_MenuFlowJoin)
            {
                // 加入游戏分支：等效点"加入游戏" → ProcedureJoinRoom → JoinListForm 自动扫描
                WriteProbe("[menu-flow] MultiplayerForm open, trigger join game");
                UI.MultiplayerForm.TriggerJoinGame();
                yield return new WaitForSeconds(6f);
                WriteProbe("[menu-flow] after join: UIForms=" + ListUiFormNames() +
                    " rooms=" + Network.RoomDiscovery.RoomCount);
                var rooms = Network.RoomDiscovery.Rooms;
                if (rooms == null || rooms.Count == 0)
                {
                    WriteProbe("[menu-flow] join: 未发现房间（RoomDiscovery 0 个）");
                    yield break;
                }
                // 加入第一个发现的房间（等效点房间项）
                var first = rooms[0];
                WriteProbe("[menu-flow] join room: " + first.HostName + " @ " + first.Ip + ":" + first.Port);
                UI.JoinListEvents.RequestJoin(first.HostName, first.Ip, first.Port);
                yield return new WaitForSeconds(5f);
                WriteProbe("[menu-flow] after join room: UIForms=" + ListUiFormNames());
                WriteProbe("[menu-flow] join done");
                yield break;
            }

            WriteProbe("[menu-flow] MultiplayerForm open, trigger create room");
            UI.MultiplayerForm.TriggerCreate("房主");
            yield return new WaitForSeconds(4f);
            WriteProbe("[menu-flow] after create: UIForms=" + ListUiFormNames());
            WriteProbe("[menu-flow] done");
        }

        /// <summary>查找当前打开的多人游戏窗体。</summary>
        private static UI.MultiplayerForm FindMultiplayerForm()
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    return null;
                }
                foreach (var g in ui.GetAllUIGroups())
                {
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            var mf = logic.Logic as UI.MultiplayerForm;
                            if (mf != null && mf.gameObject.activeInHierarchy)
                            {
                                return mf;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// 设置页自动化验证（-autosettings）：
        /// 打开主菜单 → 点"设置" → 验证窗体打开与按钮绑定 → 模拟点窗口化切换
        /// → 记录 Screen.fullScreen 变化 → 点返回 → 验证窗体关闭。
        /// </summary>
        private IEnumerator AutoSettingsFlow()
        {
            WriteProbe("[settings-test] flow started");

            // 等待启动流程与主菜单就绪（-autosettings 不自动开始游戏，停在主菜单）
            yield return new WaitForSeconds(6f);
            WriteProbe("[settings-test] opening settings via MenuForm button");

            // 通过 MenuForm 的公开事件触发打开设置页（等效于点"设置"按钮）
            UI.MenuForm.TriggerOpenSettings();

            // 等待设置页资源加载并打开
            yield return new WaitForSeconds(2f);

            // 找到 SettingsForm 实例验证状态
            var settingsForm = FindSettingsForm();
            if (settingsForm == null)
            {
                WriteProbe("[settings-test] FAIL: SettingsForm not open after TriggerOpenSettings");
                yield break;
            }
            WriteProbe("[settings-test] SettingsForm open, windowed=" + settingsForm.GetWindowedState() +
                " fullScreen=" + Screen.fullScreen);

            // 模拟点窗口化（勾选窗口化 → Screen.fullScreen 应变 false）
            bool beforeFs = Screen.fullScreen;
            settingsForm.SimulateWindowedToggle();
            yield return new WaitForSeconds(1f);
            bool afterFs = Screen.fullScreen;
            WriteProbe(string.Format("[settings-test] toggle1: fs {0}->{1} (expect {2}->{3})",
                beforeFs, afterFs, true, false));
            if (beforeFs && !afterFs)
            {
                WriteProbe("[settings-test] PASS windowed toggle (fullScreen off)");
            }
            else
            {
                WriteProbe("[settings-test] FAIL windowed toggle");
            }

            // 再次点（切回全屏）
            settingsForm.SimulateWindowedToggle();
            yield return new WaitForSeconds(1f);
            WriteProbe("[settings-test] toggle2: fullScreen=" + Screen.fullScreen +
                " windowed=" + settingsForm.GetWindowedState());

            // 点返回：设置页应关闭回主菜单
            settingsForm.SimulateBack();
            yield return new WaitForSeconds(2f);
            var after = FindSettingsForm();
            WriteProbe("[settings-test] after back, SettingsForm=" + (after != null ? "STILL OPEN" : "closed (PASS)") +
                " fullScreen=" + Screen.fullScreen);

            // 记录最终窗口状态后保持运行供人工查看
            WriteProbe("[settings-test] done. final fullScreen=" + Screen.fullScreen);
        }

        /// <summary>查找当前打开的设置窗体实例。</summary>
        private static UI.SettingsForm FindSettingsForm()
        {
            try
            {
                var ui = GameEntry.UI;
                if (ui == null)
                {
                    return null;
                }
                foreach (var g in ui.GetAllUIGroups())
                {
                    foreach (var f in g.GetAllUIForms())
                    {
                        var logic = (f as UnityGameFramework.Runtime.UIForm);
                        if (logic != null && logic.Logic != null)
                        {
                            var sf = logic.Logic as UI.SettingsForm;
                            if (sf != null && sf.gameObject.activeInHierarchy)
                            {
                                return sf;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private IEnumerator AutoFlow()
        {
            WriteProbe("[auto] auto flow started");

            // 等待启动流程与主菜单就绪
            yield return new WaitForSeconds(4f);
            WriteProbe("[auto] trigger start game");
            UI.MenuForm.TriggerStartGame();

            // 独立选角色流程已移除：角色在房间内选择（默认 1 号）；直接进大厅
            yield return new WaitForSeconds(3f);
            WriteProbe("[auto] goto lobby (character select removed, default char 1)");

            yield return new WaitForSeconds(2f);
            WriteProbe("[auto] trigger create room");
            UI.LobbyForm.TriggerCreateRoom();

            // -autofire：创建房间后 NetHostLogic 已挂载，开启自动开火（验证无限释放/连发）
            if (s_AutoFire)
            {
                var hostLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                    : null;
                if (hostLogic != null)
                {
                    hostLogic.EnableAutoFire();
                    WriteProbe("[auto] Host EnableAutoFire OK（无限释放验证）");
                }
                else
                {
                    WriteProbe("[auto] Host EnableAutoFire FAIL（未找到 NetHostLogic）");
                }
            }

            // ★ 等到期望人数到齐（或超时）再准备：否则房主会"一人开战"（房间里只有自己时
            //   `AllReady()` 即为真），后加入的客户端永远收不到 S2CBattleStart（2026-09-28 实测 P0）。
            if (s_AutoReadyPlayers > 0 || s_AutoReadyWaitSeconds > 0f)
            {
                var waitHostLogic = GameEntry.Instance != null
                    ? GameEntry.Instance.GetComponentInChildren<Network.NetHostLogic>()
                    : null;
                if (waitHostLogic != null && s_AutoReadyPlayers > 0)
                {
                    waitHostLogic.SetExpectedPlayers(s_AutoReadyPlayers);
                }
                float t0 = Time.unscaledTime;
                while (s_AutoReadyWaitSeconds > 0f && Time.unscaledTime - t0 < s_AutoReadyWaitSeconds)
                {
                    if (s_AutoReadyPlayers <= 0
                        || (waitHostLogic != null && waitHostLogic.PlayerCount >= s_AutoReadyPlayers))
                    {
                        break;
                    }
                    yield return null;
                }
                WriteProbe(string.Format("[auto] autoready 等待结束 players={0} 期望={1} 用时={2:F1}s",
                    waitHostLogic != null ? waitHostLogic.PlayerCount : -1,
                    s_AutoReadyPlayers, Time.unscaledTime - t0));
            }

            // 自动准备为显式开关（-autoready）：默认不自动准备，等待手动点"准备"。
            // （此前固定 30 秒自动准备会导致单人房间在 30 秒后自动开战，即"意外进入战斗"的根因）
            if (!s_AutoReady)
            {
                WriteProbe("[auto] host 进入房间，等待手动准备（未传 -autoready）");
                yield break;
            }

            // 进入房间后自动准备（延迟等待其他玩家加入；全部准备后自动开始）
            // -autoshop/-autodrag 是单机回归，不需要等人 → 缩短等待，避免探针还没跑到就超时
            float waitBeforeReady = (s_AutoShop || s_AutoDrag) ? 3f : 30f;
            yield return new WaitForSeconds(waitBeforeReady);
            WriteProbe("[auto] host trigger ready");
            UI.RoomFormEvents.RequestReady();

            // 等待战斗开始（模拟驱动：SimView 渲染玩家/敌人/子弹）
            yield return new WaitForSeconds(10f);

            // UI 状态探针：列出当前所有 UIForm（验证进战斗后旧 UI 是否已关闭）
            try
            {
                var ui = GameEntry.UI;
                var forms = new System.Collections.Generic.List<string>();
                if (ui != null)
                {
                    foreach (var g in ui.GetAllUIGroups())
                    {
                        foreach (var f in g.GetAllUIForms())
                        {
                            var logic = (f as UnityGameFramework.Runtime.UIForm);
                            if (logic != null && logic.Logic != null)
                            {
                                forms.Add(logic.Logic.Name + "(" + (logic.Logic.gameObject.activeInHierarchy ? "A" : "I") + ")");
                            }
                        }
                    }
                }
                var canvases = Object.FindObjectsOfType<Canvas>(true);
                var overlayList = new System.Collections.Generic.List<string>();
                foreach (var c in canvases)
                {
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy)
                    {
                        overlayList.Add(c.gameObject.name);
                    }
                }
                WriteProbe("[auto] UIForms=" + string.Join(",", forms.ToArray()) +
                    " overlay=" + string.Join(",", overlayList.ToArray()));
            }
            catch (System.Exception e)
            {
                WriteProbe("[auto] UI probe error: " + e.Message);
            }

            // -autobag：验证背包 Tab 显隐（战斗中打开不暂停；HUD 仍存活）
            if (s_AutoBag)
            {
                WriteProbe("[bag-test] toggle open (Tab)");
                UI.BackpackHotkey.Toggle();
                yield return new WaitForSeconds(3f);
                WriteProbe("[bag-test] open UIForms=" + ListUiFormNames());
                UI.BackpackHotkey.Toggle();
                yield return new WaitForSeconds(2f);
                WriteProbe("[bag-test] closed UIForms=" + ListUiFormNames());
                UI.BackpackHotkey.Toggle();
                yield return new WaitForSeconds(2f);
                WriteProbe("[bag-test] reopened UIForms=" + ListUiFormNames());
            }

            // -autodrag：拖拽集成探针（打开背包 → 合成指针事件把法术卡拖进法杖槽 → 断言事务与幽灵）
            if (s_AutoDrag)
            {
                yield return StartCoroutine(AutoDragProbe());
            }

            // -autoshop：波间商店回归（清空本波 → 商店打开 → 商店期间**不得自动开下一波** → 继续后开下一波）
            if (s_AutoShop)
            {
                yield return StartCoroutine(AutoShopProbe());
            }

            // 统计场景中实际渲染的 SpriteRenderer（验证美术是否生效）
            var renderers = Object.FindObjectsOfType<SpriteRenderer>();
            int withSprite = 0;
            foreach (var r in renderers)
            {
                if (r != null && r.sprite != null)
                {
                    withSprite++;
                }
            }

            // 统计确定性模拟实体（SimView 渲染 SimPlayer_/SimEnemy_/SimBullet_）
            int simPlayers = 0;
            int simEnemies = 0;
            int simBullets = 0;
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null)
                {
                    continue;
                }
                if (go.name.StartsWith("SimPlayer_"))
                {
                    simPlayers++;
                }
                else if (go.name.StartsWith("SimEnemy_"))
                {
                    simEnemies++;
                }
                else if (go.name.StartsWith("SimBullet_"))
                {
                    simBullets++;
                }
            }

            // 读取本地确定性模拟状态（帧号/玩家位置/敌人数，供双实例一致性对比）
            var sim = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            string simState = "no-sim";
            if (sim != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("frame=").Append(sim.FrameIndex)
                  .Append(" wave=").Append(sim.WaveIndex)
                  .Append(" players=").Append(sim.Players.Count)
                  .Append(" enemies=").Append(sim.Enemies.Count)
                  .Append(" bullets=").Append(sim.Bullets.Count);
                foreach (var p in sim.Players)
                {
                    sb.Append(" P").Append(p.SessionId).Append(":(").Append(p.Position.x.ToString("F2"))
                      .Append(",").Append(p.Position.y.ToString("F2")).Append(")");
                }
                simState = sb.ToString();
            }

            var players = Object.FindObjectsOfType<Entity.PlayerEntity>();
            var enemies = Object.FindObjectsOfType<Entity.EnemyEntity>();
            var simSim = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            string weapons = simSim != null ? simSim.DescribeWeapons() : "no-sim";
            WriteProbe(string.Format("[auto] SpriteRenderers={0} simPlayers={1} simEnemies={2} simBullets={3} weapons=[{4}] simState={5}",
                renderers.Length, simPlayers, simEnemies, simBullets, weapons, simState));
            Debug.Log(string.Format("[AutoPlay] 模拟实体统计: simPlayers={0} simEnemies={1} simBullets={2} weapons=[{3}]",
                simPlayers, simEnemies, simBullets, weapons));

            // 等玩家被敌人打死 → 结算界面 → 模拟点"重新开始"，验证重开路径不产生多玩家
            yield return new WaitForSeconds(15f);
            WriteProbe("[auto] trigger restart (game over flow)");
            UI.GameOverEvents.RequestRestart();
            yield return new WaitForSeconds(8f);

            var players2 = Object.FindObjectsOfType<Entity.PlayerEntity>();
            var enemies2 = Object.FindObjectsOfType<Entity.EnemyEntity>();
            WriteProbe(string.Format("[auto] after-restart: legacyPlayers={0} legacyEnemies={1}",
                players2.Length, enemies2.Length));
            Debug.Log(string.Format("[AutoPlay] 重开后: legacyPlayers={0} legacyEnemies={1}",
                players2.Length, enemies2.Length));

            // 同进程多局回归（文档 §6 B 组）：回到房间后再次准备开第二局，
            // 对比两局的 seed 与模拟初始状态，验证无跨局状态污染。
            // 房间页已恢复（seed=0 房间模拟）；再次触发准备 → 全部准备 → 第二局开始
            WriteProbe("[auto] 多局回归：第二局准备");
            UI.RoomFormEvents.RequestReady();
            yield return new WaitForSeconds(10f);

            var sim2 = GameEntry.SimView != null ? GameEntry.SimView.Simulation : null;
            if (sim2 != null)
            {
                WriteProbe(string.Format("[auto] run2 frame={0} wave={1} players={2} enemies={3}",
                    sim2.FrameIndex, sim2.WaveIndex, sim2.Players.Count, sim2.Enemies.Count));
            }
            else
            {
                WriteProbe("[auto] run2 no-sim（第二局未启动，检查房间/准备流程）");
            }

            // 流程结束标记：若协程自然跑完，后续 OnDestroy/OnApplicationQuit 探针可定位进程退出原因
            WriteProbe("[auto] AutoFlow finished (coroutine end)");
        }

        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
