# 三天基线第二轮：原生组合通过，基线在春2中午阻断

日期：2026-09-21。依据《三天基线复核与第二轮优化方案》。阶段 A，单玩家、DeepSeek Flash、正常时间；没有启动 R-B1、阶段 B 或 14 天验收。G3 没有设置阈值。

## 1. 结论与停止事实

**F1—F7 已落地；要求的五项原生组合检查通过。独立三天基线未通过：只完成春1，春2 12:50 停止，0 次重启。不能计算为两天或三天通过。**

代码提交 `7b064ce`。基线存档 `AgentLab_449657228`，run `dee6d014527b4aca994d28a5eb53e54a`，从原生春1、500金、初始工具开始。原始证据：`work/round2-baseline-3days-1/`；组合证据：`work/round2-combined-3/`。运行数据不进 Git。

停止原因是**菜单操作没有效果却被回报为 input_sent，模型反复点击未解锁背包格**，不是旧的 relief_depth 未复位。制作保护正确拒绝后，模型从通用菜单入口执行了原生制作，箱子滞留手持栏。

自动 RootFailureWatch 没有停下这类无错误码的操作。本次是观察者确认同令牌同选择连续三次无变化后调用 `agent_pause`；最终日志显示暂停落地前又执行了第4次。这是停止时效缺口，不能写成自动监控通过。监督脚本随后退出，`result.json` 的 `RuntimeError:lab_pause` 是暂停的结果；业务原因和暂停前后快照另存 `manual-three-noop-stop.json` / `manual-three-noop-paused.json`。

这次没有临时改规则、清空背包、丢物、改时间、人工关菜单或续跑。暂停后仅做只读分析与报告。

## 2. 五项组合证据及边界

| 项目 | 结果 / 证据强度 | 原生证据 |
| --- | --- | --- |
| F1 信息闭环 | **实测＋域检查**：强压缩仍保留容量标量；world.read 返回真实对象；基线52份请求关键标量均在 | `round2-combined-3/observation-before.json`；基线 `model-audit.json` |
| F2 重复恢复 | **实测**：同一父任务完成20次捕获，3次原生存货共34件，最终 succeeded；未重新派任务冒充续作 | `round2-combined-3/long-fishing.json`，command `work:1621c7ab48ad4240a9337e6d1800c3f2` |
| F2 真嵌套 | **原生状态下调用纯核心检查＋静态确认**：depth=1返回 relief_depth_limit，未写物理约束；上限1保留 | `observation-before.json` 的 nested / nested_is_physical_constraint；CapacityChecks |
| F3 声明重复 | **实测**：三次不同 attempt，同角色/根因/版本；第三次真实拒绝后从 running 变 paused | `third-refusal-before-stop.json` / `third-refusal-paused.json`。这是错误回执场景，不覆盖菜单无效果 |
| F4 出行卸载 | **实测**：已回农场，整备把5件无关工具及2件渔获放箱，保留鱼竿和允许的补给，然后海滩捕获成功；before/after完整库存守恒 | `departure-before.json` / `departure-latest.json`；原生 `loadout_transferred` 的 id=`round2-departure` |
| F5 获取能力 | **实测**：缺鱼竿时出现 acquire:fishing；读真实邀请信、原生剧情授予鱼竿 | `capability.json` / `native-willy-mail.json` / `rod-event-after-mail.json` / `native-rod.json` |

此前组合尝试1暴露邮件前置缺失；尝试2成功完成18次捕获，但进食腾格导致只有1次存货，覆盖不足。尝试3仅把合法任务参数改成20次、max_food=0，原生物资不变，完成3次卸货。没有伪造满包。

F6 在组合尝试2真实途经捡到1件珊瑚，没有修改路径。初版 Point 坐标序列化为空，之后已改为数组；本次新基线没有触发可核验的途经拾取，因此**新坐标字段的原生覆盖仍待验证**，不报成全覆盖。

组合 DLL 指纹为 `2bcae0ea972b610b3862df4400212f62571fe68bbb6455bb58908cbde0715a54`；基线为 `c6b76d2e630238a3133ce1ff4036c749dac7f688a03acced7cb49ed7ae387747`。后者增加按帧运输分类、显式物理根因字段、拾取坐标数组；未改组合动作链。构建0警告0错误，338条域断言、3项 Python 根因监控检查通过。构建与域检查不替代原生证据。

