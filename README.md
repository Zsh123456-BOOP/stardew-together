<div align="center">

# 同行 · Together

### 把一句「帮我经营农场」，变成星露谷里的真实行动。

**面向自主经营、任务与全成就推进的游戏 Agent，也是一位随时可以接手工作的游玩助手。**

`Stardew Valley` · `SMAPI` · `DeepSeek Flash` · `C#`

[快速开始](#快速开始) · [工作原理](#工作原理) · [开发文档](#开发与文档) · [团队](#团队)

</div>

---

你定目标，同行负责理解世界、安排工作、调用工具，再检查事情是否真的完成。

从整理第一块农田，到采购、种植、收获、变现与再投资；从一封新邮件，到任务前置、区域解锁与成就进度——我们希望 AI 能理解一整段游戏生活，并把长期目标落实成今天可以执行的一步。

你可以让它自主经营，也可以只交给它一件事，随时接回控制继续玩。

## 想怎么玩，由你决定

| 玩法 | 你提出目标 | 同行的工作方式 |
| --- | --- | --- |
| 自主经营 | 「把农场经营起来，收入用来继续发展。」 | 读取作物、库存、现金和时间，选择工作，执行并记录实际结果。 |
| 任务与成就 | 「看看下一步能推进什么。」 | 查询原生任务、收集与解锁进度，展开已适配的前置条件和材料需求。 |
| 游玩助手 | 「帮我照顾这片田，我回来再接着玩。」 | 连续完成农活、拾取、补水与必要整备，支持暂停和接管。 |
| 游戏百科 | 「这份材料能做什么？现在能种什么？」 | 查询游戏数据中的物品、配方、作物与条件，把依据交给玩家和模型。 |

自主控制玩家是当前主要运行形态。全成就闭环、自定义伙伴协作与中后期完整生产链仍在持续完善。

## 不只会聊天，还能把工作做下去

**经营有依据。** 商店真实报价、作物成熟日期、实际库存和配方共同参与决策。AI 选择目标、品种与投资方向，程序负责数量计算、条件检查和执行。

**劳动能连续。** 寻路、工具选择、批量农活、掉落拾取与库存整备由执行器衔接。模型不必为每一步移动、每次挥锄重新发指令。

**进度有记忆。** 当天买了什么、种了什么、哪些工作已经完成，进入事实记录。相同条件下的失败合并保存，附带解除条件；历史叙述不会代替当前真实库存。

**过程看得见。** 游戏内常驻面板展示正在做什么、原因和下一步。完整模型请求、工具回执、动作与资源变化可留存，便于复盘每一次决策。

**结果靠核验。** 制作、购买、出货和过夜走原生流程。命令被接收不等于完成，物品与进度变化才是完成依据。

## 工作原理

```mermaid
flowchart LR
    A[玩家目标] --> B[DeepSeek Flash\n选择目标与取舍]
    C[SMAPI 真实状态\n地图 · 背包 · 时间 · 进度] --> B
    D[百科与计算工具\n配方 · 报价 · 容量 · 条件] --> B
    B --> E[持续任务计划]
    E --> F[原生动作执行\n移动 · 劳动 · 采购 · 制作]
    F --> G[结果核验与当日记忆]
    G --> C
    G --> B
```

大模型负责有意义的选择，算法负责可计算的细节。Together 直接通过 SMAPI 读取游戏状态，无需逐帧截图识别；正式运行也不依赖常驻 Python 决策服务。

工具层覆盖农务、清理采集、钓鱼、矿洞行动、商店交互、制作、存储、出货、任务查询与睡眠换日。各项能力的适配范围和原生验证记录保存在开发文档中。

### 记忆：保存经历，按需找回依据

Together 将记忆中的内容分开处理：

| 内容 | 记录什么 | 如何用于决策 |
| --- | --- | --- |
| 事实与事件 | 原生动作回执、采购、种植、浇水、出货及资源变化 | 回答“发生过什么”，保留结果和证据来源 |
| 计划与承诺 | 当前目标、任务依赖、执行状态和未完成工作 | 回答“还有什么要做”，由结构化任务队列维护 |
| 经验与总结 | 带适用条件的失败规则、日／季统计摘要，以及可选的模型日总结 | 帮助检索历史和避免重复失败；模型总结标记为派生内容 |

记忆检查点随游戏存档保存，详细事件另存为按存档与玩家隔离的本地归档，并建立包含日期、工具、对象和结果的检索索引。通过 `memory.search` 查找历史，通过 `memory.evidence` 按证据 ID 读取原文。启用模型日总结时，总结必须引用给定事件中的有效证据 ID；它仍然是模型归纳，不是新的游戏事实。

例如，“昨天已经浇水”会保留为历史，但今天是否缺水仍从游戏重新读取。**历史流水不累加成当前库存，计划不算已完成，摘要也不能覆盖实时状态。**

源码：[记忆归档](mod/Together/MemoryArchive.cs) · [检索索引](mod/Together/MemoryIndex.cs) · [记忆接入](mod/Together/MemoryArchiveRuntime.cs) · [可选日总结](mod/Together/MemoryReflectionRuntime.cs)

### 上下文压缩：每轮只提供决策需要的内容

每次请求都会重新组织当前事实，而不是不断追加整段历史对话。时间、体力、现金、库存、活动任务、采购状态和必要约束优先保留；其他信息按当前目标筛选，详情通过查询工具展开。

压缩主要发生在三处：

- **按需展开资料。** 当前目标保留必要的材料与条件；其他配方、建筑和成就通过 `progress.catalog`、`progress.dependencies` 查询。成就按各条进度线提供下一档候选，选中的目标保留进度，避免每轮携带全部档位。
- **去掉重复表达。** 同一材料缺口集中保存，候选工作引用它；已经在当前状态或工具消息中交付的查询结果不重复展开。同一笔原生采购能够解释的入包和扣款，归并为“买入 15 份种子，支出 300 金”，保留证据关联；无法解释的变化继续记录。
- **按预算裁剪历史。** 先预留固定规则、工具定义和必要回执的空间，再压缩上下文。较旧、较长的详情改为摘要与补读入口，可用 `context.read`、`query.read` 或记忆工具取回。关键状态超过硬预算时明确报错，不静默删除关键约束。预算使用估算值，实际 token 用量以 API 返回为准。

例如，正在备木材时不必反复展开所有种子的收益表；进入采购决策后，再读取现场报价和种植负担。压缩的是本轮输入视图，原始事件仍保存在归档中。

源码：[上下文投影](mod/Together/DecisionContext.cs) · [按需展开与去重](mod/Together/OnDemandContext.cs) · [流水归并](mod/Together/ActivityDiary.cs) · [输入预算](mod/Together/ContextBudget.cs)

### 工具按需加载：先提供相关能力，再展开细节

程序根据当前任务、候选工作、所在地点和菜单状态选择工具，无需额外调用一个分类模型。查询、计划、移动、劳动、等待和收工等基础工具常驻；农务、交易、仓储生产、社交、探索等能力按需加入。模型还可以用 `tools.lookup` 按名称、用途或能力组查找工具，查得的定义短期保留，活动任务所需的工具持续保留。

筛选也深入工具内部：`work.run` 按工作类型组合参数与说明。例如农务和备料任务使用相关能力配置，钓鱼、矿洞和畜牧等配置在需要时再加载。**API 中的工具定义和调用参数校验使用同一份冻结契约**，不会为了压缩而直接截断参数 Schema。

工具可见只表示模型可以提出调用；预算、地点、解锁条件和原生执行前置条件仍由程序检查。

源码：[工具发现](mod/Together/AgentToolDiscovery.cs) · [能力配置与参数契约](mod/Together/ToolSpec.cs) · [原生 Tool Calls 协议](mod/Together/NativeToolProtocol.cs)

### 一次完整调用：从观察到结果回写

```mermaid
flowchart TD
    A[读取 SMAPI 当前状态] --> B[整理任务、相关记忆与待交付结果]
    B --> C[选择工具契约并压缩上下文]
    C --> D[模型返回 Tool Calls]
    D --> E[检查决策是否过期、参数与执行条件]
    E --> F[调度原生动作与持续任务]
    F --> G[核验实际结果，更新任务与记忆]
    G --> A
```

一次请求由固定 `system` 规则、压缩后的当前上下文、通过 API `tools` 字段提交的工具定义，以及必要的上一轮调用与回执组成。模型返回结构化 `tool_calls`，程序按本轮契约校验参数，并检查回复是否仍适用于当前状态，再交给执行器。

工具消息通过 `tool_call_id` 对应模型调用；对于异步任务，最初回执可能只是“已接收／已排队”，实际完成要等原生动作结果核验后再交付。例如购买请求入队时不会增加已购数量，只有原生到货证据才更新账本与记忆。寻路、挥锄、拾取等连续子动作由执行器推进，不需要每一步都重新请求模型。

源码：[请求组装](mod/Together/AutoplayModel.cs) · [决策与调度循环](mod/Together/AutoplayRuntime.cs) · [结果回执契约](mod/Together/ExecutionContract.cs)

## 从一块田，到全成就

全成就意味着把经营、收集、制作、关系和探索连接成长期计划，而不仅是连续执行一批劳动。

| 项目 | 时间与规模 |
| --- | --- |
| 玩家成就收集参考 | 基础 40 项成就约 **150–200 小时**；1.6 新增内容需另计。 |
| Together 全成就运行用时 | **153 小时**，Windows 运行。 |
| DeepSeek Flash 总用量（日志折算） | **1.7451 亿 token**，包含输入与输出。 |
| 完成全部成就的 API 总费用 | **人民币 213.3 元**。 |

用时与总费用来自维护者提供的 Windows 运行结果；Token 按既有运行日志折算。玩家耗时参考 [TrueAchievements](https://www.trueachievements.com/game/Stardew-Valley/completiontime)。

## 快速开始

当前已验证的开发环境：**macOS、Stardew Valley 1.6.15、SMAPI 4.5.2、.NET SDK 6、Python 3.11+**。需要自行安装游戏与 SMAPI，并准备 DeepSeek API Key。

```bash
git clone --recurse-submodules https://github.com/Zsh123456-BOOP/stardew-together.git
cd stardew-together
cp .env.example .env
```

在本机 `.env` 填入模型配置：

```dotenv
DEEPSEEK_API_KEY=你的密钥
DEEPSEEK_MODEL=deepseek-flash
DEEPSEEK_BASE_URL=https://api.deepseek.com
```

准备固定版本的上游源码，构建并启动独立 Mod 环境：

```bash
python3 scripts/prepare_companion_sources.py --remove-squad-tests
python3 scripts/build_companion.py
python3 scripts/launch.py --companion --keep-window
```

游戏目录不在默认位置时，设置 `STARDEW_GAME_PATH`；需要指定 SDK 时，设置 `DOTNET`。构建产物写入项目的 `work/`，不覆盖原有游戏 Mods。

进入存档后，按 **F8** 打开同行，进入自主游玩并填写目标。按 **F10** 可暂停接管。右侧半透明面板支持滚动，便于查看当前安排。

API Key 只保存在本机，`.env`、存档、日志与运行产物均不进入 Git。模型用量会记录；运行配置中的 `ModelTokenBudgetPerDay: 0` 表示不设置每日 token 上限。

## 开发与文档

| 目录 | 内容 |
| --- | --- |
| `mod/Together/` | 模型决策、任务调度、经营工具、百科、记忆与游戏内界面 |
| `mod/SquadAdapter/` | Squad 伙伴控制适配 |
| `mod/AgentBridge/` | 游戏状态与控制桥接 |
| `scripts/` | 固定依赖准备、构建、打包、启动与验证工具 |
| `tests/` | 纯算法与协议检查 |
| `docs/` | 设计、开发记录与原生验证证据索引 |
| `vendor/Farmtronics/` | 固定版本的 Farmtronics 子模块 |

- [产品设计](docs/同行完整设计方案.md)
- [自主通关开发方案与复用清单](docs/自主通关开发方案与复用清单.md)
- [百科与知识检索](docs/同行百科与知识检索方案.md)
- [原生目标续作与背包处理](docs/第十二轮目标续作与背包处置修复记录.md)
- [来源与许可说明](docs/同行来源说明.md)

算法检查可运行 `dotnet run --project tests/Together.DomainChecks.csproj`。原生夹具只在开启 Lab 模式、世界就绪且角色名为 `AgentLab` 时执行，不用于正常经营存档。

当前重点是长期运行稳定性、生产链衔接与任务/成就覆盖；伙伴双身体协作作为后续方向推进。每项能力以真实游戏结果验收，不直接修改经验、金钱、成就或统计计数来替代行动。

## 团队

[Zsh123456-BOOP](https://github.com/Zsh123456-BOOP) · [LHBVv](https://github.com/LHBVv) · [zsq040123-cloud](https://github.com/zsq040123-cloud)

## 开源基础

Together 复用 [The Stardew Squad](https://github.com/Isalda/the-stardew-squad) 的伙伴与导航基础，以及 [Farmtronics](https://github.com/JoeStrout/Farmtronics) 的机器人原型；同时参考 [StardewValley-MCP](https://github.com/amarisaster/StardewValley-MCP) 与 [StardewValleyAIDialogueMod](https://github.com/phenyisole/StardewValleyAIDialogueMod) 的设计。

依赖版本见 [`configs/companion-upstreams.json`](configs/companion-upstreams.json) 与 Git 子模块记录。上游来源及许可证随对应代码保留。
