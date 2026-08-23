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
- [ ] 自动重连 UI 提示完善
- [ ] 同步观感优化（插值/预测）

### Phase 5 — 打磨与发布 🟢 主体完成（2026-08-23）
- [x] **Windows 打包验证**：StandaloneWindows64 成功（69.95MB，0 错误）
- [x] 同步观感优化：客户端实体位置插值平滑
- [x] **音效接入**：SfxManager 程序合成音效
- [x] **战斗 HUD**：BattleHudForm
- [x] **emoji 美术接入**：ArtManager
- [x] **UI 动效**：UGuiForm 打开缩放淡入
- [x] **内容扩展与平衡**：第 3 把武器（刀光）、Mod 6 种、玩家血量 150、敌人平衡、击杀奖励提升
- [x] **OnKill 效果**：杀戮怒火 Mod 击杀触发临时攻速加成（Projectile→WeaponModComponent.NotifyKill），验证 mul=0.5
- [x] **波次配置数据化**：BattleManager 从 ConfigComponent 读波次参数（Battle.EnemiesPerWave 等）
- [ ] 双实例真机联调（构建 exe 网络对战，需人工在 Unity 外操作）

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