## 3. 新阻断：三处接缝缺陷

### 3.1 制作保护被通用菜单绕行（实测＋静态确认）

春2 12:50：背包12/12，木材79，没有仓库。模型创建 `craft:Chest`、completion=placed 的目标。

`PlayerProduction.cs` 的 `CapacityAdapter.After` 正确预测：消耗50木材后仍余29，占用原格；没有位置接收箱子，返回 `capacity_no_free_slot`。**这个执行器没有扣料。**但它此前已打开 CraftingPage，失败后菜单仍在。

模型随后 `menu.choose(c41)`。`NativeMenuTools.Choose` 直接转发原生点击，没有执行同样的容量/物资保护校验。原生制作消耗50木材，产生1个手持箱子。原生材料变化为79→29，菜单 held=`(BC)130,count=1`，原生 crafting Chest 计数为1。箱子没有进入背包、更没有放置，不能算目标完成。

需要修的是统一执行边界：通用菜单不能成为绕过同一容量、授权及保护校验的入口。不能删除手持物或强行塞入背包来解围。

### 3.2 未解锁格被标成空格（实测＋静态确认）

`NativeMenuTools.cs:43-46` 遍历 `InventoryMenu.inventory` 的显示格；越过 `actualInventory.Count` 的格传 null 给 ItemInfo，于是生成：

```
c12 → inventory:inventory:12 {"empty":true}
```

这是从0起算的第13格；玩家实际只有12格。原生 `InventoryMenu.cs:318` 对 `num >= actualInventory.Count` 拒绝接收。原生文件在 `work/dual-body-spike/native/InventoryMenu.cs`。

**F1 的 free_slots=0 是正确且完整可见的；错误来自另一份菜单观察把未解锁格叫作空。**模型还将 ingredients=False 解释成材料充足，进一步放大了问题。

### 3.3 输入已发送被当成有效进展（实测＋静态确认）

`NativeMenuTools.Choose` 返回 `input_sent`，没有核验菜单/手持物/库存是否变化，也未登记无效果根因。RootFailureWatch 只处理 error 或 failed，因此没有收到可计数失败。

原始响应 UTC：09:04:15.505、09:04:37.426、09:04:58.690、09:05:21.039；四次都是 token=`F29A1E4630109431`、id=`c12`。回执令牌、手持箱子、库存均未变。前三次后观察者准备暂停，第四次在暂停落地前执行。应由动作回执层核验并拦停，不能依赖人工观察的采样间隔。

原始 response/call 在 `native-logs/61b9b3092c5d41eea99b86ecbf500034/model-dee6d014527b4aca994d28a5eb53e54a.jsonl` 和 `day-1.jsonl`；`model-audit.json` 保留逐条来源行号。

## 4. A—E 测量（完整1日＋部分第2日）

### A. 时间分解

| 范围 | 劳动 | 运输 | 等模型 | 等自然生长 | 受阻 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 春1 06:00—实际入睡18:50，770游戏分钟 | 13.18% | 32.75% | 13.39% | 0% | 40.69% |
| 春2 06:00—停机12:50，410已观测游戏分钟，**非完整日** | 21.82% | 55.24% | 5.65% | 0% | 17.30% |

春2没有实际入睡，不计算正式 AwakeLaborRatio，不填造完整日数据。菜单暂停时游戏时间不走，第二行不能体现全部现实等待：停机快照另记 native_menu 138.30秒、native_event 39.16秒、model_wait 19.27秒。不能把原生剧情等待和无效菜单卡死混称“发呆”。

时间按每个游戏时钟区间的实际帧状态分配，不等于最优生产效率。原有“移动也算 progressing_work”的旧有效劳动口径与表中 labor 不同，两套字段均保留供复核，不能混用。G3 仍待用户在可信完整基线后决定。

### B. 失败、存货、睡眠

