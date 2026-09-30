# EmojiWar2 UI 列表与 Cell 规范

> 目标：让“动态列表”在工程内只有一套写法，可复用、可回归、不反复造轮子。

## 一、强制规则（新列表必须遵守）

1. **列表 = 容器 + Cell 子物体**
   - 列表区域是一个“容器”（空 RectTransform 占位，或专用 Panel），**每个数据项 = 一个 Cell 子物体**；
   - **禁止**把多条内容拼进单个 Text（`AppendLine` 拼多行当列表）——列表节点永不承担文本展示；
   - 从旧“单文本列表”迁移时：容器可复用原 Text 节点的矩形（保留作者布局），但必须**运行时改名（如 `PlayerListContainer`）并销毁其 Text 组件**，Hierarchy 里不再有任何 `txt_*` 文本残留（RoomForm 已按此处理）。

2. **Cell 是独立预制体，放 `Assets/GameMain/UI/items/`**
   - 例：`PlayerCell.prefab`、`CharacterCard.prefab`、`RoomItemRow.prefab`；
   - 运行时经 `UiPrefab`（`GameEntry.Resource` 全路径，'game' AssetBundle）加载；
   - 新加 Cell 后必须把它加进 `GameResourceBuilder.AssetPaths`，并重建 AssetBundle。

3. **在“列表所在位置”下放 Cell 子物体预制体；刷新 = 先清空再生成，保证无残留**
   - **作者态（预制体里）**：列表容器（正）下方**必须放一个 Cell 预制体实例**——它同时是**行源**：编辑器里就能看到列表项长什么样、多大（WYSIWYG），运行时也不用字符串路径去找预制体；
   - **运行时**：行源统一经 `UI/UiListCell.Resolve(container, label, fallbackItemPath, cachedSource, onReady)` 取用——先把模板实例**移出容器**（→ 全局隐藏根 `GfUiItemPool.GlobalHiddenRoot`，容器 100% 干净）并作为 `Instantiate` 模板；容器下确实没放模板时，才回落到 `items/` 路径（`UiPrefab`）；
   - Cell 实例必须挂在**列表容器的正下方**（`SetParent(container)`，本地坐标摆行）；
   - 每次刷新：**先清空容器内全部现有 Cell → 再按数据顺序逐个生成**（严禁对旧数据做“差量修补”，避免旧行/多余行/过期数据残留）；
   - 清空 = 把**容器内所有子物体**清掉：本池行回收进 `GfUiItemPool`（空闲项**移出容器并移出窗体层级**，统一挂到全局隐藏根 `GfItemPoolHiddenRoot`（DontDestroyOnLoad、非 UI、无渲染）并 SetActive(false)）；**不在池里的子物体（多放的模板、历史残留等）直接 Destroy**——容器 100% 清空；重建 = 同一池 `Acquire`（无空闲才实例化）——既有“对象池复用”，容器与窗体层级都不留任何残留；
   - 关窗/离开：清空容器 → `pool.Destroy()` → `UiListCell.Release(ref cellSource)`（模板实例已挂到隐藏根，必须显式销毁，否则跨场景常驻）；
   - 列表容器内**永远只有“当前应显示的行”**（Hierarchy 可视即真相）。

4. **Cell 数据填充幂等**
   - 每次刷新对该行调用 `SetContent(...)` 全量赋值（文本/颜色等）；
   - Cell 脚本内部用子物体按名称 `Find` 缓存，不依赖 QuickBind。

5. **布局语义**
   - 容器保留作者在预制体里调的矩形（位置/尺寸）；
   - 行内排版用锚点或 LayoutGroup；行间顺序由代码定位或 LayoutGroup，避免写死“按整屏坐标”的魔法值（超宽/超高屏会裁切）。

6. **空态处理**
   - 空列表 = 0 行（清空后不生成任何 Cell）；空态提示用独立提示位，**不得复用列表容器节点的 Text**。

## 二、落地范式（参考实现）

