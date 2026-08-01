# 陪伴阶段 B：DeepSeek 人设决策与自主行为

日期：2026-09-18。状态：已用真实 `deepseek-flash` API 和真实星露谷游戏跑通。阶段 A 的 NPC 控制证据见 `陪伴阶段A验收.md`。

## 本阶段实现

流程：真实游戏状态 → 人设、原生关系与近期经历 → DeepSeek JSON 决策 → 本地协议与目标校验 → Squad 执行 → 实际结果入库。

- 使用官方 `https://api.deepseek.com/chat/completions`、`deepseek-flash`，纯文本，无视觉输入。[官方接口](https://api-docs.deepseek.com/)、[JSON 输出](https://api-docs.deepseek.com/guides/json_mode/)。
- 人设 JSON 可编辑：关系称呼、性格、喜好、厌恶、当前心情和主动行为倾向。内置冒险爱好者与疲惫的钓鱼爱好者两份样例。
- 模型输出 accept/refuse/negotiate/idle；执行技能只开放 mine/follow。拒绝和协商不派工。
- 目标必须是游戏端提供的真实对象 ID，推理后再次读取世界状态，避免按过期地图行动。
- 玩家可以显式 `--force mine`，程序覆盖意愿决定，但仍检查目标与游戏条件。模型不能自行为响应添加 force 字段。
- 模型 JSON 结构或决策与动作组合无效时，最多再调用一次修正；再次失败则终止，不派工。无无限重试。
- 原生友情点、约会／婚姻状态已经读取并传入决策；本阶段没有修改游戏好感数值，也未单独测量不同好感度的因果影响。
- 近期经历存入 `work/companion.sqlite`，按包含存档／玩家／NPC 的 actor ID 隔离；读取最近六次交互及真实结果。不是完整的长期承诺管理系统。
- `--watch` 观察真实空闲状态两秒后触发自主决策，不需要玩家逐条下命令；同一候选目标环境去重，决策间隔至少十秒，默认最多三次决策、九十秒观察窗口。
- 模型只决定目标；NPC 移动、交互和挖矿由 C# / Squad 完成。完成对白从实际结果生成，避免模型提前宣称成功。

## 真实验收结果

入口：`python3 evals/companion_deepseek.py`。这是会调用付费 API 并重置 AgentLab 测试场景的验收脚本。

最终完整一轮：

| 场景 | 模型／程序行为 | 游戏结果 | 模型响应秒数 | Tokens |
| --- | --- | --- | ---: | ---: |
| 冒险人设收到挖一块石头请求 | accept，选择真实目标 | 实际挖掉，证据 `mined_by_actor=true` | 1.262 | 1571 |
| 疲惫钓鱼人设收到相同请求 | refuse：“今天真的很累，实在不想挖石头……” | 无命令派发 | 1.184 | 1774 |
| 玩家显式强制挖矿 | 接受并按程序强制参数执行 | 实际挖掉，保留验证 | 1.506 | 2057 |
| 观察到空闲，自主决策 | 主动选择附近石头并说明原因 | 实际挖掉，无玩家逐条指挥 | 1.069 | 2228 |

四个场景通过；该轮四次 API 调用，总计 7630 tokens，均未触发修正重试。这里只统计最终完整验收轮；之前的连接测试和开发调试调用不计入这个数值。以上为模型响应耗时，未包含 NPC 行走和挖矿耗时。

初次自主决策测试出现过 decision 与 skill 不一致，校验层拒绝了该响应，没有向游戏派工。补充自主行动示例和一次受限修正机制后，最终完整一轮通过。

本项目共十二项单元检查通过，包括模型伪造目标、拒绝夹带动作、额外 force 字段、无效响应最多修正一次、强制后仍检查过期目标、拒绝不派工和记忆隔离。单元测试用的 FakeModel 仅用于协议测试；本页四个场景全部使用真实 DeepSeek 与真实游戏。

一个场景一个样本，不能据此声称人设一致性达到某个稳定成功率。后续需多轮、不同关系与环境下的评测。

## 快速使用

项目根目录为 `/Users/zhongsuhua/Desktop/星露谷agent开发`。`.env` 已在本机配置；权限 0600，Git 忽略。不要把实际 Key 放入 `.env.example`、代码或报告。

先构建并启动：

```bash
python3 scripts/build_companion.py
python3 scripts/launch.py --lab --companion
```

测试环境载入方式参照阶段 A。游戏中需要已招募的 NPC；专用 AgentLab 可以通过 `python3 -m agent reset-lab` 初始化两位队友和三块石头，要求玩家已经在 Farm。正常游玩通过 Squad 自身招募功能加入队友，不使用 lab 初始化。

给队友指令：

```bash
python3 -m agent.companion '帮我挖掉附近的一块石头。' --actor Abigail --persona adventurer
python3 -m agent.companion '帮我挖掉附近的一块石头。' --actor Abigail --persona fishing
python3 -m agent.companion '这次请按我的指令挖一块。' --actor Abigail --persona fishing --force mine
python3 -m agent.companion '跟着我。' --actor Abigail --persona adventurer
```

让队友自己观察空闲并决定行动：

```bash
python3 -m agent.companion --actor Abigail --persona adventurer --watch --max-decisions 3 --max-seconds 90
```

观察窗口到期不再开始新决策；已经开始的模型请求／动作会等待其自身超时或结果。每次决策正常一次模型调用，格式不合法时最多两次；单次输出上限 600 tokens，未启用思考模式。拒绝后相同环境不会不断重新询问。需要长期运行时应后续实现独立服务与预算设置，当前脚本默认有限运行。

人设文件：`configs/personas/adventurer.json`、`configs/personas/fishing.json`，直接编辑后下一次调用生效。当前 CLI 选择这两个配置名；尚未提供游戏内任意人设创建界面。`current_mood` 是手工配置状态，尚未由游戏事件自动更新。

## 证据、错误与边界

- 最终游戏／模型证据保存在本地 `outputs/companion-deepseek-report.json`；运行数据库和输出不提交到 Git。
- 模型网络请求发生在外部 Python 进程，不阻塞游戏主线程。网络失败不会开始动作；命令发送后连接不确定则保存 command ID 和 unknown，需查询该 ID，不能新建命令盲目重试。
- 游戏端挖矿验证的是节点被指定 NPC 动作消除，不把玩家的采集量计为队友产出。
- 正式战斗、陪钓、独立钓鱼、跨地图长任务、完整承诺恢复尚未通过这个新 Agent 入口开放。
- 当前输入和对白显示在终端；没有把 AI Dialogue 的聊天 UI 直接合并进来，也未把三个上游 Mod 原样同时启用。此版本以 Squad 为唯一行动执行者，借鉴另外两者的职责，使用本项目自己的协议与决策代码。
- Key 不进入模型上下文、日志或 Git；API 错误只报告状态码，不打印请求头或完整错误响应。本次验收结束后没有留下持续调用模型的后台循环。

下一步应优先做游戏内聊天入口与任务状态展示，再扩展“挖几块→遇怪中断→恢复→兑现约定”的跨事件任务。
