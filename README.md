# 星露谷状态驱动 Agent

无需视觉模型，通过 SMAPI 读取真实地图和作物状态，用 Farmtronics 机器人执行任务。

完整设计见 [开发方案](星露谷Agent开发方案.md)。开发进度和实测结果记录于 `docs/`。

## 开发约定

- 阶段通过验证后提交本地 Git；提交不代表已发布。
- `vendor/Farmtronics` 是固定版本的上游子模块，保留上游许可证。
- 游戏资产、个人存档、密钥与运行产物不进入 Git。
- 使用项目独立的 Mod 目录；开发场景明确标记为 AgentLab。
- 命令被接收不等于执行成功，完成必须检查真实状态。

## 上游

[Farmtronics](https://github.com/JoeStrout/Farmtronics)，MIT，固定提交由 Git 子模块记录。本项目新增控制 API、SMAPI 桥接、任务调度和评估，不将上游能力冒充自研。
