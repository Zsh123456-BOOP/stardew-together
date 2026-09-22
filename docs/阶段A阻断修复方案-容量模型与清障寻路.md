# 阶段 A 阻断修复方案：容量模型与清障寻路

日期：2026-09-21。本文是 Claude 对 [给Claude的阶段A停工交接与优化请求](./给Claude的阶段A停工交接与优化请求.md) 的答复方案，**不代表任何修复已实现或已验收**。实施由 Codex 承接。

依据：[双身体陪玩架构方案](./双身体陪玩架构方案.md)（D2/D5/D6、阶段划分、14 天闸门）、[阶段A连续存活实施与验收报告](./阶段A连续存活实施与验收报告.md)、本次对代码的独立复核。

---

## 1. 对本轮结果的判定

**阶段 A 的降级机制是成立的，本轮是真进展**：连续原生过夜从 3 天提到 10 天，0 次重启，模型异常降级与崩溃后自动加载检查点均取得实测证据。这些不推翻。

**但 10 天不等于阶段 A 通过，而且不能只按"差 4 天"理解。** 关键是降级触发时间：

| 游戏日 | 降级时间 | 判读 |
| --- | --- | --- |
| 春 1 | 12:40 | 模型不可用降级，机制正常 |
| 春 2、3、4、7 | 13:20、19:30、21:10、10:30 | 当日有实际劳动 |
| **春 5、6、8、9、10** | **06:30、06:40、06:30、06:40、07:00** | **起床后半小时内就进入降级** |

后 5 天实际是：**醒来 → 撞同一个容量死锁 6 次 → 回去睡觉。** 它们是"存活"了，但不是"经营"。

因此本方案的目标不是"再加 4 天"，而是**消除死锁并给验收补上经营质量指标**，否则 14 天同样可以靠早睡刷出来。

---

## 2. 根因复核

### 2.1 交接报告定位的两处时序缺陷——确认成立

**`mod/Together/StorageExpansion.cs:63`**（`TryStartStorageExpansion`）：

```csharp
if(!recipe.doesFarmerHaveIngredientsInInventory())return false;
CompactPlayerStacks();
if(!Game1.player.Items.Any(i=>i==null))throw new InvalidOperationException("storage_expansion_requires_one_crafting_slot");
```

实测状态：12 格满，其中一格是 50 木材。造箱子的配方恰好消耗 50 木材 → **那一格会被清空，正是它要求的空格**。检查跑在消耗之前，于是永远抛异常。

**`mod/Together/PlayerProduction.cs:54`**：

```csharp
var expected=recipe.createItem();
if(!Game1.player.couldInventoryAcceptThisItem(expected))throw new InvalidOperationException("craft_output_capacity_required");
```

同一类错误：在扣料之前检查产物能否入包。

**两处是同一个 bug 的两个实例**：*在消耗发生之前，检查消耗之后的状态*。

### 2.2 真正的病灶：容量逻辑没有单一模型（本次复核新增）

这是交接报告没有量化的部分。当前容量判断散落在至少 24 个调用点，各自一套逻辑、各自一个错误码：

| 写法 | 数量 | 代表位置 |
| --- | --- | --- |
| `couldInventoryAcceptThisItem(...)` | **18 处** | `PlayerProduction.cs:54`、`PlayerMachines.cs:40`、`PlayerShopping.cs:46/57`、`PlayerCrabPots.cs:54`、`PlayerAnimalProducts.cs:22/47`、`NativeRewards.cs:25`、`PlayerLostItem.cs:37`、`PlayerInformation.cs:35`、`PlayerBeach.cs:31`、`TreeProduction.cs:34`、`PlayerPickup.cs:53`、`PartnerCargoTransfer.cs:22`、`InventoryWork.cs:66` 等 |
| `Items.Any(i => i == null)` | 3 处 | `StorageExpansion.cs:63`、`InventoryWork.cs:153`、`DailyAgendaRuntime.cs:75` |
| `freeSpotsInInventory()` | 2 处 | `BusinessProduction.cs:78`、`ManagedWorkRuntime.cs:11` |
| 独立小助手 | 2 个 | `StorageTiming.NeedsRoom`、`OperationsState.RequiredFreeSlots` |

后果：

