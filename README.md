# 同行 · Together：星露谷陪伴 Agent

**当前可运行原型为 C# Mod「同行 · Together 0.2」**：游戏内 F8 聊天、自定义人设、DeepSeek Flash 决策、自主目标、中断恢复、共同农务、真实进度读取、关系与记忆，以及 Squad 执行动作。不使用视觉模型，正式游玩不依赖 Python 服务。

[完整产品设计方案（实施中）](docs/同行完整设计方案.md) · [原型使用指南](docs/同行使用指南.md) · [0.2 实现与验收](docs/同行0.2实现与验收.md) · [上游来源与许可边界](docs/同行来源说明.md)

```bash
python3 scripts/build_companion.py
python3 scripts/package_together.py
```

双击生成目录 `outputs/同行体验版/启动同行.command`。模型 Key 读取项目 `.env`，不进入 Git 或安装 ZIP。`Together-0.2.0-own-code.zip` 仅含新 Mod，需要单独准备 Squad 适配依赖；本机体验目录已配好。

开发测试仍可用 `python3 scripts/launch.py --companion --lab`，仅在 AgentLab 专用存档操作。C# 检查：`work/dotnet/dotnet run --project tests/Together.DomainChecks.csproj`；真实游戏检查：`python3 evals/companion_control.py`、`python3 evals/together_control.py`、`python3 evals/farm_care.py`（关闭面板并停止任务后运行）。

以下保留早期 Python 决策原型与 Farmtronics 验证底座说明，其限制不代表当前 Together 版本。

当前产品方向为**有自定义人设、关系记忆与自主决策的 NPC 陪玩队友**。已实测接通 **DeepSeek Flash → 人设决策 → 指定 Squad NPC → 挖矿结果回执**，支持拒绝、显式强制和空闲自主行动。[陪伴版运行与验收](docs/陪伴阶段B验收与运行.md) · [NPC 控制验收](docs/陪伴阶段A验收.md) · [源码衔接设计](docs/三仓库衔接方案.md)。

陪伴版：`python3 scripts/build_companion.py` → `python3 scripts/launch.py --companion --lab`，进入测试存档后运行 `python3 -m agent.companion '帮我挖一块石头。' --persona adventurer`。正常游玩省略 `--lab` 并通过 Squad 招募 NPC。配置示例见 `.env.example`，实际 Key 只放被忽略的 `.env`。

下面保留 Farmtronics 技术验证底座的使用说明。

无需视觉模型，通过 SMAPI 读取真实地图和作物状态，用 Farmtronics 机器人执行任务。

完整设计见 [开发方案](星露谷Agent开发方案.md)。开发进度和实测结果记录于 `docs/`。

**Farmtronics 已实测跑通**：读取地图/作物/背包 → A* 寻路 → 整片浇水 → 收获入包 → 返回；支持暂停继续、执行途中绕开新障碍。该底座验收时由 Codex 充当上层规划者；上面的 NPC 陪伴版另接 DeepSeek。

## 开发约定

- 阶段通过验证后提交本地 Git；提交不代表已发布。
- `vendor/Farmtronics` 是固定版本的上游子模块，保留上游许可证。
- 游戏资产、个人存档、密钥与运行产物不进入 Git。
- 使用项目独立的 Mod 目录；开发场景明确标记为 AgentLab。
- 命令被接收不等于执行成功，完成必须检查真实状态。

## 上游

陪伴方向的三个固定版本在 `configs/companion-upstreams.json`，本地源码放在忽略提交的 `external/`。运行 `python3 scripts/prepare_companion_sources.py --remove-squad-tests` 可准备源码并移除用户指定的 Squad 测试及解决方案引用。该脚本不会安装 Mod。

[Farmtronics](https://github.com/JoeStrout/Farmtronics)，MIT，固定提交由 Git 子模块记录。本项目新增控制 API、SMAPI 桥接、任务调度和评估，不将上游能力冒充自研。

## 本机运行

需要 .NET SDK 6、Python 3.11+、星露谷 1.6.15 和 SMAPI 4.5.2（目前实测组合）。可通过 `DOTNET` 指定 SDK 可执行文件，通过 `STARDEW_GAME_PATH` 指定包含游戏 DLL 的目录。

```bash
git submodule update --init
python3 scripts/build.py
python3 scripts/launch.py --lab
```

在启动后的 SMAPI 控制台输入 `agent_new`，仅允许从标题页创建新的 AgentLab 测试角色。角色载入后可输入 `agent_lab` 初始化测试地块，`world_freezetime 1` 冻结测试时钟。另一个终端执行：

```bash
python3 evals/stage0_smoke.py
```

`--lab` 开启专用测试初始化接口；接口还会检查角色名。正常模式不开放初始化接口。构建只写项目 `work/Mods`，不覆盖游戏的原有 Mods。启动脚本生成 `.local.json` 本地访问令牌，请勿提交或公开。

## 执行完整任务

```bash
python3 -m agent state
python3 -m agent reset-lab
python3 -m agent submit configs/demo-plan.json --run
```

计划依次完成 12 株作物浇水、6 株成熟作物收获和返回。坐标针对 AgentLab-v1 测试场景；其他地块需要按实时状态调整。

暂停与继续、失败处理见 [Codex 调用指南](docs/Codex调用指南.md)，接口细节见 [协议说明](contracts/协议说明.md)。

## 验证

```bash
python3 -m unittest discover -s tests -v
python3 evals/stage0_smoke.py
python3 evals/stage1_tasks.py
python3 evals/dynamic_obstacle.py
```

后三项是游戏内集成测试，会重置 AgentLab 专用场景，不用于个人正常存档。实测报告见 [阶段 1 验收](docs/阶段1验收报告.md)。
