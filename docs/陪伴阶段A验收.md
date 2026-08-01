# 陪伴阶段 A：指定 NPC 执行与结果反馈

日期：2026-09-18。实测环境：macOS、星露谷 1.6.15、SMAPI 4.5.2，固定 Squad 0.12.1 源码。

## 实现

- 本项目 `mod/SquadAdapter/CompanionControl.cs` 编入本地构建副本，通过 SMAPI GetApi 暴露控制接口。原始 Squad 克隆的运行源码保持未修改。
- `scripts/build_companion.py` 对固定源码做少量挂接：指定任务分配、挖矿结果归因、托管角色禁用无关自动劳动、卡住传送记录。移除构建副本的自动部署目标。
- 通用 AgentBridge 可选择 Farmtronics 或 Squad；Squad 使用独立 `work/CompanionMods`，原游戏 Mods 未覆盖。
- 当前开放 `mine`（指定真实目标 ID）和 `follow`（切换跟随模式）；只支持单机、玩家附近的已招募 NPC。
- 角色 ID 包含存档、玩家和 NPC 内部名；目标 ID 绑定真实对象实例。派工后查询实际游戏状态，并记录特定 NPC 的挖矿删除事件。
- terminal 结果固定保存，重复查询不会因为 NPC 后来走动而改变完成证据。取消从游戏主线程执行；已经完成挖矿的动作保留 succeeded，不伪装成没发生过。

## 真实游戏检查

入口：`python3 evals/companion_control.py`。证据位于本地忽略提交的 `outputs/companion-control-report.json`。

1. 场景中 Leah 比 Abigail 更靠近目标，指定 Abigail 后由 Abigail 执行。
2. Abigail 的坐标发生真实变化，目标石头被其挖矿动作移除，返回 `mined_by_actor=true`。
3. 原命令重发得到同一结果，不重复执行；终态证据不变。
4. 同一个命令 ID 更换内容返回 `command_id_conflict`。
5. 旧会话返回 `stale_session`。
6. 对已移除对象再次派工返回 `stale_target`。
7. `follow` 成功切换跟随模式；该状态不代表已经到达玩家身旁。
8. 行走开始后取消，返回 cancelled，石头未被挖掉，角色无残留挖矿任务。

以上八项通过。测试使用独立 AgentLab 存档、明确初始化的两名 NPC 和石头，并冻结游戏时钟；动作执行使用 Squad 自身寻路和工具逻辑。不是任意地图、复杂战斗或长期稳定性结论。

## 启动

```bash
python3 scripts/build_companion.py
python3 scripts/launch.py --lab --companion
```

SMAPI 控制台可用 `agent_load AgentLab_449404282` 加载已有专用测试存档；该命令只在 lab 模式标题页接受 `AgentLab_<数字>` 名称。载入后 `debug warp Farm 44 23` 到测试地图，`world_freezetime 1` 冻结测试时钟，再运行验收脚本。这些测试入口不用于个人正常存档。

## 当前限制

- 自动战斗允许抢占任务；当前向上层回报 interrupted，尚未自动恢复长任务。
- 不提供独立跨地图工作、独立钓鱼、聊天 UI 或多 NPC 高层协商。
- 这里只验收“挖掉节点”，未把队伍总库存增加当作该 NPC 独占的采矿产出。
- 队友传送兜底会被标为 recovery_warp 失败，不能计为寻路成功。
- 本阶段的模型尚未参与验收；DeepSeek 人设测试在下一阶段独立记录。