- 每个点只看**当前瞬时**空格，没有任何点会模拟"这段操作过程中容量怎么变"。2.1 的两个 bug 是这个缺陷的必然产物，不是偶然写错。
- 每个点抛自己的错误码（`craft_output_capacity_required`、`machine_output_inventory_full`、`storage_expansion_requires_one_crafting_slot`…），于是**同一个物理根因在失败记忆里表现为十几个不同失败**，各自独立计数到 6 次。这解释了日志里"同类失败六次"为何反复出现在不同工具上。
- 品质分堆问题（K 轮已暴露过"多种品质的鱼被当作同一物品估算空间"）无法在单点修复，因为没有共享的堆叠模型。

**结论：只修 2.1 的两处会让春 5–10 的死锁解开，但不会消除这类缺陷的再生。修复必须收敛到一个模型。**

### 2.3 失败记忆绑定错了维度

`FailureKnowledge.Key(actor, tool, args, location)` 以工具与参数为键。`Family()` 虽有 `capacity` 分类，但键仍按工具分裂。

后果：一个"满包且无可用仓库"的物理事实，被记成 N 个工具各自的失败，每个各撞 6 次才降级。日志里的"同类失败六次"是这个机制的输出，不是模型不听话。

`SurvivalState.NewDay()` 会 `Abandoned.Clear()` 并重置 `ModelFailures`。**日期变化本身清除了失败事实，而容量条件并没有变**，于是次日重新进入同一循环——这正是春 5–10 的形态。

### 2.4 降级质量没有指标

`SurvivalState` 只记 Mode/Reason/SleepAttempts，没有任何"今天是否做了有价值的事"的度量。因此 06:30 降级和 21:10 降级在验收上完全等价。这是 14 天门槛可以被"早睡刷天数"满足的原因。

---

## 3. 修复 R-A1：统一容量模型

### 3.1 设计原则

容量不是标量。**它是一段有序变更上的不变式，必须逐步检查，而不是只看起点或终点。**

对任一段操作，按真实顺序模拟：`取料 → 扣料 → 产出 → 入包 → 交付/存放`。区分：

- **峰值占用**（过程中最高）——决定会不会中途卡住
- **终值占用**（结束时）——决定后续任务还剩多少空间

造箱子的例子：起点 12/12 满，扣 50 木材后 11/12，入箱后 12/12。峰值=终值=12，中途有空位 → **可行**。旧代码只看起点 → 判不可行。

### 3.2 纯核心 + 适配器（为满足域检查约束）

`tests/Together.DomainChecks.csproj` 只引用 Newtonsoft.Json，不能引用游戏类型。所以必须拆两层：

**纯核心**（新增 `mod/Together/CapacityModel.cs`，不 using StardewValley，可域测试）：

```csharp
public readonly record struct StackKey(string Id,int Quality,string Variant);

public sealed class CapacitySnapshot {          // 规划副本，永不写真实库存
    public CapacitySnapshot(int slotCount,IEnumerable<(StackKey Key,int Stack,int MaxStack,bool Protected)> slots);
    public int SlotCount {get;}
    public int FreeSlots {get;}
    public int Occupied {get;}
    public CapacitySnapshot Clone();
    public bool TryTake(StackKey key,int count,out string reason);   // 扣料，可清空槽位
    public bool TryPut(StackKey key,int count,int maxStack,out string reason); // 入包，遵守堆叠上限
}

public abstract record CapacityOp;
public sealed record TakeOp(StackKey Key,int Count):CapacityOp;
public sealed record PutOp(StackKey Key,int Count,int MaxStack):CapacityOp;
public sealed record RequireFreeOp(int Slots):CapacityOp;

public sealed record CapacityVerdict(bool Feasible,int FailedStep,string Reason,
    int PeakOccupied,int FinalOccupied,int FreeSlotsAtEnd);

public static class CapacityPlan {
    public static CapacityVerdict Simulate(CapacitySnapshot start,IReadOnlyList<CapacityOp> ops);
}
```

**适配器**（`mod/Together/CapacityAdapter.cs`，依赖游戏）：