```
数据(数组) ── Parse/映射 ──> rows[]
RefreshList(rows)：
  0) ResolveCellSource()     // 行源：容器下预放的 Cell 预制体实例（模板）优先，否则 items/ 路径回落（UiListCell.Resolve）
  1) PrepareContainer()      // 容器就绪：列表 Text 永久禁用，仅保留矩形
  2) ClearAll()              // 先清空：现有 Cell 全部 pool.Recycle（移出容器→全局隐藏根），非池子物体 Destroy，容器/窗体下清零
  3) for row in rows:        // 再生成：pool.Acquire(Instantiate(cellSource), container) → cell.SetContent → PositionRow
      go = pool.Acquire(Instantiate(cellSource), container)
      cell.SetContent(row); go 定位
  空列表：步骤 2 后不再生成任何 Cell
关窗/离开：ClearAll() → pool.Destroy() → UiListCell.Release(ref cellSource)
```
参考文件：`UI/UiListCell.cs`（行源：模板优先 + 路径回落）、`UI/BackpackForm.cs`（模板=行源 + 先清空再生成，**新列表照这个写**）、`UI/GfUiItemPool.cs`（全局隐藏根）、`UI/UiPrefab.cs`；历史实现：`UI/RoomForm.cs`（玩家列表，行源仍走路径）、`UI/JoinListForm.cs`（房间列表行）。

## 三、当前符合度清单

| 列表 | 容器（预制体） | Cell 模板 | 状态 |
|---|---|---|---|
| 背包·武器槽（BackpackForm） | `WeaponSlots` | `items/WeaponSlot.prefab` | ✅ 模板=行源（UiListCell）+ 池化 |
| 背包·物品槽（BackpackForm） | `GridContainer` | `items/InventorySlot.prefab` | ✅ 模板=行源（UiListCell）+ 池化 |
| 房间玩家列表（RoomForm） | `txt_PlayerList`→运行时 `PlayerListContainer` | `items/PlayerCell.prefab` | ✅ 有模板 + 池化；行源仍走 `items` 路径，待统一到 `UiListCell` |
| 加入房间列表（JoinListForm） | `RoomContainer` | `items/RoomItemRow.prefab` | ✅ 有模板 + 池化 |
| 选角卡片（CharacterDockForm） | `btns` | `items/CharacterCard.prefab` | ⚠️ 有模板；开一次建/关销毁，建议后续池化 |
| 商店商品（ShopForm） | 代码动态行，容器无模板 | ❌ 无 | ❌ 待迁移（`items` 预制体 + 模板 + 池化） |

体检命令：`EmojiWar/Tools/Check UI List Cell Norm`（要求全 `OK`，出现 `MISSING` 即违规）；
补齐命令：`EmojiWar/Tools/Ensure UI List Cell Templates`（按 `UiListNormTools.CellByContainer` 登记表补模板）。

## 四、新增一个列表的标准动作（Checklist）

1. 数据模型与 `CellView.SetContent(...)`（幂等全量赋值）
2. `Assets/GameMain/UI/items/XXX.prefab`（含脚本 + 按名称子物体）
3. `Constant.UIItemAssetPath` 加路径；`GameResourceBuilder.AssetPaths` 加入；重打 AB（**改了 items 预制体/窗体预制体也必须重打 AB**）
4. **宿主窗体预制体：列表容器正下方放一个 `XXX` 预制体实例**（行源模板）；并在 `Editor/UiListNormTools.CellByContainer` 登记 `窗体/容器 → items/XXX.prefab`
5. 宿主窗体代码：`UiListCell.Resolve(...)` 取行源 → `ClearContainer()` 先清空 → `pool.Acquire(() => Instantiate(cellSource), container)` 生成 → 关窗 `ClearContainer()` + `pool.Destroy()` + `UiListCell.Release(ref cellSource)`
6. 探针/回归：打 `[uilist]`/容器 `children` 数量（清空后必须等于行数）、`poolTotal` 只增不减说明在复用
7. 跑 `EmojiWar/Tools/Check UI List Cell Norm`，必须全 `OK`

## 五、违规案例（留档，防止复发）

- **背包首版（2026-09-10）**：`BackpackPrefabBuilder` 生成 `WeaponSlots`/`GridContainer` 时是**空容器**，
  没有在容器下放 Cell 预制体（违反 §一.3 作者态要求）；`BackpackForm` 构建时也**没有先清空容器**，
  作者手放的模板会与生成的行叠加、关窗还留残留。
- **修复**：① `UI/UiListCell.cs`（模板=行源，移出容器 + 显式释放）；② `BackpackForm` 改为
  「取行源 → 先清空再生成 → 关窗清空+释放」；③ `BackpackPrefabBuilder` 生成容器时内置模板；
  ④ 新增 `Editor/UiListNormTools.cs` 体检/补齐工具；⑤ 本规范明确"模板=行源"并补 Checklist。
- **教训**：列表功能不是"能显示就行"——**容器在预制体里就必须带一个 Cell 模板**，且刷新逻辑必须"先清空再生成"。
