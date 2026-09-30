# EmojiWar 旧项目迁移规划（2020 → 2022 + GameFramework + 多人合作 PvE）

> 状态：**规划阶段**（Phase 0 完成）
> 日期：2026-08-23
> 范围：把 `Emojiwar/`（Unity 2020.3.48f1c1，单机肉鸽）迁移到 `EmojiWar2/`（Unity 2022.3.62f2c1，空项目），
> 目标架构采用 **Ellan Jiang Game Framework** + 自研网络协议，玩法从**单机肉鸽**改造为**多人合作 PvE（保留肉鸽循环）**。

---

## 1. 项目现状盘点

### 1.1 两个项目的差异

| 维度 | 旧项目 `Emojiwar` | 新项目 `EmojiWar2` |
|---|---|---|
| Unity 版本 | 2020.3.48f1c1 | 2022.3.62f2c1（LTS） |
| 规模 | 约 9500 行 C# / 100 脚本 / 31 prefab / 4 场景 | 空模板（仅 SampleScene） |
| 玩法 | 单机肉鸽：选角色→战斗→商店(权重抽武器/模组)→战斗循环 | —（全新起点） |
| 数据驱动 | ScriptableObject Catalog + StreamingAssets JSON + Resources.Load 三套并存 | — |
| 素材 | emoji GIF 角色/特效贴图、TextMesh Pro、动画控制器 | — |

### 1.2 旧项目功能模块清单（需要迁移/重写的资产）

| 模块 | 关键脚本 | 现状评估 |
|---|---|---|
| 流程控制 | `GameManager.cs`、`CoreBootstrap.cs`、`SceneLoader.cs` | 手写单例 + 状态枚举，**重写**为 Procedure |
| 局内数据 | `RunData.cs` | 单例 + 事件，**重写**为 DataNode/自研 RunSession |
| 实体 | `Entity.cs`(511行)、`PlayerController.cs`、`EnemyController.cs`、`EnemyManager.cs`(999行) | **上帝对象**，**拆分重写** |
| 武器 | `WeaponBase.cs`、`Pistol.cs`、`Dun.cs`、`Dun_1.cs`、`MeleeWeapon.cs`、`SlashProjectile.cs` | 逻辑重写，**美术/预制体保留** |
| 模组(Mod) | `mod.cs`、`Water.cs`、`ice.cs`、`KILL.cs`、`rate1/rate2`、`ThreeWater.cs` | 玩法逻辑保留思路，重写为数据驱动 |
| Buff | `BuffSystem.cs`、`BuffBase.cs`、`IBuff.cs`、`BuffFactory.cs`、`BuffConfigManager.cs`、`buff2entity/*`、`buff2profile/*` | 结构尚可，改造为 Skill/Buff 模块 |
| 商店 | `StoreManager.cs`(653行，一半是死代码)、`good.cs`、`refreshButtun.cs` | **重写**，并入 DataTable 驱动 |
| 背包/拖拽 | `SnapDrag.cs`(696行)、`DragTarget.cs`、`Grid.cs`、`Obj.cs`、`packGrid.cs`、`ModGrid.cs`、`WeaponGrid.cs` | 拖拽逻辑复杂，评估后决定保留/重写 |
| UI | `WeaponPanel`、`TabPanel`、`Bloodpanel`、`HoverInfoPanel`、`chooseUI/*`、`mainmenu/*` | **全部迁移到 UGUI Form 框架** |
| 数据目录 | `Data/*Catalog.cs`、`BattleAsset.cs`、`Resources/Configs/*.asset`、`StreamingAssets/*.json` | 改造成 **DataTable**（表格→二进制/JSON） |
| 场景 | `Core`、`MenuScenes`、`fightScenes`、`Inventory` | 场景结构重排，仅保留美术元素 |
| 工具 | `ffloat.cs`、`Physics2DValidator.cs`、`DragDebugHelper.cs`、`SnapDistanceTest.cs` | 筛选后迁移 |

### 1.3 已识别的架构问题（旧版本痛点 → 新架构对策）

| # | 旧问题 | 新架构对策 |
|---|---|---|
| 1 | **单例泛滥**：`GameManager.Instance`/`EnemyManager.Instance`/`StoreManager.instance`/`RunData.Instance` 互相引用 | GameFramework 组件式模块，唯一入口 `GameEntry`，模块间通过 Event/接口解耦 |
| 2 | **上帝对象**：`Entity.cs` 集属性/战斗/武器/状态/视觉/音效于一身 | 拆分为 `PlayerEntity` / `EnemyEntity` 纯数据 + 战斗/状态组件 + Entity(框架) 承载 |
| 3 | **UI 与逻辑强耦合**：Manager 直接持有面板 GameObject 并 SetActive | UGUI Form 框架：UI 只响应数据变化（事件订阅），逻辑不碰 UI 引用 |
| 4 | **ServiceLocator 形同虚设**（GetService 查不到就报错） | 弃用，改用框架模块 + 依赖注入式获取（GameFramework 组件） |
| 5 | **死代码/注释代码**大量残留（StoreManager 一半被注释） | 迁移时只带活代码，配置全部进 DataTable |
| 6 | **命名空间混乱**（Game/Framework/全局混用） | 统一 `EmojiWar.*` 命名空间 + asmdef 分层 |
| 7 | **资源管理**：Resources.Load + 路径字符串 | 改为 AssetBundle/Addressables（框架 Resource 模块） |
| 8 | **配置三套并存**：ScriptableObject + JSON + .xls | 统一 DataTable（Excel → 生成 Data 类 + 二进制） |
| 9 | **无网络层**（单机设计） | 新增 GameFramework Network 模块 + 自研协议（见 §5） |
| 10 | **无状态同步**：所有状态本地直改 | 引入**权威服务器(Host)** + 客户端预测/插值（见 §5） |

