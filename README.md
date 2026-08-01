# 星露谷状态驱动 Agent

无需视觉模型，通过 SMAPI 读取真实地图和作物状态，用 Farmtronics 机器人执行任务。

完整设计见 [开发方案](星露谷Agent开发方案.md)。开发进度和实测结果记录于 `docs/`。

**已实测跑通**：读取地图/作物/背包 → A* 寻路 → 整片浇水 → 收获入包 → 返回；支持暂停继续、执行途中绕开新障碍。当前由 Codex 充当上层规划者，尚未接独立模型服务。

## 开发约定

- 阶段通过验证后提交本地 Git；提交不代表已发布。
- `vendor/Farmtronics` 是固定版本的上游子模块，保留上游许可证。
- 游戏资产、个人存档、密钥与运行产物不进入 Git。
- 使用项目独立的 Mod 目录；开发场景明确标记为 AgentLab。
- 命令被接收不等于执行成功，完成必须检查真实状态。

## 上游

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