- `CapacitySnapshot Of(Farmer who)`：从 `who.Items` 提取槽位，`maximumStackSize()` 取上限，按预留政策标 `Protected`。
- `StackKey` 的构造必须与原生 `canStackWith` 一致。**做不到就保守**：构键后对同键物品实际调用 `canStackWith` 交叉校验；若不一致，把该槽位标为不可堆叠（`MaxStack=当前Stack`）。**宁可低估容量，绝不高估。**
- 伙伴迁移后同一适配器接 `BotFarmer.Items`（D6）。**阶段 A 只接玩家路径；纯核心保持 actor 无关，为阶段 B 复用做好准备，但不在阶段 A 实现伙伴侧。**

### 3.3 迁移：24 个调用点收敛

按风险分三批，每批独立验证：

| 批次 | 调用点 | 说明 |
| --- | --- | --- |
| 第一批（阻断修复） | `StorageExpansion.cs:63`、`PlayerProduction.cs:54` | 改为 `Simulate([TakeOp(料...), PutOp(产物)])`。这两处修完春 5–10 的死锁应当解开 |
| 第二批（产出类） | `PlayerMachines`、`PlayerAnimalProducts`、`PlayerCrabPots`、`TreeProduction`、`PlayerPickup`、`NativeRewards`、`PlayerLostItem`、`PlayerInformation`、`PlayerBeach` | 统一改为操作序列；品质分堆在此处一并解决 |
| 第三批（交易与交接） | `PlayerShopping`、`PartnerCargoTransfer`、`InventoryWork`、`BusinessProduction`、`ManagedWorkRuntime`、`DailyAgendaRuntime`、`StorageTiming`、`OperationsState.RequiredFreeSlots` | 收敛助手函数，删除重复实现 |

**规划用副本，执行仍走原生扣料与入包。** 不得为"兑现预测"直接改真实库存——这条是零伪造红线的延伸。

**错误码统一**：所有容量失败归并为少量根因码（见 R-A3），不再每个调用点自造一个。旧错误码若出现在 `RecoveryPolicy.WaitCodes` 中，需同步更新映射，不要留下失效字符串。

---

## 4. 修复 R-A2：腾格动作决策器（打破死锁）

### 4.1 候选动作表

容量不足时不是只能"存箱"。按成本与收益排序，**能同时推进目标的优先**：

| 序 | 动作 | 净收益 | 成本 | 权限要求 |
| --- | --- | --- | --- | --- |
| 0 | 堆叠整理 `CompactPlayerStacks` | 合并腾出的格 | 0 | 无，**总是先做** |
| 1 | 投入机器（已备料） | +料格 | 走路 | 已批准生产项目 |
| 2 | 交付任务/订单 | +物品格 | 走路 | 已接任务 |
| 3 | 制作消耗材料 | 料格 − 产物格 | 材料 | 该配方有已批准用途 |
| 4 | 按补给政策食用 | +1 | 食物 | 非预留、非献祭品 |
| 5 | 送回可达仓库 | +存入格 | 往返 | 有可达标记箱 |
| 6 | 制作并放置新箱 | +箱容量 | 木材＋往返 | `Storage.AutoExpand` 与预算 |
| 7 | 出售真实余量 | +售出格 | 物品 | **需显式授权**（`surplus`） |
| 8 | 原生购买背包升级 | 永久 +12 格 | 金币＋往返 | 预算与 `keep_gold` |

**禁止**：为腾格吃掉献祭品/任务物、卖掉工具或已承诺材料、乱送礼、丢弃物品。不引入任何默认销毁。稀有物与任务预留物先保护。

### 4.2 反递归规则（本方案最关键的一条）

死锁的形态是：*满包 → 要存箱 → 要造箱 → 造箱要空格 → 满包*。防止它的规则：

1. **每个候选动作在被选中前，必须用 `Simulate` 验证其自身前置在当前状态下可行。**不可行者不进入选择集，并记录排除原因。
2. **腾格解析深度上限为 1。** 一个腾格动作不得再触发腾格解析。若某候选的前置本身需要腾格 → 直接排除，不递归。
3. **所有候选都不可行时，返回结构化阻碍**，逐条列出每个候选被排除的原因，并给出可行的替代工作（例如"改做不产生掉落的浇水/照料"）。**不得反复派同一个必失败动作。**
4. 决策器只做上表的 0–6；**7（出售）和 8（背包升级）需要模型或政策显式授权**，算法不自行决定。

### 4.3 仓库选址与动线