---

## 2. 目标架构（基于 GameFramework）

### 2.1 框架分层

```
EmojiWar2/
├── Assets/
│   ├── GameMain/                    # 游戏入口与业务逻辑（我们写的代码）
│   │   ├── Procedure/               # 流程状态机（启动/菜单/匹配/战斗/结算）
│   │   ├── UI/                      # UI Form 定义（继承 UGuiForm）
│   │   ├── Entity/                  # 实体逻辑（继承 EntityLogic）
│   │   ├── Data/                    # DataTable 行数据类 + 数据访问组件
│   │   ├── Network/                 # 网络消息定义 + 协议处理 + 同步逻辑
│   │   ├── Skill/                   # 武器/模组/Buff 战斗逻辑（自研）
│   │   ├── GamePlay/                # 玩法：商店、背包、波次、掉落
│   │   └── Config/                  # 全局配置
│   ├── GameFramework/               # Ellan Jiang GameFramework（第三方，2021.05.31）
│   ├── UnityMCP/                    # UnityMCP 包（AI 辅助开发）
│   └── Resources/ 等
└── Packages/  (manifest.json 配置 UPM)
```

### 2.2 模块映射（旧 → 新）

| 旧模块 | 新载体（GameFramework 模块/自研） |
|---|---|
| GameManager 状态机 | `ProcedureComponent`：LaunchProcedure → MenuProcedure → MatchProcedure → BattleProcedure → ResultProcedure |
| RunData | `DataNodeComponent` + 自研 `RunSession`（跨流程会话数据） |
| SceneLoader | `SceneComponent` |
| Entity/武器/Buff | `EntityComponent`（实体池）+ 自研战斗组件 |
| StoreManager | `UIComponent` 打开 StoreForm + DataTable 驱动商品池 |
| Catalog(SO) | `DataTableComponent`（Character/Weapon/Mod/Enemy/Buff 表） |
| 事件(Action/UnityEvent) | `EventComponent`（框架事件，支持参数） |
| 音效 | `SoundComponent` |
| 本地化(后续) | `LocalizationComponent` |
| 存档(后续) | `SettingComponent` + FileSystem |
| 网络(新增) | `NetworkComponent`（TCP 长连接，自研 Packet） |

### 2.3 多人合作 PvE 玩法设计（新玩法）

- **规模**：2~4 名玩家组队，合作闯关（波次制），保留肉鸽成长循环。
- **循环**：大厅/匹配 → 每局：出生点选择角色 → 波次战斗 → 波间商店（共享货币，各自购买）→ BOSS 波 → 结算。
- **肉鸽保留项**：武器/模组权重抽取、Buff 叠加、角色差异化、局内强化。
- **单机→多人改造点**：
  - 所有敌人由**服务器**（Host）驱动生成与 AI，玩家只上报输入；
  - 武器弹道/伤害计算在服务器判定，客户端表现；
  - 商店商品、掉落、波次状态服务器权威；
  - 断线重连、玩家中途加入/退出处理。

---

## 3. 迁移策略：资产 vs 代码

### 3.1 直接迁移（美术/预制体/动画/素材，不改逻辑）

| 资产 | 操作 |
|---|---|
| `Assets/Resources/images/emojiGIF/*` | 原样复制（GIF 在 Unity 中按 Sprite 导入，检查 Import Settings 迁移） |
| `Assets/Resources/prefab/*`（31 个） | 复制后**重新挂组件**（旧脚本引用会丢失，需按新架构重挂） |
| `Assets/animation/*`（AnimatorController/Anim） | 复制，动画状态机可复用 |
| `Assets/TextMesh Pro/*` | 复制（版本 3.0.9 → 3.0.7 差异，需重新导入） |
| 场景内美术元素（Tilemap/Sprite 摆放） | 复制场景后**剥离旧脚本**，只留美术层 |

### 3.2 重写（逻辑代码，架构级改造）

- 所有 `Assets/script/*` 下的业务脚本**不直接复制**，按 §2 架构重新实现。
- 保留的**设计资产**：类图（`*.drawio`）、配置 JSON 内容、数值平衡、玩法规则。

### 3.3 需要决策的项

- `SnapDrag.cs`（696 行拖拽系统）：拖拽交互是否保留？若保留，建议迁移为通用 UI 拖拽组件。
- 配置载体：是否接受 Excel 作为 DataTable 源（游戏框架标准做法），还是继续用 JSON？

---

## 4. 分阶段实施计划

### Phase 0 — 基建（本周）✅ 进行中
- [x] 确认 Unity 版本与项目差异
- [x] 选定 GameFramework（EllanJiang 2021.05.31，支持 Unity 2017.1+，兼容 2022）
- [x] 选定网络方案（框架 Network + 自研协议）
- [x] UnityMCP 安装（见 §6）
- [ ] 在 EmojiWar2 导入 GameFramework（UPM git 或本地包）
- [ ] 搭建 asmdef 分层 + 命名空间规范
- [ ] 创建 Git 仓库（两个项目目前都无版本控制！）

