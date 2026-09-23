# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目定位

让 DeepSeek 驱动的 Agent 在真实星露谷（Stardew Valley 1.6.15 + SMAPI 4.5.2）里**同时控制玩家本体和一个同行 NPC**，自主经营农场、正常睡觉换日，目标是完整通关。不使用视觉模型、向量库或截图；所有世界状态从 SMAPI 直接读取，所有动作走原生交互与原生结算。

当前可运行形态是 C# Mod「同行 · Together 0.7」（`mod/Together`）。早期的 Python 决策原型（`agent/`）与 Farmtronics 验证底座（`mod/FarmtronicsAdapter`）保留，但不是当前主线。

## 常用命令

```bash
# 构建：把固定版本的 Squad 上游 + 本项目 Mod 编译进隔离的 work/CompanionMods
python3 scripts/build_companion.py

# 启动隔离运行时（--lab 才开放 AgentLab 夹具接口）
python3 scripts/launch.py --companion --lab

# C# 域检查（纯函数/契约层，不需要游戏）
work/dotnet/dotnet run --project tests/Together.DomainChecks.csproj

# Python 单元测试
python3 -m unittest discover -s tests -v
python3 -m unittest tests.test_navigation -v          # 单个模块

# 打包体验版
python3 scripts/package_together.py
```

游戏内集成检查（需要先 `launch.py --lab` 并载入 AgentLab 存档，另开终端运行）：

```bash
python3 scripts/check_operations_kernel.py      # 单项原生检查
python3 scripts/check_autoplay_suite.py         # 成组回归，收齐全部结果再修
python3 evals/companion_control.py              # 关闭面板、停掉任务后运行
```

`scripts/check_*.py` 是**原生行为检查**（在真实游戏里跑夹具），`tests/*.cs` 是**域检查**（离线纯逻辑），两者不能互相替代。环境变量：`DOTNET` 指定 SDK，`STARDEW_GAME_PATH` 指定含游戏 DLL 的目录；模型 Key 只放被忽略的 `.env`。

## 架构

四层，自下而上：

1. **上游 Squad（NPC 行动执行器）** — `external/the-stardew-squad`，固定 commit 记录在 `configs/companion-upstreams.json`。源码不进 Git。
2. **适配层** — `mod/SquadAdapter/*.cs`。构建时被复制进 `work/build/Squad` 暂存目录，与上游一起编译。
3. **桥接层** — `mod/AgentBridge`，本地 HTTP（默认 127.0.0.1:18765，Bearer token 写入 `.local.json`）。Python 侧用 `agent/client.py` 的 `Bridge` 调用。仅用于开发/测试，正式游玩不依赖。
4. **主体 Mod** — `mod/Together`，193 个文件几乎全是 `ModEntry` 的 `partial` 扩展，按业务域拆分而非按层拆分。

### 上游改造方式（重要）

`scripts/build_companion.py` 用 `replace_once()` 对上游源码做**精确锚点字符串替换**，锚点出现次数必须恰好为 1，否则构建直接失败（`Upstream anchor changed`）。构建前还会比对上游 HEAD 与 `configs/companion-upstreams.json` 里的审计 commit。所有改造发生在 `work/build/Squad` 暂存副本上，上游克隆保持干净。

改动 Squad 行为时改的是 `build_companion.py` 里的锚点补丁 + `mod/SquadAdapter/`，不是 `external/`。

### 模型决策循环

`AutoplayRuntime.cs` 的 `TickAutoplay()` 是主循环，每帧依次推进 `playerExecutor` → 语义任务 → 调度队列 → 日常自动化 → 经营 → 投资 → 清理 → 目标 → 遥测，最后才考虑是否请求模型。

关键设计：

- **模型只做取舍，不做逐格调度。** 具体劳动交给 `SemanticWork.cs`（`work.run`）和 `PlayerExecutor.cs`，它们自己选点、寻路、选工具、补给、续作。
- **唤醒式请求。** `WakeAgent(reason)` 累积唤醒原因；`AgentDecisionPacing` + `AgentPollingPolicy` 决定能否跳过本轮付费请求。队列有活干时不轮询模型。
- **陈旧决策作废。** 模型异步回复落地前检查 `agentGeneration`、日期和 `DecisionBasisChanged()`；不匹配就记 `stale_decision` 并重新唤醒，动作不执行。
- **上下文预算。** `ContextBudget.cs` 统一调用 `DecisionContext` 投影和预算裁剪；`AgentToolDiscovery` 常驻 11 个基础工具，加当前候选/任务相关工具及短期 lookup 定义，按固定顺序输出完整契约。`tools.lookup` 支持能力组，不额外调用分类模型，不截断参数 schema；原始回执仍按 ID 保留。

### 工具系统

`AgentToolRegistry.Catalog` 保存工具目录及参数/语义契约；`work.run` 的结构化来源是 `ToolSpecs`，Catalog 文本也从该来源生成。`ExecuteCore()` 的 switch 把工具路由到各 `ModEntry` partial 方法。

新增工具需要三处同步：`Catalog` 条目、`ExecuteCore` 分支、必要时 `AgentToolDiscovery.CoreNames`。每个请求先冻结 ToolSpec；API tools 和 Decode（包括 plan.submit 内动作）共用该份参数 Schema。work.run 按农务/资源/仓储及按需加载的钓鱼/矿洞/畜牧/伙伴生产配置生成；其他工具从 Catalog 迁移到 ToolSpec，歧义参数继续由业务声明检查验证。当前使用普通 Tool Calls（未启用 Beta strict）；模型返回 tool_calls，按 API 调用 ID 回传接收回执，异步完成仍由原生事件核验。描述与参数 schema 都是运行时契约。