沿用现有 `StorageExpansion` 的选址算法（它已计算住宅/主田/加工区路程并保护通道，这部分是好的）。补充：**不要用增加箱子掩盖不合理动线**——若最近可达箱往返成本超过阈值，应优先候选 8（背包升级）或 1/2（就地消耗），并在日志里记录比较依据。

---

## 5. 修复 R-A3：失败记忆按根因约束

### 5.1 容量约束的键改为（根因 × 行动者）

```csharp
public sealed record CapacityConstraint(
    string Actor,
    string RootCause,        // no_free_slot | no_stackable_room | no_reachable_storage
                             // | all_candidates_infeasible | all_items_protected
    long CapacityVersion,    // 解除条件
    string[] ExcludedCandidates,  // 每个腾格候选被排除的原因
    int Day,int Time);
```

一个根因**约束所有其 op-plan 需要容量的工具**，在派单前就拦截，不再让每个工具各撞 6 次。

### 5.2 解除条件是容量版本号，不是日期

`CapacityVersion` 单调递增，仅在下列事实变化时 +1：

- 该行动者的槽位占用发生变化（数量或堆叠结构）
- 新增一个可达的标记仓储
- 背包升级完成
- 预留/保护集合变化
- 新增一个已批准的材料用途（使候选 3 可行）

**日期变化本身不增加版本号，也不清除约束。** 这要求修改 `SurvivalState.NewDay()` 的 `Abandoned.Clear()` 语义：瞬时故障可按日清除，**容量这类条件性约束必须按解除条件清除**。

### 5.3 与降级的关系

- 容量根因约束成立期间，调度器不应把需要容量的任务派出去 → 不产生 6 次失败 → 不触发当日降级。
- 应改派不需要容量的有效工作（浇水、照料动物、走访、社交、清理不产生掉落的杂草）。
- 只有当**所有**不需要容量的有效工作也都没有时，才允许考虑收工，且要写明剩余工作与阻碍。

---

## 6. 修复 R-A4：降级质量与验收指标

### 6.1 新增决策日志字段

每个游戏日记录：

```
day, effective_labor_minutes, available_minutes, verified_actions_delta,
degradation_time, degradation_reason, capacity_constraints_active[],
blocked_work[], cash_delta, asset_delta, inventory_turnover
```

`effective_labor_minutes` 定义为处于"有进展任务"状态的游戏分钟数。等待自然生长、合理休息、业务停滞必须分开计，不能混为一类。

### 6.2 验收门槛：原门槛不变，新增经营指标

**原门槛（不改，不豁免）**：连续 14 个游戏日无人介入，0 次重启，从独立验收起点重跑，**不得拼接本轮 10 天**。

**新增经营门槛**（与原门槛明确区分，防止早睡刷天数）：

| 编号 | 指标 | 阈值 |
| --- | --- | --- |
| G1 | 06:00–12:00 触发安全降级的天数 | **= 0** |
| G2 | 连续两日 `verified_actions` 增量均为 0 | **不存在** |
| G3 | 每日有效劳动时间占可用时间 | **待定**，见下 |
| G4 | 14 天末现金＋资产 vs 初始；完整"种→收→售→再投资"闭环 | 现金＋资产净增长；**至少 1 次**完整闭环 |

**G3 的阈值不由本方案拍定。** 现在没有可信基线——本轮后 5 天接近 0%，前 4 天数据受死锁污染。要求 Codex 在阻断修复后先跑 **3 天基线测量**，报告实测分布，再与用户共同确定阈值。**不允许先设一个好看的数字再去凑。**

---

## 7. R-B1：清障寻路（放在 14 天闸门之后）

用户需求明确：遇到普通、允许清除、工具能处理的障碍，应挖开走近路，而不是一律绕行。这个需求**保留且要做**，但**不放在 14 天闸门之前**。

### 7.1 为什么排在闸门之后

1. **它不是本轮任何一次失败的原因。** 10 天日志里的降级全部来自容量，没有一次来自绕路。
2. **清障会产生掉落 → 直接加剧当前的容量瓶颈。** 在容量模型修好之前引入新的掉落源，会让死锁更频繁。
3. **一次只改一个大系统然后验收。** 若同时改容量和寻路再跑 14 天，失败将无法归因。

结论：**R-A1–R-A4 → 14 天闸门 → R-B1 → 阶段 B。**