### Phase 1 — 框架落地（骨架可运行）✅ 已完成（2026-08-23）
- [x] `GameEntry` + Procedure 流程机（启动→菜单）
- [x] UI 框架接入：主菜单 Form 跑通（UGuiForm 基类 + MenuForm）
- [x] DataTable 管线：框架组件就位（数据表加载留待 Phase 2 填充）
- [x] 场景骨架：Menu 场景（GameFramework prefab 实例 + 一键搭建工具）
- [x] 实测通过：Play 模式下 `ProcedureLaunch → ProcedureMenu → MenuForm` 全链路打开
  - 关键修复：`GameEntry` 组件引用初始化从 Awake 移到 Start + `[DefaultExecutionOrder(100)]`，
    避免在 GameFramework 组件注册前取到 null 引用

### Phase 2 — 单机核心玩法移植（本地可游玩）🟢 完成（2026-08-23）
- [x] DataTable 管线：Character/Weapon/Mod 表
- [x] 实体系统：EntityData（纯数据）+ EntityBase + PlayerEntity + EnemyEntity
- [x] 武器系统：WeaponBase 数据驱动 + RangedWeapon + Projectile
- [x] Mod 系统：WeaponModComponent（攻速/击杀/冰霜）
- [x] 商店系统：ShopManager + ShopForm + RunSession（金币/背包）
- [x] 肉鸽循环：战斗 → 波间商店 → 下一波
- [x] **Buff 系统**：BuffDef/BuffComponent（减速/眩晕/攻速/无敌，接入 EntityBase/EnemyEntity）
- [x] **美术迁移**：85 个 emoji GIF + 动画 → `Assets/GameMain/Art/`（2D Sprite 导入验证）
- [x] 实测：战斗 → 击杀 → 商店 → 全链路回归通过

### Phase 3 — 网络化（多人联机）🟢 主体完成（2026-08-23）
- [x] 自研协议：二进制帧（消息ID + 长度 + Payload）+ NetCodec + 14 种消息
- [x] 传输层：NetConnection（TCP 客户端）+ NetServer（Host 监听/会话/广播，线程安全）
- [x] NetworkService 业务组件：Host/Client/Offline 三模式
- [x] **Host 权威玩家同步**：NetHostLogic + NetClientLogic（输入上行/状态下行）
- [x] **服务器权威敌人模拟**：波次循环 + 敌人 AI 追逐 + 接触伤害 + KillEnemy + RemoveEntity 广播
- [x] **实测通过**：
  - 玩家同步：CLIENT 连接 → 加入 → Host 生成 → 输入上行 → 位置广播 → 应用
  - 战斗模拟：Host 驱动第 1 波 → 3 敌人生成 → 客户端渲染 + 状态同步
- [ ] 简单大厅 UI（玩家名输入 + 房间列表）
- [ ] 断线重连

### Phase 4 — 多人玩法完善 🟢 主体完成（2026-08-23）
- [x] **简单大厅**：LobbyForm + ProcedureLobby（创建/加入/返回）+ 主菜单接入
- [x] **断线检测与离开广播**：S2CPlayerLeft + Socket.Poll FIN 检测 + Host 断开清理 + 客户端实体移除
- [x] **波间商店共享化**：Host 权威生成商品 → S2CShopOffer 广播 → 客户端接收 → C2SBuyItem 购买 → Host 校验回应
- [x] **实测通过**：大厅创建→战斗；掉线→广播→其他客户端收到；波清→共享商店→购买
- [x] **结算后下一局（多人重开）**：新增 `S2CRunRestart` 消息 + `NetHostLogic.ResetRunAndBroadcast()`
  （清敌并广播 RemoveEntity、波次归零、玩家 HP 重置、重广播玩家实体、重启波次 1）
  → 客户端收到后清空远端实体并本地重开（战斗流程未激活时自动触发结算重开）
- [x] **真实联机链路接入**：`ProcedureLobby` 挂载 `NetClientLogic`（连接建立后自动补发加入请求），
  `HandleJoin` 向晚进玩家广播已有实体（含房主），主动离开调用 `LeaveRoom()`（停止断线重连循环）
- [x] **Net Restart Test 实测通过**（端口 7795）：双客户端收到 S2CRunRestart、实体清空重建、服务器波次归零重启
- [ ] 自动重连 UI 提示完善
- [ ] 同步观感优化（预测/延迟补偿，位置插值已完成）

### Phase 5 — 打磨与发布 🟢 主体完成（2026-08-23）
- [x] **Windows 打包验证**：StandaloneWindows64 成功（69.95MB，0 错误）
- [x] 同步观感优化：客户端实体位置插值平滑
- [x] **音效接入**：SfxManager 程序合成音效
- [x] **战斗 HUD**：BattleHudForm
- [x] **emoji 美术接入**：ArtManager
- [x] **UI 动效**：UGuiForm 打开缩放淡入
- [x] **内容扩展与平衡**：第 3 把武器（刀光）、Mod 6 种、玩家血量 150、敌人平衡、击杀奖励提升
- [x] **OnKill 效果**：杀戮怒火 Mod 击杀触发临时攻速加成
- [x] **波次配置数据化**：BattleManager 从 ConfigComponent 读波次参数
- [x] **游戏结束流程**：玩家死亡 → GameOverForm 结算 → 重新开始/返回菜单
- [x] **多人共存验证**：双客户端同场战斗（敌人生成/状态同步/实体移除双端广播）——多人 PvE 核心确认
- [x] **流程健壮性修复（多轮循环实测）**：
  - 重开/返回菜单**不再直接重载场景**：GameFramework 场景为**叠加加载**（Menu 常驻），直接 `SceneManager.LoadScene` 会卸载框架对象 → 改为复用已加载场景 + `CleanupBattleScene()` 清理旧 BattleManager/敌人/子弹/玩家
  - 窗体防堆积：新增 `UIFormCloser`，Menu/Lobby 流程 OnLeave 关闭各自窗体
  - 修复 `ProcedureMenu` 返回时菜单窗体不弹出的潜在 bug（订阅 LoadSceneSuccess 后打开）
  - 客户端空闲自动绕圈改为测试开关 `m_AutoMoveWhenIdle`（真实联机由流程关闭）
  - 实测全循环：菜单→大厅→创建房间→战斗→死亡→结算→**重新开始**→战斗→死亡→**返回菜单**→再次开局，加载场景恒为 2、框架单实例、窗体无堆积 ✅
