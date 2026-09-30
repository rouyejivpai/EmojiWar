# ADR-001 数据驱动架构：分层混合（表格 → 导出）+ 统一服务/校验

- 状态：已采纳（2026-09-10）
- 决策人：项目负责人（选定方案 B）
- 关联代码：`Scripts/Data/DataComponent.cs`、`Scripts/Data/ConfigService.cs`、`Scripts/Data/SO/*.cs`、`Scripts/Data/CharacterSelectConfig.cs`

## 1. 背景与现状盘点

**已是 SO（运行时真实数据源）**：`Assets/GameMain/Resources/Data/**`
- `Character/Character_1..4.asset`、`Weapon/Weapon_1..5.asset`、`Mod/Mod_1..8.asset`、
  `Select/CharacterSelect.asset`、`Battle/BattleConfig.asset`
- `DataComponent.Init()` 同步 `Resources.LoadAll<T>()` 装载并建字典索引；`CharacterSelectConfigLoader`
  读 `GameEntry.Data.SelectConfig`。

**已清理的遗留文本（2026-09-10，直接删除）**：
- `Assets/GameMain/DataTables/{Character,Mod,Weapon}.txt`（旧 DataTable 源，运行时早已不读）
- `Assets/GameMain/Resources/Configs/character_select.json`（SO 生成器的输入，且被误打进包造成“双源”）
- `Editor/DataSoGenerator.cs`（依赖上述 txt/json 的旧生成器，随之删除）

## 2. 决策（方案 B：分层混合）

| 数据类别 | 编辑源 | 运行时格式 | 例子 |
|---|---|---|---|
| 展示 / 引用类 | **SO（Inspector 手编）** | SO（Resources/AB） | 选角展示条目、图标、UI 文案、预制体/资产引用 |
| 数值 / 表格类 | **CSV/Excel（表格）** | 由表格导出的 SO 或二进制（进 AB） | 角色数值、武器数值、Mod 效果、波次/平衡、商店价格 |
| 访问层 | —— | **ConfigService 统一入口 + 校验 + 版本哈希** | 所有业务代码只依赖 ConfigService |

理由：
- SO 的强项（类型安全、Inspector 引用、重构友好）保留给“展示/引用类”；
- 表格的强项（批量编辑、评审、Git 文本友好、策划参与）用于“数值类”；
- 统一服务层把“编辑源 → 运行时格式”的差异隔离在数据层，业务代码不感知。

## 3. 统一服务层（已落地 PoC）

`ConfigService`（`Scripts/Data/ConfigService.cs`）：
- **统一访问**：`GetCharacter/ GetWeapon/ GetMod/ GetSelectEntry/ Battle`（内部转 DataComponent 索引，避免业务散落直取）；
- **启动校验**（`Validate`）：id>0、id 唯一、必填字段（名字/图标）、数值范围、跨表引用（SelectConfig 条目 → Character 存在）、BattleConfig 非空；
  问题以 `Debug.LogError` + 探针 `[config] ISSUE:` 输出；无问题输出 `[config] validate OK, hash=0x...`；
- **配置版本哈希**（`VersionHash`）：对关键字段稳定序列化（按 id 排序）后 FNV-1a 64bit；
  启动打印 `[Config] ready: hash=0x... chars= weapons= mods=`；
- 挂接点：`ProcedureLaunch.OnInitResourcesComplete()` → `GameEntry.Data.Init()` 之后调用 `ConfigService.Rebuild()`。

## 4. 后续阶段（roadmap）

- **Phase 2｜联机一致性（建议下一步）**
  - 房间握手时交换 `ConfigService.VersionHash`；不一致 → 拒绝开局并提示“配置版本不一致”；
  - 战斗开始时把 hash 写入探针/对账日志，便于排查“模拟不同步”。
- **Phase 3｜表格管线（方案 B 的“导出”部分）**
  - 目录：`Design/Sheets/*.csv`（或 xlsx 经导出为 csv）；列约定：`id` + 类型注释行 + 数据行；
  - 导出器（Editor 菜单 + 可选 CI 命令行）：`表格 → 校验 → 生成 SO/二进制 → Resources/Data/**`；
  - 校验失败**阻断导出/构建**（复用 ConfigService.Validate 规则集，抽成共享 `ConfigValidation`）；
  - 生成的 SO 打标记“由表格生成，勿手改”（Inspector 只读或加文件头注释）；
  - 兼容热更：如需运行时下发，导出 JSON/二进制而非 SO（方案 B→E 演进），ConfigService 增加 `IConfigSource` 抽象。
- **Phase 4｜多语言 / Mod（按需）**
  - 文案抽 `LocalizationTable`（id → 多语言），SO 只存 key；
  - Mod 覆盖：外部 `ConfigPatch`（json）叠加到基础配置，需带版本与签名校验。

## 5. 约定（团队规范）

1. 业务代码**不再直接** `Resources.Load` 或 `GameEntry.Data.GetXxx`，统一走 `ConfigService`；
2. 新增数值表：先加 CSV + 导出规则，再生 SO；不允许“表格与 SO 各改一半”；
3. 新增展示/引用配置：直接加 SO 资产（放 `Resources/Data/<类别>/`）；
4. 任何配置改动后跑一次启动校验（当前会在启动日志输出 `[config]` 结果），有 ISSUE 必须修复；
5. 联机相关改动上线前核对两端 `VersionHash` 一致。

## 6. 验证记录

- 启动日志：`[Config] ready: hash=0x... chars=4 weapons=5 mods=8`、探针 `[config] validate OK, hash=0x...`；
- 构建版冒烟：房间准备/开战流程正常（见 `doc/流程状态机.md` 场景 A/B 探针）。