### 7.2 设计（评审交接报告第 6 节的方案：整体成立，补四条）

交接报告的六点设计我认可，按原样实施。补充四条约束：

1. **清障掉落必须进容量模型。** 一条清障路径在提交前，要用 `Simulate` 验证沿途掉落装得下；装不下则先解析腾格，或改选绕行路径。这是 R-A1 与 R-B1 的接口。
2. **优先选清障本身也是有用劳动的路径。** 砍成熟树得木材、碎石得矿石属于双收益；清杂草只有通行收益且会再生。同等成本下选前者。
3. **单条路径的清障次数设硬上限**（建议 ≤3 个障碍），防止退化成"挖穿整张地图"。超限则绕行。
4. **工具等级不足 = 无限成本**，不是高成本。巨石、树桩在工具不达标时必须视为不可清除节点，不能反复尝试打不动的目标。

复用入口按交接报告所列：`PlayerRouteController.cs`、`PlayerExecutor.cs` 的 Walk/Passable/站位、`SemanticWork.cs`、`FarmLayout`/`FarmDistrict`。Squad 的 `AStarPathfinder` 与 `FollowerManager` 移动保留，**不替换整个导航系统**。

---

## 8. 优先级与阶段边界

| 编号 | 内容 | 归属 | 是否阻塞 14 天闸门 |
| --- | --- | --- | --- |
| R-A1 第一批 | `StorageExpansion.cs:63`、`PlayerProduction.cs:54` 时序修复 | 阶段 A | **是** |
| R-A1 第二、三批 | 24 个调用点收敛到统一模型 | 阶段 A | **是** |
| R-A2 | 腾格决策器与反递归 | 阶段 A | **是** |
| R-A3 | 容量约束按根因，解除按版本号 | 阶段 A | **是** |
| R-A4 | 降级质量日志与 G1/G2/G4 | 阶段 A | **是** |
| G3 阈值 | 3 天基线测量后与用户确定 | 阶段 A | 是（需先测量） |
| R-B1 | 清障寻路 | 闸门之后 | 否 |
| 容量模型接 `BotFarmer` | 伙伴侧迁移 | **阶段 B** | 否 |
| Squad 货袋替换为 `BotFarmer.Items` | D6 迁移 | **阶段 B** | 否 |
| 日志重复写入 | 独立小缺陷 | 随手修 | 否 |

**不豁免 14 天闸门。** 不接受拼接本轮 10 天。

---

## 9. 验证组合

### 9.1 域检查（纯核心，离线）

`CapacityModel.cs` 是纯算法，必须有域检查，并且**记得在 `tests/Together.DomainChecks.csproj` 手写 `<Compile Include>`**（该项目 `EnableDefaultCompileItems=false`，漏加不会报错）。

- 满包 + 恰好 50 木材 → 造箱可行；峰值/终值正确
- 扣料不足以腾格 → 判不可行，`FailedStep` 指向 `PutOp`
- 不同品质不合堆 → 分别占格
- 分批制作：峰值出现在中途而非终点
- 上限 999 / 上限 1（工具、大件）的边界
- 保护物不计入可腾空间
- 反递归：所有候选不可行 → 返回完整排除原因，不抛栈溢出

### 9.2 原生检查（AgentLab，三重门禁）

1. 满包且恰有 50 木材：原生造箱 → 正常放置 → 卸货 → 恢复原任务；全程核验物品与配方计数守恒
2. 制作不足以腾格、品质不合堆、分批制作/收货、菜单 heldItem 余物、仓库满、部分交接、多种掉落
3. 同根因跨日未解除时不重复派必失败工作；日期变化不清除容量约束
4. 模型失效仍能降级过夜；22:00 守护真实触发；未知菜单策略
5. **R-A1 第一批修完后必须单独验证"原生制作确实通过"**——交接报告已声明这一点尚未验证，不能假定改了检查顺序就一定通过。需实测 `clickCraftingRecipe` 的 heldItem、批次数与原生扣料顺序。

### 9.3 基线测量

阻断修复后先跑 **3 天基线**，报告 G3 所需的有效劳动时间分布，再与用户确定阈值。这 3 天不计入 14 天验收。

---

## 10. 仍是假设、需要 Codex 先验证的判断