| 指标 | 春1 | 春2部分 |
| --- | ---: | ---: |
| 真实存货 / 出行整备转移 | 0 / 0 | 0 / 0 |
| 显式 store 任务 | 1 | 1 |
| 失败任务 | 9 | 5 |
| 声明期拒绝 | 4 | 1 |
| 安全降级 | 0 | 0 |
| 入睡 | 18:50，体力22 | 未入睡 |
| verified_actions 增量（原生生产回执口径） | 97 | 37，未结算 |

春1执行根因：active_cleanup_order_required×5、known_failure_conditions_unchanged:target_abandoned_today×1、no_matching_targets×1、cleanup_insufficient_remaining_allowance×1、capacity_all_candidates_infeasible×1。声明根因：不在农场规划、invalid_cleanup_request、容量重复拒绝、invalid_plan_task各1。

春2执行根因：work_interrupted_by_menu、no_matching_targets、event_interrupted_read_menu、capacity_all_candidates_infeasible、capacity_no_free_slot各1；声明容量重复拒绝1。

5次清理单错误分布在容量版本8/48/86，同版本最多2次，未触发三次规则。这说明当前“全部错误按容量版本分组”对非容量错误的条件相关性也值得复核，不能直接把它们当成同条件5次漏停。

本轮没有真实仓库存放，重复 store 是无可用仓库时的尝试。没有观察到 relief_depth_limit 被写为物理约束。春2容量恢复候选完整列明：无批准加工/交付/制作、无可吃补给、无仓库、扩容预测无格、出售与升级未授权。这是实际开局仓储准备过晚，不是再次发生深度锁。

### C. 现金、库存、作物、资产

| 项目 | 开局 | 春1结算 | 春2停机 |
| --- | ---: | ---: | ---: |
| 现金 | 500 | 500 | 500 |
| 已到账收入 / 购买支出 | 0 / 0 | 0 / 0 | 0 / 0 |
| 木材 | 0 | 67 | 29（制作前79，原生消耗50） |
| 石头 / 纤维 | 0 / 0 | 18 / 23 | 70 / 49 |
| 混合种子 | 0 | 2 | 2 |
| 煤 / 晶洞 | 0 / 0 | 0 / 0 | 3 / 1 |
| 防风草 | 0 | 连片3×5共15株、已浇水 | 15株，第二天已浇水，未成熟 |
| 鱼竿 | 无 | 无 | 原生 Willy 剧情授予竹竿 |
| 已部署生产/仓储资产 | 0 | 0 | 0；另有手持箱子1，未部署 |

春1库存可售价值下限193金，不能称为现金利润；未成熟作物、不可售工具不按未来收入估价。春2未结算，Quality 的 CashEnd/AssetsEnd 默认0不是实际余额，不引用它们。没有任何完整“种→收→售→再投资”闭环。

### D. 模型、调用与成本

| 项目 | 上轮 | 本轮 |
| --- | ---: | ---: |
| 原始请求/响应 | 50组 | 52组（含1次HTTP522） |
| 记录 token 的响应 | — | 51 |
| 输入 token | 447,235 | 503,115 |
| 输出 token | — | 11,677 |
| 缓存命中输入 | — | 140,160 |
| 缓存命中率 | 30.11% | 27.86% |
| known_failure 声明拒绝 | 14 | 2 |
| 作废响应 | 5 | 2 |
| 工具回执 | 93 | 85 |

输入总量增加12.49%，规定的 known_failure 空转下降，但新增菜单无效果空转未被这个指标涵盖。因此**不能宣称成本优化成功**，也没有自行加大压缩。两个运行的终止时点不同，原始总量仅用于暴露问题，不能作为同等经营产出的收益对比。

春1输入307,788、输出8,103、命中28.11%；春2部分输入195,327、输出3,574、命中27.46%。模型异常2次：一次非法双JSON回复、一次HTTP522，后续自动恢复；没有重启游戏。没有转移中断/守恒失败。

F1逐请求审计：52份请求缺失关键标量数=0；包含world.read/plan.read/day.plan的历史观察共13处，均为实际数据，指针0处。

完整工具分布（作为常规报表项）：

