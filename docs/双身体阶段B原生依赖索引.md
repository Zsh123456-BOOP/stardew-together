# 双身体阶段 B 原生依赖索引

本文件只整理阶段 0 静态依赖，**不代表阶段 B 已开始**。源文件为 `work/dual-body-spike/native/`（当前原生 DLL 反编译）；证据强度与覆盖范围逐项列出。迁移时按符号检索，游戏版本变化后重新确认行号。

| ID | 原生位置/符号 | Game1.player 依赖及影响 | 阶段 0 覆盖与缺口 |
| --- | --- | --- | --- |
| N01 | `InventoryMenu.cs:90–96` | 默认绑定 `Game1.player.Items`，按当前玩家容量补空槽 | B 构造制作菜单会经过，故必须连构造都放在作用域内 |
| N02 | `CraftingPage.cs:87` | 垃圾桶等级从全局玩家读取 | B 构造 |
| N03 | `CraftingPage.cs:115` | 配方列表按全局玩家的已知制作配方过滤 | B 构造、列出 Chest |
| N04 | `CraftingPage.cs:233` | 烹饪配方是否已知影响显示 | 烹饪未验证 |
| N05 | `CraftingRecipe.cs:136、140` | 读取当前配方制作计数、捕蟹笼职业条件 | 前者属于 B 构造；职业分支未验证 |
| N06 | `CraftingRecipe.cs:164–171` | 原料数量来自全局玩家和可选容器 | 候选/交互路径；未单独插桩 |
| N07 | `CraftingRecipe.cs:280–286` | 消耗 **Game1.player.Items** 中的材料，没有显式 who 参数 | B 原生消耗 50 木材 |
| N08 | `CraftingPage.cs:485` | 对全局玩家触发 `NotifyQuests(OnRecipeCrafted)` | B 原生处理器 |
| N09 | `CraftingPage.cs:486–488` | 增加全局玩家的 `craftingRecipes[recipe]` | B 证明增加的是隐形 Farmer 的计数 |
| N10 | `CraftingPage.cs:497` | `Game1.stats.checkForCraftingAchievements()` | B 原生处理器；此处 `Game1.stats` 本身指向 `player.stats` |
| N11 | `Stats.checkForCraftingAchievements` | 从 `Game1.player.craftingRecipes` 汇总，原生写入自身 itemsCrafted 并可能发成就 | 本次只做第一个箱子，未验证达成成就阈值的分支 |
| N12 | `CraftingPage.cs:499–501` | 手柄模式下把产物加入全局玩家背包 | 本次不是该分支；探针把 heldItem 交给明确的 bot 背包 |
| N13 | `CraftingPage.cs:404–406、418` | Shift 入包、丢弃 heldItem 都读取全局玩家 | 未调用玩家 UI 操作路径 |
| N14 | `CraftingPage.cs:588–617` | 回收价、垃圾桶渲染读取全局玩家 | 实验菜单未显示，未验证 |
| N15 | `InventoryMenu.cs:262、409–449、531–545` | 回调、停止持有动作、当前工具与容量显示读取全局玩家 | 未验证这些交互/绘制分支 |
| N16 | `CraftingRecipe.cs:205、340–394、468–532` | 特殊烹饪规则、额外原料/调味料、配方提示与历史读取全局玩家 | 未验证烹饪/提示分支 |
| N17 | `CraftingPage.cs:492–493` | `cookedRecipe` 与烹饪成就检查指向全局玩家 | 未验证烹饪 |

补充检索：`Character.GetToolLocation`（工具/坐标）、`Farmer.IsLocalPlayer` 与 `addItemToInventoryBool`（身份门槛）、`Debris.updateChunks`（最终拾取两轴≤64）、`Debris.findBestPlayer`（location.farmers 注册）、`Tree.getLastFarmerToUse/performTreeFall`（GetPlayer 查找/经验归属）。

实测边界：箱子原生制作、一次树的10次挥斧、12个木材chunk逐个拾取及交接已通过。成就阈值、烹饪、UI交互、注册/持久化等未验证。每一类迁移仍须记录作用域内材料来源、进度归属、退出后引用与后续帧。

Squad 对应 S01–S21 见[穿刺报告](./双身体阶段0穿刺报告.md)的冲突表；尤其不要遗漏旧任务执行器里的经验转记与公共货袋。