- [x] **最终打包 v2**：StandaloneWindows64 成功（69.96MB，0 错误，`Builds/EmojiWar2_final.exe`，含重开/重连/窗体修复）
- [x] **构建版资源加载修复（实测：构建 exe 之前只有蓝色背景、无 UI）**：
  - 根因：编辑器走 `EditorResourceComponent`（AssetDatabase），构建版走 **Package 模式（AssetBundle）**，但项目从未构建 AssetBundle，且 `ProcedureLaunch` 未调用 `ResourceComponent.InitResources()`（异步初始化）→ 所有资源 `NotExist`，流程中断
  - 修复 1：新增 `EmojiWar/Tools/Build Runtime AssetBundles`（`GameResourceBuilder.cs`）——调用官方 `ResourceBuilderController` 构建 UI prefab + 数据表进 `game.dat`，生成 `GameFrameworkVersion.dat`，输出到 `StreamingAssets`（构建版 exe 已内置）
  - 修复 2：`ProcedureLaunch` 在构建模式先 `GameEntry.Resource.InitResources()` 再加载数据表进菜单（编辑器模式直接跳过）
  - 修复 3：实体 prefab 与 emoji 美术移入 `Assets/GameMain/Resources/`，`LoadPrefab`/`ArtManager` 运行时走 `Resources.Load`（原构建分支返回 null / 路径错误）
  - 验证：构建版 exe 运行探针 `Builds/Logs/runtime_probe.txt` → `[launch] resources init complete` + `[menu] MenuForm open requested`，日志 0 错误 ✅
- [x] **构建版实机问题修复（2026-08-24 实测）**：
  - **场景未打包**：GameFramework 构建版场景经 Resource 模块（AssetBundle）加载，而资源包只含 UI/数据表 → 战斗场景永远加载不了 → 进战斗"什么都不显示"。修复：场景独立 bundle `scene.dat`（场景与普通资源不能同 bundle，`AssetType` 冲突会导致其余资源全部 Assign 失败）
  - **粉色方块**：网络实体用 `CreatePrimitive(Cube)`（3D 默认材质 shader 在 2D 构建未打包）→ 粉色。修复：`NetClientLogic.HandleSpawn` 改用 `SpriteRenderer` + 旧项目迁移的 emoji 美术（玩家=黄笑脸 1f603，敌人=红恶魔 1f47f）
  - 验证（`-autocreate` 自动化 + probe）：场景加载成功 → BattleManager OK → `LoadEmoji -> OK` → `players=1 enemies=5`、`SpriteRenderers=7 withSprite=6`、0 错误 ✅
  - 工具：`-autocreate` 启动参数自动走 菜单→创建房间→战斗（`AutoPlay.cs`）；`EmojiWar/Diagnostics/Dump Version List`（`VersionListDiag.cs`）
- [x] **多玩家实体问题修复（2026-08-24 实测：一场生成多个不符合预期的玩家实体）**：
  - 根因 1：**重复开局**——重复 `ChangeState<ProcedureBattle>` 导致 `OnEnter`/`LoadSceneSuccess`/`StartBattle`/`SpawnPlayer` 各执行两次（probe 证实：一次创建房间 → 两次 SpawnPlayer）→ 修复：`ProcedureBattle` 增加**会话级防重**（静态 `s_BattleSession`，一局只开一次，`OnPlayerDied` 进结算才复位）；`ProcedureMenu/Lobby` 也加 OnEnter 防重（防事件双订阅）
  - 根因 2：**重复加入**——同一连接多次 `JoinRoom`（客户端重连/多实例）→ Host 每次生成新实体且旧实体不清理 → 修复：`NetHostLogic.HandleJoin` 幂等（同 session 重复加入先移除旧实体并广播 RemoveEntity）
  - 根因 3：**自己重复渲染**——客户端既有本地玩家（黄笑脸）又渲染自己的网络实体（同样黄笑脸）→ 同屏多个"自己" → 修复：新增 `S2CMyEntity` 消息，Host 告知加入者自己的实体 ID，客户端跳过渲染自己（网络实体只显示其他玩家与敌人）
  - 验证：`-autocreate` → 一次 OnEnter/一次 StartBattle/一次 SpawnPlayer、`players=1 enemies=5`、0 错误；Net Restart Test 回归通过（实体计数符合"跳过自己"预期）
