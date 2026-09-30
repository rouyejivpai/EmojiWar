# EmojiWarStudio 项目约定（Agent 必读）

> 本文件是跨会话的**强制约定**入口。做任何改动前先读对应文档全文，不要只看本摘要。

## 一、UI 列表与 Cell（强制，来源：`doc/UI列表与Cell规范.md`）

1. **列表 = 容器 + Cell 子物体预制体**；禁止把多行内容拼进单个 Text。
2. Cell 小预制体放 `Assets/GameMain/UI/items/`，并登记进 `Constant.UIItemAssetPath` + `GameResourceBuilder.AssetPaths`。
3. **预制体里，列表容器（正）下方必须放一个 Cell 预制体实例**（作者态占位 = 运行时行源）；
   运行时经 `UI/UiListCell.Resolve(...)` 取用（模板移出容器 → `GfUiItemPool.GlobalHiddenRoot`），
   没放模板才回落到 `items/` 路径。**新建/重建窗体预制体时必须一并生成模板**
   （参考 `Editor/BackpackPrefabBuilder.AddCellTemplate`）。
4. 刷新 = **先清空再生成**：池内行 `Recycle`，非池子物体（多放的模板/残留）`Destroy`，
   容器内永远只有"当前应显示的行"；关窗 `ClearContainer` + `pool.Destroy()` + `UiListCell.Release`。
5. 每个列表都要在 `Editor/UiListNormTools.CellByContainer` 登记，并跑
   `EmojiWar/Tools/Check UI List Cell Norm`（必须全 `OK`）。
6. 新增/修改列表后要写探针断言：容器 `children` 数 == 行数、模板取用日志 `[uilist]`。

## 一之二、UI 进度条（强制）

- **所有进度条（血条 / 能量条 / 冷却条 / 后续任何 Fill 条）统一走 `UI/UiBarSmoother.cs`**：
  宿主只负责 `SetTarget(ratio)`（首帧传 `immediate:true`），**禁止直接写 `sizeDelta` 造成跳变**；
- 平滑为帧率无关指数 lerp（`m_BarLerpSpeed`，默认 8 ≈ 0.12s 到位），像素变化 < 0.1 不写布局（防 Canvas 重建）；
- 新进度条照 `BattleHudForm` 的三条（`m_HpFill` / `m_EnergyFill` / `m_FireProgressFill`）接法写，并保留 `[hudbar]` 类探针便于回归。

## 一之三、悬停详情面板（强制）

- **物品/法杖的悬停详情统一走 `UI/Items/ItemDetailPanel.cs`**（`Assets/GameMain/UI/items/ItemDetailPanel.prefab`）；
  禁止再往 `UiSlotView` 里内联拼字符串（旧的 `BuildTooltip` 已废弃）——内容随字段增长会失控；
- 面板内部 = **固定文本节点（标题/副标题/段标题）+ 动态段落容器**；每个容器都要按 §一 的列表规范
  放一个行 Cell 模板（行 Cell = `UI/Items/ItemDetailCell.cs`，字段 `Icon / Name / Value`）；
- 生成/改样式走菜单 `EmojiWar/Setup/Build Item Detail Panel (S6)`（`Editor/ItemDetailPanelBuilder.cs`），
  改完**必须重打 AssetBundle** 并确认已登记 `Constant.UIItemAssetPath.ItemDetailPanel` + `GameResourceBuilder.AssetPaths`；
- 面板挂在独立顶层 Canvas（sortingOrder 30000、`blocksRaycasts=false`），**永不挡拖拽**；拖拽中不弹详情。

## 二、数据与配置（来源：`doc/数据驱动架构ADR.md`）

- 运行时数据源 = `Resources/Data/**` 的 SO，统一经 `ConfigService` 访问与校验；
- **资产类字段一律用引用（Sprite / GameObject），禁止用字符串名/路径**（图标经 `ArtManager.LoadEmoji` 解析）；
- 表现层数据（图标等）不得进入帧同步模拟与网络消息。

## 三、帧同步（确定性）

- Lockstep 20Hz，Host 权威；模拟层不得依赖 Unity 时间/随机/表现数据；表现与模拟通过 Id 关联。

## 四、改动生效链路（必须走完）

- 改脚本 → 编译（Unity 编辑器编译通过，看 `Library/ScriptAssemblies/*.dll` 时间戳）；
- **改预制体 / items 小预制体 → 必须重打 AssetBundle**（`EmojiWar/Tools/Build Runtime AssetBundles`），否则 exe 里看不到改动；
- 出包 → `manage_build` windows64（MCP 通道抽风时改用编辑器菜单 `EmojiWar/Tools/Build Windows64 Player`），
  再把 `Builds/StandaloneWindows64/*` 复制为 `Builds/EmojiWar2_final*`（等价菜单：`EmojiWar/Tools/Refresh Final Artifacts`）；
- 编辑器 MCP 通道挂死时：先让 Unity 失焦/弹窗处理掉（`ping not answered` 多半是被保存/编译对话框挡住）或重启编辑器；
  MCP 会话需 `set_active_instance EmojiWar2@43bdfc418b1a5376`；新增脚本文件后若未触发编译，改动任一已有 `.cs` 再 `Assets/Refresh`；
- 验证以**构建版 exe + AutoPlay 参数 + 运行探针**（`Builds/**/Logs/runtime_probe_<pid>.txt`）为准，编辑器 Play 无法接收命令行参数。

## 五、用户手改过的资产不要覆盖

- `RoomForm.prefab`、`CharacterDockForm.prefab` 的手工布局不得被生成器重建；
- 需要重建 `BackpackForm.prefab` / 窗体预制体前先确认（生成器会整体覆盖，手工改动会丢）。
