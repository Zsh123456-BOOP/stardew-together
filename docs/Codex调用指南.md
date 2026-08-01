# 由 Codex 充当规划者

当前根据用户选择，暂不配置模型服务。Codex 读取结构化状态、理解用户目标，提交技能计划，再根据执行结果决定是否继续或调整。游戏控制完全通过下面的工具进行。

所有命令在本项目根目录执行。

## 1. 观察

```bash
python3 -m agent state
```

先检查 `ready`、`location`、`actors`、工具/水量以及目标作物。名称为 bot-1 / bot-2 的机器人由专用测试夹具创建；普通存档机器人 ID 以状态接口实际返回为准。

## 2. 提交计划

查看 `configs/demo-plan.json`：它表达“浇水 → 收获 → 返回”，并明确角色和地块坐标。Codex 根据当前指令和状态生成或修改计划，再调用：

```bash
python3 -m agent submit configs/demo-plan.json
python3 -m agent run <返回的任务ID>
```

也可用 `submit ... --run` 同时创建和执行。执行进程可能需要几十秒；需要边运行边接收临时指令时，应让它在独立终端/命令会话中运行。

## 3. 查进度与证据

```bash
python3 -m agent task <任务ID>
python3 -m agent events <任务ID>
python3 -m agent state
```

只有状态为 `succeeded` 且最终游戏状态符合目标时才向用户报告完成。请求被接收、动作已开始、角色看起来在移动，都不等于任务完成。

## 4. 临时改令

```bash
python3 -m agent pause <任务ID>
python3 -m agent task <任务ID>
```

看到状态 `paused` 后，再给同一机器人提交“返回”任务。原任务会保留目标与进度。返回任务完成后：

```bash
python3 -m agent resume <原任务ID>
```

恢复会重新读取作物状态，不盲目重放旧动作。独立进程的角色锁可避免两个高层任务同时控制同一机器人。

## 5. 失败处理

- `unreachable`：读取新地图/状态，调整目标或绕行；执行器已先尝试有限次数局部重规划。
- `missing_tool` / `resource_insufficient` / `inventory_full`：报告具体条件，不伪造补充资源。
- `stale_session`：存档或测试场景已经改变，重新观察并新建计划。
- `connection_uncertain` / `cancel_pending`：先核对旧命令状态，禁止立即重发有副作用的动作。

测试用 `reset-lab` 会改变场景和会话，不应作为任务失败后的普通恢复手段。正式展示应保留失败记录，而不是不断重置直到得到一次成功。

## 真实能力边界

当前有可执行的结构化工具、任务状态机和真实游戏反馈；自然语言理解由当前对话中的 Codex 完成。没有独立聊天窗口，也没有声称规则解析器就是大模型。后续接模型只需要替换规划端，保留相同工具与校验机制。