本方案中以下判断是**静态确认或推断，不是实测**，实施前应先取证：

| 判断 | 强度 | 如何验证 |
| --- | --- | --- |
| 修好两处时序检查后原生制作会通过 | **推断** | 9.2 第 5 条，单独实测 |
| 春 5–10 的降级全部由容量根因导致 | **实测日志＋代码，但归因未逐日插桩** | 按运行 ID 过滤业务日志逐日核对根因码 |
| 24 个调用点全部可收敛到同一模型 | **静态确认清单，未逐点评估** | 第二、三批迁移时逐点确认；确有特殊语义的要写明为何不收敛 |
| `StackKey` 能与 `canStackWith` 一致 | **待验证** | 交叉校验；不一致时按 3.2 保守降级 |
| 容量根因约束能消除 6 次重试 | **推断** | 原生检查 9.2 第 3 条 |

---

## 11. 补充裁决：整备实现缺口的优先级（2026-09-21，R-A1 落地后）

Codex 报告三项整备声明缺口：**多箱取料、建造、部分交付**，并要求判断优先级。

### 11.1 裁决

> **不在 14 天闸门之前补齐实现；但必须保证这三种形状失败时是「声明期干净拒绝」，绝不走到转移阶段。**

### 11.2 依据

本次复核发现：阶段 A 的失败分级已生效，非夹具停机路径从 8 类降到 4 类（`loadout_transfer_interrupted`、`logging_failed`、`multiplayer_not_supported`、`quality_evidence_failed`、`survival_return_failed_three_attempts`）。"连续失败六次"和"模型回复非法"已成功降级。

**但 `TaskPreparation.cs:198` 仍是唯一一个业务原因导致全场停机的路径：**

```csharp
if(transferring)PauseAutoplay("loadout_transfer_interrupted:"+e.Message);
else CompleteScheduled(task,...failed...);
```

多箱取料正是最可能撞上它的形状——跨多个箱子转移，中途任一异常即停机。因此：

- 缺口本身不阻塞 14 天（那些活动不做，运行仍能继续）
- **但缺口经由 `loadout_transfer_interrupted` 会变成停机，那就阻塞**

所以正确的修复不是实现三个功能，而是**一个声明期前置检查**——成本低一个数量级。

### 11.3 R-A5：整备声明期拒绝（阶段 A，阻塞闸门）

1. **声明期判定支持范围。** 整备计划生成时判断形状是否受支持：单箱取料、已支持的交付类型。不支持的形状（需跨 >1 个箱子、建造材料、部分交付）**在第一次转移发生之前**拒绝，返回 `loadout_shape_unsupported:<形状>`，并登记为根因约束（复用 R-A3 机制，避免重复派工）。
2. **调度器改派不需要该整备的有效工作**，不反复尝试。
3. **绝不出现「已转移一部分才发现不支持」。**

### 11.4 R-A6：区分可恢复中断与守恒失败（阶段 A，阻塞闸门）

当前 `transferring` 期间的**任何**异常都触发停机，但这些异常语义不同：

| 异常 | 性质 | 应有处理 |
| --- | --- | --- |
| `loadout_conservation_failed` | 守恒无法核验 | **停机**（符合 `SurvivalPolicy.Fatal` 的 `conservation`） |
| `loadout_stock_changed`、`loadout_storage_busy`、`loadout_native_capacity_changed` | 世界变了，但物品守恒仍可核验 | **可恢复**：重读实际库存 → 核对守恒 → 恢复或干净放弃 |

要求：中断后**先核对守恒**。守恒成立 → 按可恢复处理，不停机；守恒不成立或无法核验 → 停机保留证据。

**不得为了减少停机而跳过守恒核对。** 这是零伪造红线的延伸。

### 11.5 归属阶段 B

三项缺口的完整实现留到阶段 B，届时用 14 天数据判断各自实际出现频率，再决定投入顺序。**不要在阶段 A 提前实现。**

---

## 12. 禁止事项（继续有效）

零伪造进度；规划用副本、执行走原生；`ActingFarmer` 单 tick、无 await、菜单限制、恢复断言；AgentLab 三重门禁；域文件显式注册；上游只走锚点补丁；重复阻断停止规则。**不得为完成验收篡改材料、金钱、日期、统计或记录；不得放宽 14 天闸门。**