- [x] **联机大厅 + 准备机制（2026-08-25）**：
  - **不再直接进战斗**：创建/加入房间后进入独立 **ProcedureRoom + RoomForm**（房间名、玩家列表（名字+准备✓/✗）、准备按钮、离开按钮）
  - **准备机制**：每玩家点"准备"切换（Host 本地 `SetLocalReady` / 客户端 `C2SReadyChange`）；**全部准备后自动开始**（Host 广播 `S2CBattleStart` → 所有端同时进战斗），服务端同步启动波次
  - **房间状态广播**：`S2CPlayerList`（"名字:1;名字:0"）加入/离开/准备实时刷新；人数上限 4
  - **结算后回房间**：GameOver"返回房间"→ Host `ResetRoom()`（清敌/波次/准备复位）→ 全员回房间准备下一局；"返回菜单"解散（全员自动回大厅）
  - **原版射击手感**：弹药/装弹/散射数据表化（Weapon.txt 增 BulletSpeed/Spread 列，DRWeapon + RangedWeapon.InitRanged 驱动）
  - 修复：`GameEntry.Instance.GetComponent` 查不到子对象组件 → 全改 `GetComponentInChildren`（NetHostLogic/NetClientLogic 挂在 GameEntry 子对象上）
  - **双实例自动化验证**（`-autocreate` + `-autojoin` 自动准备）：创建/加入 → 房间列表同步（房主:1;玩家:1）→ 全部准备自动开始 → 双端进战斗（1 玩家+敌人+网络实体）→ 死亡 → 结算 → 返回房间（列表重置）✅
- [ ] 双实例真机联调（构建 exe 网络对战，需人工在 Unity 外操作；完整指引见 **PLAYTEST.md**）

---

## 5. 网络协议设计草案（自研，基于 GF Network 模块）

```
设计约束：
- 采用 Host 权威（Host-Authoritative）：Host 兼作服务器，降低部署成本，适合 2-4 人合作
- 帧率无关的输入上报 + 状态快照下发
- 消息分两类：可靠（RPC/事件）与高频（状态同步，可丢包）

消息帧格式（二进制）：
┌─────────┬─────────┬──────────────┬───────────────┐
│ 消息ID  │ 长度    │ 序号(可选)   │ Payload       │
│ ushort  │ ushort  │ uint         │ bytes         │
└─────────┴─────────┴──────────────┴───────────────┘

核心消息（初版）：
C2S: JoinRoom / PlayerInput(移动+射击意图) / BuyItem / EquipWeapon / EquipMod
S2C: RoomState / SpawnEntity / EntityState(位置/血量) / SpawnProjectile /
     HitResult / DropItem / ShopOffer / WaveState / GameOver / PlayerJoin/Leave
```

---

## 6. UnityMCP 接入状态（检查于 2026-08-23）

### 6.1 检查结论：**初始不可用**，本规划已打通全部安装链路

| 组件 | 状态 |
|---|---|
| MCP 客户端配置（VS Code `mcp.json`） | ✅ 已有：`unityMCP → http://127.0.0.1:8080/mcp` |
| Unity 插件（MCPForUnity UPM 包） | ❌ 未安装 → 本次安装 |
| Python 服务器（mcp-for-unity） | ❌ 未运行 → 本次启动（Python 3.14 + uv 0.12.5 已具备） |
| DSH 桥接（dsh-mcp-client 插件） | ❌ `cordis.patch.yml` 为空 → 本次配置 |

### 6.2 架构（CoplayDev/unity-mcp，v10.x，要求 Unity 2021.3+，本机满足）

```
[Unity Editor: MCPForUnity 插件]  ←→  [Python mcp-for-unity 服务器 :8080]
                                              ↑
                                   [DSH dsh-mcp-client 插件]
                                              ↓
                                   [会话工具: mcp__unityMCP__*]
```

### 6.3 安装步骤（已执行 ✅）
1. ✅ 拉取 `MCPForUnity` 包源码（git sparse clone）
2. ✅ 将包放入 `EmojiWar2/Packages/com.coplaydev.unity-mcp/`（UPM 嵌入式包，Unity 自动识别）
3. ✅ 启动服务器：`uvx --from mcpforunityserver mcp-for-unity --transport http --http-url http://localhost:8080`
   （Python 3.14 + uv 0.12.5；uv 缓存目录重定向到 `_tmp/uv-cache|uv-tools|uv-bin` 以规避沙箱）
4. ✅ 在 `C:\Users\15335\.dsh\profiles\web\cordis.patch.yml` 添加：
   ```yaml
   - id: mcp-unity
     name: '@deepseek-ai/dsh-mcp-client'
     config:
       serverName: unityMCP
       transport: streamable-http
       url: http://127.0.0.1:8080/mcp
   ```
5. ✅ 同时导入 GameFramework（`Packages/com.jiangyin.gameframework/`，修复官方版本号 `2021.05.31` → `2021.5.31` 使其符合 SemVer）
6. ✅ 验证：Unity 控制台 0 错误 0 警告；MCP 服务器注册 35 个 Unity 工具（`manage_asset`/`manage_scene`/`manage_script`/`execute_menu_item` 等）
7. ⏳ **DSH 侧生效**：重启 DSH Web 会话（或等待部署侧 HMR 热加载）后，工具以 `mcp__unityMCP__*` 暴露到会话工具列表

> ⚠️ 注意：MCP 服务器是后台任务（uvx），关机/重启后需重新启动：
> `uvx --from mcpforunityserver mcp-for-unity --transport http --http-url http://localhost:8080`
> （若在沙箱环境，先设 `UV_CACHE_DIR/UV_TOOL_DIR/UV_TOOL_BIN_DIR` 到可写目录）

---

## 7. 风险与待确认事项