`ExecutionContract.Receipt()` 给所有回执加统一信封：`stop_reason`、`retryable`、`resume_policy`、`resource_delta`、`native_progress_evidence`、`evidence_ids`。它**保留**各执行器的原生证据，绝不从 UI 点击或物品消失推断业务成功。

### 状态与记忆

`SaveData` 走 SMAPI `WriteSaveData("together-v2")`，存档绑定。`MemoryArchive` 分离事实/计划/经验，`memory.search` + `memory.evidence` 按证据 ID 取原文。`OperationsState.Constraints` 记录「主体＋原因＋真正相关条件」的服务约束（例：商店未开门），按真实条件解除而非按状态哈希或冷却时间。

## 硬性约定

- **命令被接收 ≠ 执行成功。** 完成必须核验真实游戏状态。回执、文档、提交信息都不能把排队、投料、库存齐全说成已完成。这是全项目最核心的纪律，代码注释和工具描述里反复出现。
- **AgentLab 隔离。** 所有夹具接口（`LabScenarios.cs`、`/lab/together`、`agent_new`、`together_lab_*`）三重门禁：`Settings.EnableLab` + `Context.IsWorldReady` + `Game1.player.Name=="AgentLab"`。绝不在个人存档跑夹具。
- **独立 Mods 目录。** 构建只写 `work/Mods` 或 `work/CompanionMods`，不碰游戏原有 Mods。
- **不进 Git：** `work/`、`external/`、`outputs/`、`.env`、`.local.json`、bin/obj。游戏资产、存档、密钥同理。
- 阶段通过验证后才提交；提交不代表已发布。

## 测试的坑

`tests/Together.DomainChecks.csproj` 设了 `EnableDefaultCompileItems=false`，每个被测文件都要手写 `<Compile Include="../mod/Together/Xxx.cs"/>`。**新增域文件不加进去就完全不会被编译或测试**，而且不会报错。

`TogetherDomainChecks.cs` 是 `Exe` 入口，顺序调用各 `*Checks.Run(...)`，失败直接抛异常。新增检查类要在这里挂上。

## 代码风格

C# 高度紧凑：单行多语句、表达式体成员、少空行，一个文件一个业务域。跟随周围写法，不要因为「更规范」就展开成多行。

注释只解释**为什么**，尤其是绕开上游行为或原生机制的地方（例：`// Replanning from a tile corner and smoothing a centre-to-centre ray can cut into a machine.`）。

面向模型的字符串优先精简中文；可采用经过等义分词实测更省且清楚的英文，不双语重复。明确写出能力边界与必要约束——这些文字是运行时契约，改动等于改行为。文档在 `docs/`，中文，区分「实测＋代码 / 静态确认 / 待验证」三档证据强度。

## 当前工作

`docs/统一经营内核重构实施方案与代码审查.md` 是下一阶段实施依据（R01–R07 + 四天连续验收 R08），列出 A01–A16 已定位缺陷。`docs/经营优化第二轮实施记录.md` 记录最近一轮已核验的改动。`docs/自主通关开发方案与复用清单.md` 是全项目范围清单。

输入与工具契约优化的最新记录见 `docs/Agent按需上下文与工具契约精简实施与日测-20260923.md`。最终同档日测正常保存；输入中位8,999.5 token，尚未达到6,000–8,000目标，不应声称经营质量验收通过。

09:40提前睡觉的后续修复见 `docs/Agent收工决策与备料体力校验修复-20260923.md`：模型结束一天的两种入口共用结构化候选评估，备料明确材料用途和体力预留；事实矛盾连续3次无进展则暂停。用户要求启动一天测试后自行观察，本轮只核验启动，经营结果等用户反馈后再读日志。

最新修复见 `docs/Agent营业等待采购恢复与收工价值修复-20260923.md`：保留营业前时间事实，以原生回执维护采购清单，补入待播种收工候选，比较未来价值，并补狭窄路径的像素级绕行。新一天测试仍由用户观察，不把离线验证或启动预检当成经营验收。

开发/测试启动默认 1280×800 窗口且静音；`scripts/launch.py` 与 AgentBridge 共同应用，读档不得恢复声音或全屏。隔离测试启动器在启用模型前核验窗口和四类音量。

13:00商店暂停的修复见 `docs/Agent选种暂停与数量口径修复-20260923.md`：选种只用reason、空清单可拒绝、报价单次消费；可修正采购错误不冻结全局；采集新增量与库存目标互斥；背包堆叠上限、原生进度单位与真实暂停原因显式展示。

连续任务与局部恢复的最新修复见 `docs/Agent连续任务与局部恢复修复-20260923.md`：采购播种链持续持有玩家任务通道，报价与投资候选去重；备料缺省预留按待办农务计算；收工使用reason＋defer，错误不再跨动作累计冻结全局。

体力与采购账本修复见 `docs/Agent体力与采购账本修复及七天试跑-20260923.md`：耕作成本复用原生单次工具成本；选种上限、排定采购、实际到货分开；失败追加不改账，跨天清空当日数量。七天试跑使用原始隔离存档、窗口静音，仅核验启动，由用户观察。