| 工具 | 次数 | 工具 | 次数 |
| --- | ---: | --- | ---: |
| work.run | 32 | farm.cleanup | 8 |
| menu.read | 8 | menu.choose | 6 |
| farm.plan | 5 | player.travel | 4 |
| plan.submit | 3 | plan.read | 3 |
| agent.wait | 3 | farm.business_status | 3 |
| day.plan | 2 | world.read | 2 |
| player.collect_home_gifts | 1 | player.sleep | 1 |
| knowledge.search | 1 | tools.lookup | 1 |
| player.read_mail | 1 | goal.create | 1 |

shop.read/player.buy/player.procure/farm.economy/farm.autonomy/farm.business/day.routine、knowledge.get/goal.requirements/progress.*/quest_board.read/player.accept_quest均为0。分日完整计数在 analysis.json 的 days[].tool_calls。

**零采购的原因是组合性的，有证据区分：**

- policy.Enabled 与 routine.Enabled 始终 false，模型多次明确说未启用、不投资。一次 `tools.lookup` 用于读取邮件工具，没有开启经营政策。
- 模型能主动去种子店，但到店后没有打开商店、读取报价或购买；提交过“购买子任务的 tool=plan.submit、args={}”的无效计划。口头“现货充足”没有原生报价支持。
- F5 报价候选依赖“已批准目标存在缺口”。首次批准目标直到春2中午才出现，而且是材料齐全的箱子。之前没有形成种子采购目标，因而没有有据可执行的采购候选。候选的安全限制正确，但经营目标→批准缺口的上游链仍缺。
- 能力获取候选确实被采用，拿到了鱼竿；不能以此推出采购、接任务和经营政策都已有效使用。

### E. 声明期整备形状

三种 loadout_shape_unsupported（多箱、建造、部分交付）均0次。本轮没有覆盖这些形状，0不是证明它们不重要；没有提前实施阶段B功能。

## 5. F7：运输取证，未改算法

按现有动作状态互斥分类的游戏分钟：

| 类别 | 春1 | 春2至停机 |
| --- | ---: | ---: |
| 跨地图动作 | 41.32 | 87.25 |
| 农场内移动 | 191.80 | 139.22 |
| 仓储/整备移动 | 0 | 0 |
| 其他本地移动 | 19.04 | 0 |
| 合计运输 | 252.16 | 226.47 |

口径限制：player.sleep 的返家路段目前可能按所在地图归入农场内/其他本地，不能将“跨地图动作”解释成全部往返行程耗时。“无效折返”是上述运输的效益子集，不可再相加；没有精确按帧反推它的秒数，报告保留 null，避免用几次跨图就捏造浪费时长。

**明确的低收益往返（实测）：**春1约14:00出发，14:50进入SeedShop，18:20离开，18:50回家入睡。期间无原生采购/交付/生产，现金不变。到店后只有世界/菜单/经营状态查询、失败计划和等待。确认1次没有完成采购目的的往返，不把夜间必要回家本身算作错误。

**有实际收益的往返（实测）：**春2 Farm→Beach→Farm，完成获取鱼竿；不能因地图重访就判无效。所有逐次跨图 origin/destination/task/purpose/source 在 `analysis.json.transport.crossings`，重访带“不是浪费证明”标签。

**清理目标序列（实测）：**32份 cleanup_route、131个规划目标，含物体Tile、劳动Stand及每段Walk；308份实际寻路route_segment含路径节点。春1清理目标31个、步数中位数1、最大86；春2部分100个、中位数1、最大67。最大长段是任务/区域切换：

- 春1 09:30：农场(17,49)→(64,30)，86步，开始另一清理子任务。
- 春2 06:00：住宅出口(64,15)→(25,24)，58步，前日general订单抢先恢复。
- 春2 08:10：田边(61,25)→(13,30)，67步，浇水完成后恢复general清理。

**结论：**多数同片区的小目标很近，长距离主要出现在任务区域切换。春2出现“西侧清理→东侧浇水→西侧清理”的折返，说明同地点工作与每日农务没有提前合并安排。已有BFS未改，清障寻路未开发。

证据中 planned_path 是路线计划，actor_route 是采样后的实际位置，二者分别保留；不能把计划路径长度当成实际总里程。完整目标序列在 `analysis.json.transport.cleanup_target_sequences`，逐段轨迹在 segments。

## 6. 卡顿拆分