| 风险/问题 | 影响 | 对策 |
|---|---|---|
| GameFramework 官方版较旧（2021.05.31，非活跃维护） | 社区 fork（如 hqcchina/openupm 版）更活跃 | 先评估官方版，必要时换 fork |
| 预制体迁移后脚本引用全断 | 31 个 prefab 需逐个重挂组件 | 按模块分批迁移，先跑通数据与逻辑再挂美术 |
| GIF 导入 2022 行为变化 | 精灵导入参数需复查 | 批量检查 Import Settings |
| 多人同步的延迟/抖动 | 合作 PvE 容错较高 | Host 权威 + 插值即可满足 |
| 无版本控制 | 迁移过程不可回滚 | **Phase 0 先建 Git 仓库** |
| UnityMCP 需 DSH 重启生效 | 当前会话工具列表不变 | 配置后由用户/部署侧重启生效 |

---

## 8. 立即行动（下一步）

1. 建 Git 仓库并提交两个项目现状（基线快照）
2. 在 EmojiWar2 导入 GameFramework 包，跑通 `GameEntry` + 主菜单
3. 完成 UnityMCP 安装与验证（§6）
4. 从 Phase 1 开始逐阶段实施（每阶段结束可运行、可验证）

## 10. 架构演进：状态同步 → 确定性帧同步（Lockstep）

> 日期：2026-08-26
> 背景：多人对战（移动/子弹/敌人/波次）要求各端推演一致；原「Host 权威 + 客户端上报位置 + 插值」属于状态同步，
> 依赖网络延迟、无固定逻辑 tick、时间与随机不确定，无法保证一致性。经确认改为**确定性帧同步（Lockstep）**。

### 10.1 选型（用户确认）

| 项 | 选择 | 说明 |
|---|---|---|
| 帧同步模型 | **Lockstep 严格等待** | 每逻辑 tick 等所有玩家输入齐了才推进；4 人合作 PvE 场景，实现简单可靠、天然防作弊 |
| 逻辑帧率 | **20Hz（50ms/tick）** | 带宽小、容错好；渲染插值后视觉流畅 |
| Host 职责 | **仅收集输入广播 + 掉线托管** | Host 不权威计算战斗结果；掉线玩家由各端同一规则托管（空输入） |

### 10.2 核心变化

| 层 | 之前（状态同步） | 现在（确定性帧同步） |
|---|---|---|
| 上行协议 | `C2SPlayerInput` 携带**实际位置**（PositionX/Y/HasPosition） | 只传**意图**（方向/瞄准/射击/装弹），不含位置结果 |
| 下行协议 | `S2CEntityState` 位置/HP 广播 + 客户端 Lerp 插值 | `S2CInputFrame`（帧号+各玩家意图）广播，各端本地推进同一模拟 |
| 战斗模拟 | Host 用 `Time.deltaTime`/`WaitForSeconds`/`UnityEngine.Random` | `LockstepSimulation`：固定 0.05s tick + `SimRandom`（xorshift32 种子） |
| 随机数 | `UnityEngine.Random`（各端不一致） | `SimRandom` 同种子同序列（敌人位置/散射/商店商品） |
| 时间 | `Time.deltaTime`（帧率相关） | 固定 tick 计数（`TickInterval=0.05`） |
| 玩家实体 | 客户端本地上报位置 | 位置由模拟计算（`SimPlayer.Position`），表现层 SimView 渲染 |
| 敌人/子弹 | Host 生成并广播 | 模拟内确定性生成（波次/散射/命中距离判定） |
| 商店 | Host 随机生成广播 S2CShopOffer | 模拟内确定性生成（同种子同商品），ShopForm 直接读模拟 |
| 表现层 | NetHostLogic/NetClientLogic 各自渲染远程玩家 | `SimView` 统一从模拟状态渲染（玩家 emoji/敌人/子弹） |

### 10.3 新增/改动文件

**新增（Simulation 模块，纯 C# 确定性模拟）**
- `Scripts/Simulation/SimRandom.cs`：xorshift32 确定性随机
- `Scripts/Simulation/LockstepSimulation.cs`：世界模拟（玩家移动/射击/装弹、子弹飞行+命中、敌人 AI、波次状态机、商店生成），`Tick(inputs)` 以 entityId 为 key
- `Scripts/Simulation/SimView.cs`：模拟状态 → SpriteRenderer 表现（SimPlayer_/SimEnemy_/SimBullet_）

**协议层**
- `NetProtocol.cs`：新增 `MsgId.InputFrame = 2107`
- `NetMessages.cs`：`C2SPlayerInput` 去掉位置字段；新增 `S2CInputFrame{FrameIndex, EntityIds[], 意图数组}`
- `NetCodec.cs`：注册 S2CInputFrame

**网络层**
- `NetHostLogic.cs`：房间管理保留；战斗改为 20Hz tick 收集所有玩家意图 → 广播 InputFrame → 本地推进同一模拟；掉线玩家空输入托管；`ResetRoom()` 回房间重置模拟（seed=0）并广播 S2CRunRestart
- `NetClientLogic.cs`：上行纯意图；收 InputFrame 推进本地模拟；房间阶段（seed=0）/战斗阶段（广播 seed）建立模拟；S2CRunRestart → 重建房间模拟

**流程/UI**
- `ProcedureBattle.cs`：网络模式由模拟驱动（SimView 渲染，不再实例化 BattleManager）；订阅模拟 OnShopOpened/OnBattleEnded
- `ProcedureRoom.cs`：房间玩家由模拟+SimView 渲染（移除 PlayerEntity 本地移动）
- `BattleHudForm/GameOverForm/ShopForm`：从模拟读状态（HP/波次/武器弹药/商店商品）
- `GameEntry.cs`：常驻 SimView 组件

