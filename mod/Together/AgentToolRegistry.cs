using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed class AgentToolRegistry {
    private readonly ModEntry mod;
    private readonly PlayerExecutor player;
    private readonly NativeMenuTools menus=new();
    public AgentToolRegistry(ModEntry mod,PlayerExecutor player){this.mod=mod;this.player=player;menus.ProfessionSelected=mod.RememberProfession;}
    public void Reset()=>menus.Reset();
    public static bool IsPlayerMutation(string name)=>name.StartsWith("player.") || name.StartsWith("menu.") && name!="menu.read";
    public static readonly Dictionary<string,string> Catalog=new(){
        ["beach.read"]="{}: 海滩桥梁和老水手实际状态。",
        ["player.beach"]="{mode:bridge|pendant,budget?:int,keep_gold?:int}: 自动走到海滩修桥或购买美人鱼吊坠，原生消耗300木材或5000金；等待原生动画，核验结果。",
        ["family.read"]="{}: 读取原生婚姻、孩子、预产期及持久家庭策略；未配置生育策略时不替模型决定。",
        ["strategy.family"]="{partner?:原生婚恋人物名,accept_children:bool,target_children:0..2,child_names:[string],auto_name_animals:bool}: 保存生育及命名策略；同意生育须预先提供目标数量的不同名字。只处理原生触发事件，不增加孩子或跳过等待。",
        ["menu.text"]="{token:string,text:string,submit?:bool}: 向刚读过的原生命名菜单输入1..24字名字，可原生确认；不支持任意菜单状态改写。",
        ["strategy.profession"]="{skill:0..4,level:5|10,profession:0..29}: 持久保存职业方向（技能编号为游戏原生顺序），检查前后分支一致性；夜间遇到真实对应选项自动原生选择，不修改已有职业。未配置或冲突仍需模型选择",
        ["agent.status"]="{}: 玩家/伙伴队列与连续劳动阶段、原生等待、长期目标阻碍、记忆时间线/归档、模型请求与预算；区分等待和无任务",
        ["usage.read"]="{}: 今日模型预算、API报告Token、失败请求保留额度、剩余和请求大小上限；额度外部持久记录，读档不回退，不等同于人民币账单",
        ["player.recruit_companion"]="{npc}: 实际走近角色并通过Squad邀请同行，遵守当前招募设置和人数条件，核验伙伴存在；经营模式自动更新农务分工。不会传送NPC或修改好感",
        ["player.tap_tree"]="{output,location?:Farm,tapper?:(BC)105}: 根据原生树木数据选成熟树，安装真实树液器或收取指定已成熟产物；计时保持原生，安装不等于获得树脂",
        ["farm.business"]="{enabled?:bool,expand?:bool,budget_per_day?:int,keep_gold?:int,max_animals?:0..96,max_machines?:0..200,feed_days?:2..28}: 持续经营政策，日常双角色照料、种植投资、饲料补给、机器投料收货、余量销售、按供给扩建畜舍与加工产能；预算共享，正常时间，不追逐成就",
        ["farm.business_status"]="{}: 实际经营账本、现金/待结算/在制品、产能投资选项、预计回收期依据与阻碍；待结算金额不可支出",
        ["player.acquire_animal"]="{type,name,location?:AnimalShop,budget,keep_gold}: 真实到牧场柜台选择动物、自动选已完工兼容畜舍并命名购买，核验原生动物和费用",
        ["player.procure"]="{location,shop,item,count,max_unit_price,budget,keep_gold,recipe?:bool}: 真实前往商店、读取现场商品并按上限原生采购，自动收货关菜单；配方与普通商品可区分；缺货/闭店返回原因",
        ["shop.sources"]="{item,recipe?:bool}: 静态游戏数据中的潜在商店及地图；不是当前报价，采购时现场重新核价",
        ["progress.pursue"]="{targets?:1..128个原生目标ID,enabled?:bool,budget_per_day?:int,keep_gold?:int,gift_value_per_day?:int,gift_value_limit?:int,income_shipping?:bool,income_keep_per_item?:0..999,nuts_per_day?:0..130,keep_nuts?:0..130,route?:community|joja}: 持久推进已绑定制作/烹饪、建筑、完美度分项、关系、钓鱼/蟹笼、献祭/Joja、修船、馆藏、交付、出货、地牢、锻造与街机目标；gift_value为礼物可售价值预算而非扣款，默认0仅聊天；算法备料和排队，按真实进度核验，每日金额预算先预留。暂停取消未执行依赖，未知后期目标仍明确阻碍",
        ["progress.dependencies"]="{id:成就/配方/任务/物品ID,depth?:1..8,limit?:10..600}: 展开原生证据依赖图、数量/品质/替代分支与具体工具入口，明确未适配和截断；只读不授予进度",
        ["capabilities.read"]="{}: 27类能力的已接工具、角色、核验方式及明确缺口；存在工具不代表完整验收",
        ["memory.search"]="{query?:string,actor?:string,limit?:1..20,offset?:int}: 检索本存档已归档事件/回执，不含读档后的未来记录；返回证据ID和截断提示",
        ["memory.evidence"]="{id:string,offset?:int}: 按归档证据ID读取原文，每页最多4000字符；继续next_offset能读完整记录；不能访问本存档时间线之外的历史",
        ["map.scan"]="{actor_id?:string,offset?:int,limit?:1..120}: 指定角色当前地图完整对象/地形分页，含树木、作物和状态版本；不会把局部地图截断当资源不存在",
        ["player.eat"]="{slot:int}: 吃真实背包的一份普通食物，原生动画/恢复/消耗核验；保留物由高层补给政策决定",
        ["player.craft"]="{recipe:原生配方名,count?:1..99}: 连续制作指定批次，自动原生菜单/材料消耗/成品入包/统计核验；只用背包原料，缺料需先取货",
        ["player.cook"]="{recipe:原生配方名,count?:1..99}: 回已升级住宅厨房烹饪，使用真实背包原料与原生烹饪统计；目前需已解锁家中厨房",
        ["shop.read"]="{offset?:int,limit?:1..100}: 读取当前已打开原生商店完整分页的实际货品、价格、货币、条件和库存；不远程打开商店",
        ["crab_pots.read"]="{}: 自有蟹笼、水域类型、真实产物和缺饵状态",
        ["player.crab_pots"]="{mode:place|tend,location?:地图ID,item?:目标蟹笼鱼QID,bait?:鱼饵QID,count?:0..40}: 算法选择合适海/淡水岸边放置真实蟹笼；tend收取原生产物并补饵，0处理当前地图全部。核验放置/投饵消耗与玩家捕获统计；须原生过夜生成捕获",
        ["fishing.options"]="{item:鱼QID,location?:地图ID}: 按真实时间、季节、天气、等级、原生解锁、鱼竿检查可钓地点；返回限制，不预测随机下一竿",
        ["player.fish"]="{item?:指定鱼QID,count?:1..20,reserve_stamina?:15..270}: 在当前可钓地图自动寻找可达岸边，真实蓄力/抛竿/咬钩/反馈控杆/收鱼；按Farmer原生捕获计数核验，次数/背包/体力/时间受限，特殊奖励菜单需继续处理；指定item只选择条件合格水域，count仍按实际所有捕获计数；定向收集用work.run(goal=fish,item,count)",
        ["player.read_mail"]="{count?:1..40}: 自动走到自家农场邮箱，逐封原生阅读、领取附件、接受附带任务、学习邮件配方；满包保留菜单，保存信件内容和实际解锁证据",
        ["player.watch_tv"]="{channel?:cooking|weather|fortune|tips|fishing}: 回家走近真实电视，选择当天实际可用频道、翻页阅读，记录节目和新增配方；无节目则正常退出，不远程授予配方",
        ["transport.read"]="{}: 读取真实巴士/船解锁、票价、司机与修船材料条件",
        ["player.transport"]="{route:desert|from_desert|island|from_island,budget?:int,keep_gold?:int}: 自动走到真实售票或回程触发点、按预算购票，等待原生乘坐动画并核验实际到达；不直接传送，缺前置或司机报告具体原因",
        ["player.repair_boat"]="{part:hull|anchor|ticket_machine}: 自动走近Willy船的对应部件，核验背包材料与预留后原生捐料；修复登记不代表已过夜竣工",
        ["orchard.read"]="{}: 当前地图真实果树、生长阻碍、成熟天数与果实",
        ["player.orchard"]="{mode:plant|harvest,location?:Farm,item?:果树苗QID,count?:int}: 自动选择满足原生间距、生长净空与通道的果树地块并原生种植；harvest走近摇树、实际走动收果核验，0收全部可达果树。满包停止要求存货",
        ["mastery.read"]="{}: 读取真实精通经验、可用精通点及五技能领取状态",
        ["player.treasure"]="{}: 当前地图自动走近真实非玩家宝箱，原生开箱/奖励菜单、核验物品或骷髅钥匙等原生能力，背包不足保留现场",
        ["island.walnuts"]="读取已加载岛屿地图的未收灌木/埋藏核桃、原生收集记录和随机掉落池",
        ["player.walnuts"]="{location,count?:0..30}: 高层核桃采集，自动找真实未收灌木或埋藏点、走近摇树/原生挥锄、捡取掉落和核验收集数；0表示该图全部可观察此类目标，不等于全岛谜题完成",
        ["volcano.read"]="读取当前火山层、冷却熔岩、开关与出口，不生成未知后续层",
        ["player.volcano_step"]="{mode:enter|advance|retreat}: 底层按当前地图计算可通行/可浇水/可碎石路线，走动踩开关和真实过层；长期行程调用work.run volcano_trip，避免LLM逐格调度",
        ["island.upgrades"]="读取原生姜岛鹦鹉建设、核桃余额、前置邮件和状态",
        ["player.island_upgrade"]="{location,upgrade,budget_nuts,keep_nuts?:0}: 按观察到的鹦鹉建设ID自动走近、原生付核桃、等待施工动画并核验完成；不包含GoldenParrot金币代找核桃",
        ["forge.read"]="读取已打开原生锻造台的材料、有效性、煤渣费用及真实结果",
        ["player.forge"]="{left_slot,right_slot,count?:1..3,budget_shards,location?:Caldera}: 自动前往原生锻造台，装入真实武器/工具/戒指和材料、正常锻造动画、核验煤渣/材料/产物并回包；须已具备可达通路，随机附魔不保证结果",
        ["arcade.read"]="读取当前街机原生状态和通关统计，不修改进度",
        ["player.arcade"]="{game:prairie|kart,mode:continue|new|deathless|progress|endless,seconds:60..7200,attempts:1..10,target_score?:50000}: 自动前往酒吧原生街机，状态驱动移动射击/跳跃，有限时间与重试；new/deathless明确重新开始草原王；只有原生通关统计新增才成功，endless核验自然结算提交分数达到target_score，不等于进度模式通关",
        ["player.mastery"]="{skill:0..4}: 自动前往已开放精通洞窟对应石碑，原生领取指定精通奖励，核验点数/技能登记/配方；不授予经验或解锁，预留奖励背包空位",
        ["player.read_book"]="{slot:int,item?:预期书籍QID}: 使用真实已持有书籍，原生动作消费一份并等待阅读动画；核验能力/经验/新配方，不直接增加统计",
        ["joja.read"]="{}: 读取真实会员、建设项目、影院与待施工状态；已打开发展表时读取原生价格",
        ["player.joja"]="{route:joja,mode:membership|project|cinema,project?:原生项目邮件ID,budget:int,keep_gold?:int}: 明确Joja路线后原生走访、对话选择和付款，核验扣款及原生申请；施工仍等待次日，不代替互斥路线选择",
        ["player.place_facility"]="{item:设备物品ID,location?:Farm,goal_id?:string}: 自动为携带的设备选合法空位，保护农田/已有物件/门口和设施通路，走近原生放置并核验",
        ["order_donations.read"]="{}: 读取原生特殊订单投递箱、当前接受的背包物品、实际计数/期限",
        ["player.order_donate"]="{order:实际订单ID,dropbox:实际投递箱ID}: 自动跨图走到原生投递箱，按原生条件投递背包合格非预留物品，核验守恒并确认；回执不把单次投递伪称整项订单完成",
        ["player.ship_items"]="{items:[{item:物品ID,count:1..999,quality?:最低品质}]}: 自动回农场出货箱，原生菜单按清单数量出货并保留其他数量/预留物资；核验箱子和背包，次日才入账",
        ["player.attach"]="{tool_slot:int,slot?:int,mode?:attach|detach}: 原生背包给鱼竿/弹弓装配饵料、浮标或弹药，detach卸下原生顺序第一个附件；旧附件回包并核验守恒，不改变耐久或物品数量",
        ["equipment.read"]="{}: 读取当前戒指/鞋帽服装/精通饰品、工具附件与背包",
        ["player.equip"]="{target:left_ring|right_ring|boots|hat|shirt|pants|trinket,mode?:equip|remove,slot?:int}: 原生背包装备更换，旧装备归包并核验；卸下需要空位",
        ["quest_board.read"]="{}: 读取实际已打开每日/特殊/齐先生任务板的任务条件和可接取状态",
        ["player.accept_quest"]="{id:刚读取的任务ID}: 在实际任务板选择指定任务，核验真实日志/特殊订单新增",
        ["animals.read"]="{}: 实际农场动物ID、种类、主人、位置、畜舍、照料、繁育和出售报价",
        ["player.animal"]="{id:真实动物ID,mode:rename|sell|move_home|reproduction,name?:string,min_price?:int,home_x?:int,home_y?:int,enabled?:bool}: 自动寻找并走近指定自有动物，通过原生菜单改名/明确最低价出售/迁居到指定畜舍/繁育设置并核验",
        ["player.geodes"]="{item?:限定矿球ID,count?:1..40,budget:int,keep_gold?:int}: 已打开铁匠加工菜单后连续原生开矿球，每颗25金，保留产物空间并核验消耗与结果；不跳过动画",
        ["animal_shop.read"]="{}: 读取实际动物商店可购买种类/价格/条件及畜舍容量",
        ["player.buy_animal"]="{type:实际商店种类,name:唯一名称,budget:int,keep_gold?:int}: 已打开动物商店后按原生流程选种类、可容纳畜舍、命名付款，核验真实入住",
        ["player.upgrade_house"]="{budget:int,keep_gold?:int}: 按当前房屋等级自动走到木匠、核对原生报价和材料、确认升级，核验真实扣料开工；不跳过工期",
        ["player.mine_access"]="{mode:enter|skull|leave|elevator,level?:int}: 自动走到真实入口/返程梯/电梯并通过原生菜单换层；电梯仅已解锁5层倍数，普通入口到1层",
        ["player.bundle"]="{bundle:原生献祭ID,budget?:int,keep_gold?:int}: 自动到社区中心或废弃Joja实际纸条、打开指定收集包，按品质和原生数量提交齐全材料并领取奖励；金库需显式预算，普通献祭预算0。缺项返回实际缺口，原生修复剧情是后续事件",
        ["construction.read"]="{blueprint?:原生建筑ID}: 读取当前真实木匠菜单的图纸/预算/工期；指定图纸返回保持通道的自动选址",
        ["player.build"]="{blueprint:观察到的图纸ID,budget:int,keep_gold?:int}: 已原生打开木匠菜单后自动选址/升级原建筑，真实扣材料金币并跟踪开工；预算必填，不拆除占地物",
        ["player.donate_museum"]="{item?:QID,count?:int}: 在已原生打开的博物馆捐赠菜单自动选择未捐物品与空展位，实际消耗和馆藏核验；count=0捐全部允许物品",
        ["player.collect_reward"]="{}: 当前已观察的原生奖励菜单逐项领取（非普通箱子），按原生规则入包/学习/获取特殊奖励；满包保留菜单并报告阻碍，绝不清空或丢弃奖励",
        ["player.service"]="{location:string,service?:shop|build|upgrade_house|upgrade_tools|animals|geodes|claim_tool|museum_donate|museum_reward|daily_quests|special_orders|qi_orders,shop?:商店ID}: 沿真实路线前往并查找地图服务柜台，原生交互/普通对话/选择已指定服务；shop需shop ID。核验实际商店/服务菜单打开，claim_tool核验原生归还。只打开服务不表示已完成购买或建造",
        ["player.machine"]="{mode:load|collect,location?:string,machine?:设备ID,item?:原料ID,count?:0..100,goal_id?:string,output?:目标产物ID}: 到指定地点依次接近真实机器投料或收货，加载按原生规则消耗背包原料/燃料并核验，收货核验入包数量；0遍历当前符合条件设备。投料完成不等于产物出炉；缺料/满包须补给后再调",
        ["player.care"]="{mode?:pet|milk|shear|feed,count?:0..100}: 自动到真实动物所在地，逐只接近并原生抚摸/挤奶/剪毛/筒仓取草与食槽放草，0处理所有符合条件动物；需要真实工具、体力与背包，夜间停止；原生产物/照料状态核验",
        ["player.claim_reward"]="{quest_id:string}: 自动打开原生日志、找到已完成未领奖任务或订单、点击领取金币奖励、核验收入与已领取状态；不会直接设置完成或加钱",
        ["player.social"]="{npc:原生人物名,mode?:talk|gift|deliver|relationship,item?:预期物品ID,slot?:int,quest_id?:string}: 自动跨图寻找并接近真实NPC，原生聊天/送礼/任务交付；送物需slot，交任务需活动quest_id，完成后核验原生关系或指定任务。普通对话自动翻页，分支选择保留；relationship显式允许求婚等特殊物品，不保证对方接受",
        ["orders.read"]="{}: 读取实际特殊订单每个目标的索引/条件/进度/失败条件/期限；投递与新采集、钓获、出货计数分别处理。",
        ["player.find_lost_item"]="{quest_id:已观察任务ID}: 自动走到原生失物地点，寻找已生成的真实任务物品、走近拾取及确认提示，按itemFound和入包核验；不会生成失物或更改任务标记。",
        ["player.combat"]="{order_id?:特殊订单ID,objective?:目标索引,quest_id?:已观察讨伐任务ID,count?:0..50,min_health?:20..200}: 指定quest_id时优先真实目标敌人且按该任务计数；选择背包近战武器，当前地图持续接近/原生挥击/换目标；0清理当前已出现怪物，原生击杀归属核验，低生命或无伤害停止请求撤退/换策略；不宣称已有全部敌种战术",
        ["player.mine_descend"]="{}: 当前矿层自动找到已揭露且可达的真实梯子，走近原生交互并核验换层；未发现梯子先采矿/战斗，不直接生成通道或改层数",
        ["player.buy"]="{shop:string,item:物品ID,count:int,max_unit_price:int,budget:int,keep_gold?:int,currency?:0|1|2|4,keep_currency?:int,trade_item?:ID,trade_budget?:int}: 当前原生商店连续采购；count为购买次数，支持配方学习、ClintUpgrade升级启动、金币/节日积分/赌场币/齐钻及物品兑换，核验真实消耗和原生结果。默认金币，其它货币须显式指定currency和保留额；兑换需trade_item与trade_budget。配方或升级一次只买1份，自定义购买回调需专属核验",
        ["storage.policy"]="{auto_expand?:bool,max_shared_chests?:0..32,wood_budget_per_day?:0..999}: 设置并读取自动扩容政策，默认最多4个共享箱/每日100木材；始终通过原生制作放置，预算与目标材料保护生效",
        ["storage.configure"]="{location?:string,x:int,y:int,role:output|none}: 给已观察的真实玩家箱设置同行收货/取货标记；不转移物资；work.run(goal=withdraw,item=ID,count=数量,quality=最低品质)自动去共享箱取货",
        ["farm.autonomy"]="{enabled?:bool,budget_per_day?:int,keep_gold?:int,plots?:1..96,max_daily_manual_water?:0..96,priority?:income|collection|low_labor,shop?:SeedShop,location?:SeedShop}: 持续每日按实际现金与现场报价重新投资，自动采购/布局/取种/播种，失败保留原因；默认关闭",
        ["farm.economy"]="{budget?:int,keep_gold?:int,plots?:1..96,max_daily_manual_water?:0..96,priority?:income|collection|low_labor}: 真实农场快照+今天shop.read观察报价，后台计算混合种植/采购/预留/照料负担，返回plan_id；不先花钱",
        ["farm.economy_status"]="{plan_id:string}: 查看后台经济规划及条件现金流；过日或读档必须重算",
        ["farm.execute"]="{plan_id:string}: 幂等把规划的采购/共享箱取种子/实际布局播种接入队列，真实预算/供货/占地再次核验",
        ["farm.plan"]="{seed?:物品ID,fertilizer?:已持有作物肥料ID,count?:1..96,max_daily_manual_water?:0..96,require_scarecrow?:bool,priority?:income|collection|low_labor}: 在农场/温室按已有种子和真实可达地形生成地块方案，保护出入口、工作站位，架子作物检查种下后可达性；返回plan_id，work.run(goal=plant,plan_id=...)自动翻土播种浇水补水。读取已施肥料/职业/临水水稻与跨季生长条件，返回最多3个排序方案和收获/次日现金预测；当前不采购种子、不优化机器加工，不把预测当实收。",
        ["perfection.read"]="{}: 原生完美度11类实绩、权重、关联目标、原生总分和豁免券分开读取；不是平台成就核验",
        ["progress.catalog"]="{kind?:achievement|crafting|cooking|quest|order|route|bundle|house|boat|shipping|mastery|book|scope,offset?:int,limit?:1..80}: 当前原生目标及配方分页，含依赖、材料、完成证据、缺口；按next_offset继续，未知条件不能猜",
        ["plan.read"]="{}: 持续任务队列、revision、双角色独立状态和真实回执；queued不是完成",
        ["plan.submit"]="{submission_id:string,expected_revision:int,tasks:[{id:string,actor:player或真实actor_id,tool:string,args:{},after?:[任务id],location?:string,day?:绝对day,not_before?:HHMM,deadline?:HHMM,purpose?:string}]}: 一次提交1到24步，允许player动作与companion.assign；同角色依次执行，不同角色并行。当前日默认，最远7天；跨地图后动作写明location；未观察的参数先查询。重复submission_id幂等。",
        ["plan.cancel"]="{ids:[任务id]}: 取消指定任务；保存开始后不可取消。失败后取消受阻旧计划，再根据真实状态提交新任务",
        ["plan.archive"]="{}: 清理已结束且不再被依赖的任务记录，保留在用依赖与全局核验计数",
        ["day.routine"]="{enabled?:bool,assignments?:{orchard?:player,mail?:player,cooking_tv?:player,crab_pots?:player,water?:角色ID,harvest?:角色ID,feed?:角色ID,pet?:角色ID,milk?:player,shear?:player,animal_collect?:player}}: 保存跨日农务分工；每晨按真实缺项生成独立角色队列，无需模型重复派同样农活，首次默认未启用",
        ["day.read"]="{}: 今日农务、任务、材料缺口、可达工作候选与时间/体力预算；每批完成自动刷新",
        ["day.plan"]="{priorities:[string],resources?:[{item:string,count:int,purpose:string}]}: 保存1至8项优先事项和最多8项目标库存（总量，非增量）；按实际库存核验",
        ["world.read"]="{}: 日期、环境、农场、伙伴actor_id及真实candidates、共同目标",
        ["map.read"]="{actor_id?:player或真实伙伴ID,x?:int,y?:int,radius?:1..20}: 指定角色所在地图局部格子、障碍、作物、矿物、交互、真实出口；坐标可用于移动与操作",
        ["inventory.read"]="{}: 玩家背包slot、ID、数量、工具；含手持物",
        ["knowledge.search"]="{query:string}: 原生百科模糊检索",
        ["knowledge.get"]="{query?:string,id?:string}: 百科详细证据与实时条件",
        ["goal.requirements"]="{id:string}: 已有百科物品/配方条目的需求与现有库存",
        ["goal.run"]="{id:共同目标ID,mode?:run|pause}: 持续按依赖采集/取料/制作/烹饪/加工；无需模型重复下发子步骤。失败暂停自动目标并返回阻碍，pause保留目标且取消其待执行任务，正在执行需action.cancel",
        ["goal.create"]="{request_id:string,entity:物品ID或craft:配方名或cook:菜名,count?:int,quality?:0|1|2|4,allow_new_facilities?:bool,completion?:owned|crafted|cooked}: 幂等创建持久共同目标，原生配方自动展开依赖并预留材料，不打开UI",
        ["goal.prepare"]="{id:共同目标ID}: 根据真实库存生成下一批可执行任务（共享箱取料、普通资源收集、原生制作）；返回tasks可直接交plan.submit；每批后重新核算，未接通路线返回gaps",
        ["progress.missing"]="{}: 按原生Data/Achievements列出未完成条目的名称、描述与ID；不等同于平台全成就检查",
        ["progress.roadmap"]="{}: 原生成就、实际技能与下一阶段建议；建议不是已经完成的成就，按季节与前置条件并行安排",
        ["progress.read"]="{}: 玩家原生任务、技能、配方计数、邮件、成就；不是Steam成就证明",
        ["work.run"]="order_id+objective（索引）绑定实际订单的采集/钓鱼/矿洞目标，排除fail_on_completion；quest_id可绑定真实采集/钓鱼/讨伐委托（仅玩家），使用本任务实际计数，达标后停止；mine_trip可选start_level为已解锁5倍数电梯层，探索真实生成地图。fish按item目标鱼种和count实际原生新增捕获数量连续钓鱼，自动选地图/水域/抛竿力度、补给/卸货/续作；省略item任意鱼。volcano_trip自动补给/装水/冷却熔岩/碎石/踩开关/跨层，target_level:1..10，10为Caldera；矿洞可选region:normal|skull,target_level为区域内层数，travel_budget明确授权巴士票预算；{actor_id?:player或真实伙伴ID,goal:storage_expand|fish|volcano_trip|mine_trip|milk|shear|animal_collect|pet|feed|tend|collect|process|withdraw|plant|resource|hardwood|stone|wood|fiber|water|refill|harvest|forage|clear_dead|store,item?:物品ID,quality?:int,plan_id?:string,target_level?:1..120,include_trees?:bool,max_food?:0..10,location?:真实地图名,count?:int,reserve_stamina?:15..270,until?:HHMM<=2300}: 高层持续劳动，无需坐标/工具槽/target_id。mine_trip限玩家，目标target_level默认下一5层里程碑；自动原生入矿/电梯/寻找梯子/挖石/近战/吃补给，深度达标或体力生命时间不足时走真实返程梯退出；普通矿1..120，骷髅矿和火山使用各自行程与原生通道。resource需item，自动寻找原生铜铁金铱煤/宝石等矿点；hardwood寻找等级允许的树桩/树干（限玩家）。石/木/纤维/矿物/硬木count为本次实际新增物品数量（默认20），其它count为目标数；pet/feed玩家与伙伴可用；milk/shear/animal_collect限玩家，自动跨畜舍照料/收取地面和自动采集器产品、补给和卸货续接；tend/collect/process批量调用Squad生产控制器，仅伙伴；process表示从原料箱补机器，完成投料不代表成品出炉。0在可核验已清空时完成，缺候选但条件不明会报告阻碍。自动走到指定地图、选工具、逐次寻路换目标，玩家浇水自动补水再继续。木材默认树枝，include_trees=true时玩家可砍成熟未挂树液器的普通树；plant用farm.plan返回的plan_id执行布局，plant/clear_dead/refill限玩家；NPC无原生体力条，按能力/货物/时间限制。中断或部分完成返回实际数量与stop_reason，不伪报达标。默认保留20体力、22点停止；体力不足可吃最多max_food份普通非预留食物（默认3），然后续作；采集前自动留2空槽，不足则寻找Farm及畜舍output箱卸货再回来；不足按storage策略原生制作放置新箱。store可主动存货；保留工具/种子/补给，预留材料可入共享箱保持用途保护，不会丢弃出售。可取消/查进度。",
        ["player.work"]="{skill:water|till|plant|fertilize|harvest|clear|chop|break_clump|clear_dead|forage,slot?:int,tiles:[{x:int,y:int}]}: 最多36格同图农活/资源收集（clear支持石块用镐、树枝用斧、杂草用镰刀；clear_dead仅镰刀清理枯死作物，不清理活苗；forage仅拾取真实野生采集物）；自动寻路、工具动画、逐格核验；避免每格请求模型",
        ["player.move"]="{x:int,y:int,location?:明确地图}: 原生寻路走到当前地图目标；返回动作ID",
        ["player.travel"]="{location:string}: 按实际出口/建筑门前往已加载地点；锁门会失败",
        ["player.use_tool"]="{slot:int,x:int,y:int}: 使用实际工具击打相邻格，保留动画与原生结算",
        ["player.interact"]="{x:int,y:int,slot?:int}: 邻格原生交互，如收获、NPC、机器、门、矿梯",
        ["player.place"]="{slot:int,x:int,y:int}: 使用真实持有的种子/可放物品，原生判定及消耗",
        ["player.ship"]="{slot:int}: 在真实农场出货箱旁，将指定槽位整叠可售物品投入出货箱；次日原生结算，不提前加钱",
        ["player.sleep"]="{reason:string,review?:string}: 正常经营须先查看day.read；提前休息必须说明替代活动为何不可行，有可行工作时拒绝。 回家、真实床位、睡眠确认、结算、保存、第二天；须选择的夜间菜单用menu工具",
        ["menu.read"]="{}: 原生菜单文本、可选响应、组件id、token、手持物；不使用截图",
        ["menu.open"]="{page:inventory|crafting|journal}: 打开相应原生菜单",
        ["menu.choose"]="{token:string,id:string,right?:bool}: 点击刚读取的原生组件，过期token拒绝；返回菜单状态，不声称业务完成",
        ["menu.scroll"]="{direction:up|down}: 原生菜单滚动一页",
        ["menu.close"]="{}: 仅当原生允许安全关闭且无手持物时关闭",
        ["companion.assign"]="{actor_id:string,skill:string,target_id?:string,destination?:string,seconds?:int}: travel 必须带 destination，只负责到达；mine/water/harvest/forage/clear/collect/pet/till/plant/feed/tend/buy/ship/gift/refill/deposit 必须带当前 world.read 候选的 target_id；劳动不能用 destination 代替目标。follow/stay 切换模式，guard/rest/fish 可带 seconds。跨图劳动分两轮：travel 成功→读取新候选→派劳动。",
        ["action.status"]="{id:string}: 动作真实进度和前后证据；也接受 plan 的任务 id，排队状态不是完成",
        ["action.cancel"]="{id:string}: 取消尚可取消的动作，已消耗物资不回滚",
        ["agent.wait"]="{seconds:1..60}: 等待游戏进展，期间不重复请求模型",
        ["agent.pause"]="{reason:string}: 保存计划并暂停接管"
    };
    internal static string Text(JsonElement a,string k,string fallback="")=>a.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
    internal static int Number(JsonElement a,string k,int fallback=0)=>a.TryGetProperty(k,out var v)&&v.TryGetInt32(out int n)?n:fallback;
    public object Execute(string tool,JsonElement args) => mod.WithExecutionContract(ExecuteCore(tool,args));
    private object ExecuteCore(string tool,JsonElement args) {
        if(!Context.IsWorldReady || Context.IsMultiplayer)throw new InvalidOperationException("single_player_world_required");
        if(!Catalog.ContainsKey(tool))throw new InvalidOperationException("unknown_tool");
        if((IsPlayerMutation(tool)&&mod.WorkActorBusy("player")) || tool=="companion.assign"&&mod.WorkActorBusy(Text(args,"actor_id")))throw new InvalidOperationException("actor_owned_by_work_job_cancel_or_wait");
        if(tool.StartsWith("menu.") && tool!="menu.read" && player.Busy && !player.NeedsMenuChoice)throw new InvalidOperationException("player_busy");
        if(tool is "player.use_tool" or "player.place" or "player.interact" or "player.work") {
            IEnumerable<JsonElement> targets=tool=="player.work" && args.TryGetProperty("tiles",out var tiles) && tiles.ValueKind==JsonValueKind.Array?tiles.EnumerateArray().ToArray():new[]{args};
            if(targets.Any(t=>mod.AgentTileBusy(Game1.currentLocation.NameOrUniqueName,Number(t,"x",-1),Number(t,"y",-1))))throw new InvalidOperationException("target_claimed_by_companion");
        }
        if(tool=="player.sleep")mod.CheckAgentSleep(args);
        return tool switch {
            "farm.business"=>mod.ConfigureBusiness(args),"farm.business_status"=>mod.ReadBusiness(args),
            "shop.sources"=>PlayerExecutor.ShopSources(Text(args,"item"),args.TryGetProperty("recipe",out var recipe)&&recipe.GetBoolean()).Select(s=>new{s.Shop,s.Location}),
            "perfection.read"=>PerfectionProgress.Read(),"progress.pursue"=>mod.ConfigureProgressCampaign(args),"progress.dependencies"=>mod.ReadProgressDependencies(args),"capabilities.read"=>CapabilityCatalog.Read(),"progress.catalog"=>mod.AgentProgressCatalog(args),
            "memory.search"=>mod.ReadAgentMemory(args),
            "memory.evidence"=>mod.ReadMemoryEvidence(args),
            "map.scan"=>mod.ScanMap(args),
            "farm.autonomy"=>mod.ConfigureFarmInvestment(args),"farm.economy"=>mod.PlanFarmEconomy(args),"farm.economy_status"=>mod.ReadFarmEconomy(args),"farm.execute"=>mod.ExecuteFarmEconomy(args),"farm.plan"=>mod.PlanFarm(args),
            "storage.configure"=>mod.ConfigureStorage(args),"storage.policy"=>mod.ConfigureStoragePolicy(args),
            "beach.read"=>PlayerExecutor.ReadBeach(),"family.read"=>mod.ReadFamily(),"strategy.family"=>mod.SetFamilyPolicy(args),
            "strategy.profession"=>mod.SetProfessionPolicy(args),
            "agent.status"=>mod.ReadAutonomyDiagnostics(),"usage.read"=>ModelRequestBudget.Status(),
            "orchard.read"=>PlayerExecutor.ReadOrchard(),"island.walnuts"=>PlayerExecutor.ReadWalnuts(),"volcano.read"=>PlayerExecutor.ReadVolcano(),"island.upgrades"=>PlayerExecutor.ReadIslandUpgrades(),"forge.read"=>PlayerExecutor.ReadForge(),"arcade.read"=>PlayerExecutor.ReadArcade(),
            "mastery.read"=>PlayerExecutor.ReadMastery(),"transport.read"=>PlayerExecutor.ReadTransportation(),"joja.read"=>PlayerExecutor.ReadJoja(),"order_donations.read"=>PlayerExecutor.ReadOrderDonations(),"equipment.read"=>PlayerExecutor.ReadEquipment(),"quest_board.read"=>PlayerExecutor.ReadQuestBoard(),"animals.read"=>PlayerExecutor.ReadAnimals(),"animal_shop.read"=>PlayerExecutor.ReadAnimalShop(),"construction.read"=>PlayerExecutor.ReadConstruction(args),"shop.read"=>mod.ObserveShop(args),
            "plan.read"=>mod.AgentPlanRead(),"plan.submit"=>mod.AgentPlanSubmit(args),"plan.cancel"=>mod.AgentPlanCancel(args),"plan.archive"=>mod.AgentPlanArchive(),
            "day.routine"=>mod.ConfigureDailyRoutine(args),"day.read"=>mod.AgentDailyRead(),"day.plan"=>mod.AgentDailyPlan(args),
            "crab_pots.read"=>PlayerExecutor.ReadCrabPots(),"fishing.options"=>mod.ReadFishingOptions(args),"world.read"=>mod.AgentWorld(),"map.read"=>ReadMap(args),"inventory.read"=>Inventory(),
            "knowledge.search"=>mod.Knowledge.Search(Text(args,"query"),limit:8),
            "knowledge.get" or "goal.requirements"=>mod.Knowledge.Query(Text(args,"query"),Text(args,"id") is {Length:>0} id?id:null),
            "goal.run"=>mod.AgentGoalRun(args),"goal.create"=>mod.AgentGoalCreate(args),"goal.prepare"=>mod.AgentGoalPrepare(args),
            "progress.read"=>Progress(),"progress.roadmap"=>mod.AgentProgression(),"progress.missing"=>Game1.achievements.Where(a=>!Game1.player.achievements.Contains(a.Key)).Select(a=>new{id=a.Key,name=a.Value.Split('^')[0],native_definition=a.Value,source="Data/Achievements"}).ToArray(),
            "orders.read"=>Game1.player.team.specialOrders.Select(o=>new{id=o.questKey.Value,state=o.questState.Value.ToString(),deadline=o.dueDate.Value,objectives=o.objectives.Select((x,i)=>OrderRules.Describe(x,i)).ToArray()}).ToArray(),
            "work.run"=>mod.StartSemanticWork(args),
            "player.recruit_companion" or "player.tap_tree" or "player.acquire_animal" or "player.procure" or "player.find_lost_item" or "player.beach" or "player.crab_pots" or "player.treasure" or "player.walnuts" or "player.volcano_step" or "player.forge" or "player.island_upgrade" or "player.arcade" or "player.read_mail" or "player.watch_tv" or "player.transport" or "player.repair_boat" or "player.read_book" or "player.mastery" or "player.orchard" or "player.joja" or "player.place_facility" or "player.ship_items" or "player.order_donate" or "player.equip" or "player.attach" or "player.accept_quest" or "player.animal" or "player.geodes" or "player.buy_animal" or "player.upgrade_house" or "player.mine_access" or "player.bundle" or "player.build" or "player.donate_museum" or "player.collect_reward" or "player.service" or "player.machine" or "player.claim_reward" or "player.care" or "player.social" or "player.combat" or "player.mine_descend" or "player.fish" or "player.buy" or "player.craft" or "player.cook" or "player.eat" or "player.work" or "player.move" or "player.travel" or "player.use_tool" or "player.interact" or "player.place" or "player.sleep" or "player.ship"=>player.Start(tool,args),
            "menu.text"=>menus.EnterText(args),"menu.read"=>menus.Read(),"menu.open"=>menus.Open(Text(args,"page")),"menu.choose"=>menus.Choose(args),
            "menu.scroll"=>menus.Scroll(Text(args,"direction")),"menu.close"=>menus.Close(),
            "companion.assign"=>mod.AgentCompanion(args),
            "action.status"=>mod.AgentReceipt(Text(args,"id"),false),"action.cancel"=>mod.AgentReceipt(Text(args,"id"),true),
            "agent.wait"=>Wait(Number(args,"seconds",1)),"agent.pause"=>Pause(Text(args,"reason","模型请求暂停")),
            _=>throw new InvalidOperationException("unknown_tool")
        };
    }
    private object Wait(int seconds){int actual=mod.AgentWait(seconds);return new{status="waiting",seconds=actual,note="按原生时间限制等待长度，深夜前重新决策"};}
    private object Pause(string reason){mod.PauseAutoplay(reason);return new{status="paused",reason};}
    internal static object ItemInfo(Item? item)=>item==null?new{empty=true}:(object)new{id=item.QualifiedItemId,name=item.DisplayName,count=item.Stack,quality=item.Quality,kind=item.GetType().Name,upgrade_level=item is Tool tool?(int?)tool.UpgradeLevel:null,water_left=item is StardewValley.Tools.WateringCan can?(int?)can.WaterLeft:null};
    internal static object Inventory()=>new{selected=Game1.player.CurrentToolIndex,items=Game1.player.Items.Select((v,i)=>new{slot=i,item=ItemInfo(v)}).ToArray()};
    private static object Progress()=>new{scope="native Farmer and team; platform achievements not verified",achievements=Game1.player.achievements.ToArray(),
        crafting=Game1.player.craftingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooking=Game1.player.cookingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        skills=new{farming=Game1.player.FarmingLevel,mining=Game1.player.MiningLevel,fishing=Game1.player.FishingLevel,foraging=Game1.player.ForagingLevel,combat=Game1.player.CombatLevel},
        shipped=Game1.player.basicShipped.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooked=Game1.player.recipesCooked.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        fish_caught=Game1.player.fishCaught.Pairs.ToDictionary(p=>p.Key,p=>p.Value),minerals=Game1.player.mineralsFound.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        deepest_mine=Game1.player.deepestMineLevel,mail=Game1.player.mailReceived.ToArray(),
        quests=Game1.player.questLog.Select(q=>new{id=NativeQuestIdentity.Id(q),title=q.questTitle,description=q.questDescription,completed=q.completed.Value}).ToArray(),
        note="未覆盖全部成就条件；缺少条目不能解释为已完成"};
    private object ReadMap(JsonElement args) {
        string actorId=Text(args,"actor_id","player");var origin=mod.AgentMapOrigin(actorId);
        var l=origin.Location;int r=Math.Clamp(Number(args,"radius",8),1,20),cx=Number(args,"x",origin.Tile.X),cy=Number(args,"y",origin.Tile.Y);
        int width=l.Map.Layers[0].LayerWidth,height=l.Map.Layers[0].LayerHeight;
        cx=Math.Clamp(cx,0,width-1);cy=Math.Clamp(cy,0,height-1);var cells=new List<object>();var rows=new List<string>();
        int x0=Math.Max(0,cx-r),y0=Math.Max(0,cy-r);
        for(int y=y0;y<=Math.Min(height-1,cy+r);y++){var row=new System.Text.StringBuilder();for(int x=x0;x<=Math.Min(width-1,cx+r);x++) {
            var v=new Vector2(x,y);l.objects.TryGetValue(v,out var o);l.terrainFeatures.TryGetValue(v,out var feature);var dirt=feature as HoeDirt;
            string? action=l.doesTileHaveProperty(x,y,"Action","Buildings"),touch=l.doesTileHaveProperty(x,y,"TouchAction","Back");
            bool passable=PlayerExecutor.Passable(l,new(x,y)),water=l.isWaterTile(x,y);
            row.Append(water?'~':!passable?'#':l.doesTileHaveProperty(x,y,"Diggable","Back")!=null?'.':'_');
            if(o!=null||feature!=null||action!=null||touch!=null)cells.Add(new{x,y,
                item=o==null?null:ItemInfo(o),terrain=feature?.GetType().Name,watered=dirt?.state.Value==1,
                crop=dirt?.crop==null?null:new{harvest=dirt.crop.indexOfHarvest.Value,phase=dirt.crop.currentPhase.Value,dead=dirt.crop.dead.Value,ready=dirt.readyForHarvest()},action,touch});
        }rows.Add(row.ToString());}
        return new{actor_id=actorId,location=l.NameOrUniqueName,width,height,center=new[]{cx,cy},radius=r,x0,y0,
            exits=PlayerExecutor.Exits(l).Select(e=>new{e.X,e.Y,e.TargetName,e.TargetX,e.TargetY}),
            buildings=l.buildings.Select(b=>new{type=b.buildingType.Value,x=b.tileX.Value,y=b.tileY.Value,width=b.tilesWide.Value,height=b.tilesHigh.Value,door=b.humanDoor.Value.X<0?null:new{x=b.tileX.Value+b.humanDoor.Value.X,y=b.tileY.Value+b.humanDoor.Value.Y},interior=b.GetIndoors()?.NameOrUniqueName}),
            characters=l.characters.Select(n=>new{name=n.Name,x=n.TilePoint.X,y=n.TilePoint.Y,monster=n.IsMonster}),
            farm_animals=Game1.getFarm().getAllFarmAnimals().Where(a=>a.currentLocation==l).Select(a=>new{id=a.myID.Value,name=a.displayName,x=a.TilePoint.X,y=a.TilePoint.Y,pet=a.wasPet.Value,fullness=a.fullness.Value}),
            furniture=l.furniture.Select(f=>new{name=f.Name,x=f.TileLocation.X,y=f.TileLocation.Y}),
            home=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName,grid=rows,legend="# blocked, ~ water, . clear diggable, _ clear non-diggable; origin x0,y0; moving characters may block",cells=cells.Take(36),cells_truncated=cells.Count>36,note="建筑和出口先列出，cells截断时缩小radius或移动中心查询，缺省格子不等于没有对象"};
    }
}