- 动作交接141次：中位2.916ms、P95 2296ms、最大42280ms。最大值实际来自春1 10:00清理结束到11:00新采木任务开始，跨父任务，期间有重规划与失败；不是一次挥工具耗时，更不是寻路计算。原始 work_handoff 可按父任务对齐，报表已分离同父任务、跨父任务和未归属样本。
- 同父任务子动作交接129次：中位2.655ms、P95 11.678ms、最大48.314ms；跨父任务10次中位2614.879ms，另有2次未归属。当前主要长停顿在任务间，不是所有连续劳动子动作都慢。
- 模型响应51次：中位2135ms、P95 3033ms、最大3500ms。HTTP522另列；约20秒重新唤醒间隔不是单次HTTP耗时。
- 寻路：308次搜索，按原生动作汇总149份；动作内搜索总耗时中位0.103ms、P95 2.016ms、最大20.787ms。它不是每次单独搜索的分位值。
- Together 主线程 Update：春1P95 0.815ms、P99 2.660ms、最大559.497ms；春2部分P95 0.741ms、P99 1.601ms、最大88.030ms。不是整游戏帧率。
- 最大559ms发生春1 06:00，schedule 213.7ms、context 83.1ms、snapshot 84.2ms等；春1另有402.8ms无阶段细分样本。因此不能宣称思考卡顿已经消除。原生挥工具动画、剧情、移动、无动作闲置有独立 wall_detail_seconds。

## 7. 建议下一轮范围（仅建议，未实施）

1. 修复菜单观察的可操作性：可用格/锁定格区分；ingredient可行性结构化，不把False藏在字符串；隐藏不可执行选择或明确原因。
2. 通用菜单的制作/购买路径复用业务容量、保护与授权检查；正常生产拒绝且没有held物时干净退出自己打开的菜单，避免留下保护绕行入口。
3. 对菜单输入核验实际变化，按菜单语义/物理状态合并无效果根因；第三次在执行控制层停机并留证，不能等待观察者。令牌仅证明观察未过期，不能证明点击有效。
4. 生产事务显式追踪原生手持产物；“已经制成但未入包/未部署”不得退回“重新采50木材制作”。保护所有物资，不以丢弃/强塞兑现预测。
5. 开局仓储和农务的依赖次序：在占满最后格前规划真实仓储；跨日先兑现必需农务，再续可选清理；合并区域任务减少上述三条长距离调动。
6. 把“经营方向/预算→批准需求→报价/采购”接实，保留工具按需发现但让模型拿到可执行合同；核验采购与日常政策是否真的启用，不只改提示词说“勤快”。

其中1—4是这次停止直接指向的阻断。不能通过继续压缩上下文、提高失败次数或跳过守恒来消除。三天数据尚不齐备，仍不能定G3、跑14天或进入阶段B。

## 8. 交接文件与复算

- 改动与组合记录：[三天基线第二轮实施与验证](./三天基线第二轮实施与验证.md)。原依据文件和其他未跟踪资料保持原状。
- `work/round2-baseline-3days-1/analysis.json`：A—E、工具分布、路径序列、耗时、来源行号。
- `model-audit.json`：52组请求/响应索引，逐请求标量、候选、政策、计划、用量。
- `manual-three-noop-stop.json`：停机前完整世界、背包、菜单；paused文件验证暂停。
- `native-logs/`：原始请求响应、85条工具回执、2696条业务事件；`diagnostics.jsonl`、`samples.jsonl` 提供中途状态。
- `native-save/` 只到实际保存的春2早晨，不能冒充中午存档；中午状态以停机快照为准。
- `forensic-summary.json`：部分日口径与清理分布；`evidence-index.json` 为文件指纹，排除本地控制凭据。

复算命令：

```sh
python3 scripts/report_survival_baseline.py --logroot work/round2-baseline-3days-1/native-logs --run dee6d014527b4aca994d28a5eb53e54a --out work/round2-baseline-3days-1/analysis.json
python3 scripts/audit_baseline_model.py --logroot work/round2-baseline-3days-1/native-logs --run dee6d014527b4aca994d28a5eb53e54a --out work/round2-baseline-3days-1/model-audit.json
```