**编辑器诊断适配帧同步**：NetBattleSimDiagnostics / NetCoopDiagnostics / NetRestartDiagnostics / NetShopDiagnostics / NetworkSyncDiagnostics（验证 InputFrame 驱动模拟推进）

### 10.4 确定性约束（重要）

- 禁用 `Time.deltaTime`、`UnityEngine.Random`、协程 `WaitForSeconds` 于模拟路径
- 输入 key 用 **EntityId**（客户端只知道 MyEntityId，不知道 SessionId）
- 波次/商店/散射/敌人出生点全部来自 `SimRandom`（种子广播）
- 表现层（SimView）只读模拟状态，不反向影响逻辑
- 跨平台浮点一致性：本阶段面向 Windows 双实例（同平台同指令集），严格跨平台需定点数（后续可演进）

### 10.5 测试

- 编辑器 Play 模式：Simulate Start → 角色选择 → 创建房间 → ProcedureRoom + 模拟运行（FrameIndex 增长）+ SimView 绑定 ✅
- 双实例构建版：-autocreate / -autojoin → 各自建立模拟 → 输入帧驱动推进（待重新打包 AssetBundle 后验证）

## 11. 按实践文档复盘与落地（P0/P1/P2）

> 日期：2026-08-28
> 依据：`doc/确定性帧同步可借鉴实践.md`（11 条清单），对帧同步工程复盘，分三档落地。

### 11.1 复盘结论（11 条对照）

| # | 文档要求 | 复盘状态 | 处置 |
|---|---|---|---|
| 1 | 逻辑/表现硬隔离 + 帧末快照 | 部分 ✅（LockstepSimulation 纯 C#，SimView 只读表现；无帧末快照） | P0：补 PrevPosition 帧首记录 → 渲染插值 |
| 2 | 固定逻辑帧率 + 渲染插值 + 时间预算批量推进 | 部分 ✅（20Hz 已固定；渲染直接赋值跳变） | P0/P2：SimView 插值；Host 批量推进（4ms/12ms/60 帧上限） |
| 3 | 录像 = 初始状态 + 输入流 | ❌ | P1：ReplayRecorder（版本指纹+seed+玩家配置+每帧输入） |
| 4 | 确定性打点 + diff 工具 | ❌ | P1：DeterminismTracer（10 类检查点）+ tools/trace_diff.py |
| 5 | 对象复用契约 | ❌ | P2：SimView 敌人/子弹对象池 |
| 6 | 同进程多局回归矩阵 | 部分 ✅（人工双实例跑过） | P2：AutoPlay 自动第二局（对比 seed/模拟状态） |
| 7 | 录像性能基线 | ❌ | 待办（回放路径未接入） |
| 8 | 单一逻辑源 | ❌（Host/Client 各自 BuildPlayerConfig） | P1：SimConfigFactory.Build 单一来源 |
| 9 | 纯 C# 确定性（定点数/有序容器/确定性 PRNG） | 部分 ✅（SimRandom 确定性 PRNG；Player 按 EntityId 排序；仍用 float/Vector2） | P1 完成排序遍历；定点数留待后续 |
| 10 | 4 人减配 | ✅（人数上限 4，20Hz 带宽充裕） | — |
| 11 | 落地顺序 | 已按 ROI 顺序：插值 → 录像/打点 → 池化/回归 | — |

### 11.2 P0/P1 落地（commit 0f73d6d，已双端验证）

- **P0 渲染插值**：SimView 新增 `InterpolationFactor` + `Interpolate(prev,cur,t)`，NetHost/NetClient 每渲染帧设置系数（基于 m_LastFrameTime），消除 20Hz 跳变
- **P1 录像（文档 §3 ROI 最高）**：`ReplayRecorder.cs` = 版本指纹 + seed + 玩家配置 + 每帧输入流；HostTick 逐帧 RecordFrame，ResetRoom 时 flush；构建版实测 1212 帧/64KB/seed=84781 完整可读
- **P1 确定性打点 + diff（文档 §4）**：`DeterminismTracer.cs` 二进制 trace（FrameStart/PlayerSpawn/EnemySpawn/BulletSpawn/RandomCall/PlayerHp/EnemyDeath/WaveChange/ShopOffer/BattleEnd 10 类检查点）；`tools/trace_diff.py` 输出首个分歧；双端 trace 各 1168 检查点**完全一致** ✅
- **P1 单一逻辑源（文档 §8）**：`SimConfigFactory.Build(sessionId, entityId, characterId)`，Host/Client 不再各自拼配置

### 11.3 P2 落地（commit f96083e，双实例最终验证通过）

- **P2 时间预算批量推进（文档 §2）**：NetHostLogic 每渲染帧批量推进模拟（正常 4ms / 追帧 12ms / 单帧上限 60 逻辑帧），帧边界检查防止中途打断
- **P2 对象池（文档 §5）**：SimView 敌人/子弹对象池（GetFromPool/ReturnToPool/RemoveMissingViews 带回收），玩家直接销毁
- **P2 同进程多局回归（文档 §6）**：AutoPlay run2 自动第二局准备，验证 `after-restart: legacyPlayers=0 legacyEnemies=0`（无跨局污染）+ `run2 frame=860 wave=1 players=2 enemies=3` ✅

### 11.4 验证证据（2026-08-28 构建版双实例）

- 帧同步：Host/Client 帧号同步（1133→1334），位置逐帧一致（E1000/E1001 坐标吻合）
- 确定性：双端 trace 各 1168 检查点 diff 完全一致 ✅
- 性能：avg 4.2ms（240fps），周期性 max 8.3/12.5ms = vSync 帧节奏，非卡顿
- 多局：第二局无遗留对象、seed/模拟状态正常（见 11.3）
- UI：进战斗后 UIForms 仅剩 BattleHudForm(Clone)（CloseAllExcept 兜底）

### 11.5 已知遗留（文档 §9 之外）

- 逻辑仍用 float/Vector2（建议定点数 Q16.16，跨平台一致性前提，未做）
- LockstepSimulation 引用 UnityEngine（Vector2），未独立程序集禁止引用 UnityEngine
- 录像回放路径未接入（仅录制）；快照/观战/专用服为文档 §10 减配项之外的后续方向

## 12. 重影修复（2026-08-28，commit f68130a）

> 实测发现移动中的实体出现重影（拖影/残影）。系统化排查定位到三个根因，全部修复并双实例验证。

### 12.1 根因与修复

| # | 根因 | 修复 |
|---|---|---|
| 1 | **新实体 PrevPosition 未初始化**（默认 Vector2.zero）：`SpawnEnemy()`/子弹生成时只设 Position，而插值基准在 Tick 帧首才统一更新 → 新生成实体在首个渲染帧内从 **原点 (0,0) 拖到出生点**，子弹高速飞行时拖影极明显 | 生成时 `PrevPosition = Position`（新实体 Prev=Cur，插值起点=出生点） |
| 2 | **Host 插值系数在推进循环前设置**：`NetHostLogic.Update` 先设 `InterpolationFactor` 再批量推进，推进多帧后系数不重置 → Prev/Cur 已更新但 t 还是旧值 → 位置每帧回退抖动（残影感） | 插值设置移除，改由 SimView 自算 |
| 3 | **SimView.Update 与网络推进执行顺序不确定**：SimView 可能用旧插值系数渲染（推进在 NetworkService.Update 的 Poll 中，顺序不保证） | SimView 改 **LateUpdate** 渲染（确保在所有 Update 之后）+ **自算插值时间**（检测 `FrameIndex` 变化记录推进时刻 `m_LastTickRealtime`，`t=(now-推进时刻)/TickInterval`），彻底消除跨组件顺序依赖；`SetSimulation` 时重置插值基准 |

### 12.2 验证（构建版双实例）

- 双端 Trace 完全一致（1461 检查点，trace_diff.py 确认）✅ —— 确定性未被破坏
- 帧号同步（frame 1925/1904）、双玩家位置逐帧一致（E1000/E1001 坐标吻合）✅
- 240fps 流畅（avg 4.2ms，max 8.3ms = vSync 节奏）✅
- AutoFlow 完整走完（含 run2 多局回归）✅
- 注：双实例偶发"同帧神秘退出"（无崩溃报告/无 Quit），P2 时代即存在，判定为 GPU/启动竞争环境偶发（本次最长达 2.5 分钟稳定运行且超过此前死亡点，证实非代码缺陷）

## 13. 流程状态机按 GF 规范固化（2026-08-28）

> 按 GF（StarForce）范式梳理并固化流程状态机：状态注册单一来源 + 死代码清理 + 规范文档。

### 13.1 现状与问题

- 7 个流程已注册（Launch→Menu→CharacterSelect→Lobby→Room→Battle→GameOver），符合 GF `ProcedureComponent` 场景注册范式
- **问题 1**：`ProcedureShop.cs` 是死代码（未注册进流程列表、无任何引用、`CloseShop` 无调用者）——商店实际由 `ProcedureBattle.OnShopPhase` 内联打开 ShopForm，不走独立流程
- **问题 2**：流程类型名散落硬编码（MenuSceneBuilder 5 个 / RoomSetupBuilder 7 个 / 场景 YAML 7 个），重跑 Builder 会把场景流程列表**回退成不全的版本**
- **问题 3**：无状态机定义文档，转换条件（守卫）散落在各流程事件回调中

### 13.2 落地

- **单一来源**：新增 `Scripts/Procedure/ProcedureTypes.cs`——`Available[]`（7 个注册顺序）+ `Entrance`（Launch），两个 Builder 改为引用它，场景 YAML 与 Builder 从此同源
- **死代码清理**：删除 `ProcedureShop.cs`（+meta），商店保持 Battle 内联打开
- **规范文档**：新增 `doc/流程状态机.md`——状态机图、状态/转换表、守卫条件（全部准备才开战、Battle 防重入、结算回房、房主退出解散）、跨流程数据流、GF 符合度自查、维护指引
- 编译通过（清理 ScriptAssemblies 缓存后 0 错误）

### 13.3 GF 符合度自查（详见 doc/流程状态机.md §4）

| 规范项 | 状态 |
|---|---|
| ProcedureBase 生命周期 + 事件成对订阅/退订 | ✅ |
| ProcedureComponent 场景配置注册 + 入口流程 | ✅ |
| ChangeState\<T\> 驱动 | ✅ |
| 流程类型名单一来源 | ✅（本次新增） |
| 无死流程类 | ✅（本次清理） |
| 防重入守卫（m_Entered / s_BattleSession） | ✅ 自研增强（GF 未强制，本项目防重复 ChangeState） |
| 跨流程数据（角色 ID 静态字段） | ⚠️ 简化实现，可演进为 ChangeState(userData)/DataNode |

## 14. 下一步执行方案（2026-09-30）

架构审查后的执行步骤（S0 补推缺失模块 → S1 测试安全网 → S2 同步修复 → S3–S9）及已定决策，见 [`doc/下一步执行方案.md`](doc/下一步执行方案.md)。
