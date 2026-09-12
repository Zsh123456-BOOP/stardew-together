using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed class NativeMenuTools {
    public Action<int,int,int>? ProfessionSelected {get;set;}
    public Func<CraftingRecipe,bool,string,Item>? ValidateRecipe {get;set;}
    public Func<string,JsonElement,object>? StartBusinessAction {get;set;}
    public Action<string,object>? Evidence {get;set;}
    public Action<string>? Stop {get;set;}
    private readonly MenuEffectWatch effectWatch=new();
    private sealed record Choice(string Id,string Label,Rectangle Bounds,object? Facts=null);
    internal static Item? HeldItem() => Menu() is {} m ? Content(m) switch {CraftingPage c=>c.heldItem,MenuWithInventory i=>i.heldItem,_=>null} : null;
    private static object NativeState()=>new{inventory=AgentToolRegistry.Inventory(),money=Game1.player.Money,held=HeldItem() is {} h?AgentToolRegistry.ItemInfo(h):null,crafting=Game1.player.craftingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooking=Game1.player.recipesCooked.Pairs.ToDictionary(p=>p.Key,p=>p.Value)};
    private readonly List<Choice> choices=new();
    private IClickableMenu? observed;
    private string token="";
    public void Reset(){observed=null;token="";choices.Clear();}
    private static IClickableMenu? Menu()=>Game1.activeClickableMenu;
    private static IClickableMenu Content(IClickableMenu m)=>m is GameMenu g?g.GetCurrentPage():m;
    public object Read() {
        choices.Clear();observed=Menu();if(observed==null){token="";return new{type="none"};}
        var m=Content(observed);var labels=new Dictionary<Rectangle,string>();var facts=new Dictionary<Rectangle,object>();
        if(m is NamingMenu naming){labels[naming.doneNamingButton.bounds]="submit_name";labels[naming.randomButton.bounds]="random_name";labels[naming.textBoxCC.bounds]="name_input";}
        if(m is CraftingPage craft && craft.currentCraftingPage<craft.pagesOfCraftingRecipes.Count)
            foreach(var pair in craft.pagesOfCraftingRecipes[craft.currentCraftingPage]) {
                labels[pair.Key.bounds]="craft:"+pair.Value.name+" "+pair.Value.DisplayName;
                facts[pair.Key.bounds]=new{kind="craft",recipe=pair.Value.name,ingredients_available=pair.Value.doesFarmerHaveIngredientsInInventory(),output=AgentToolRegistry.ItemInfo(pair.Value.createItem()),note="还须通过同一容量与预留校验"};
            }
        if(m is ShopMenu shop)
            for(int i=0;i<shop.forSaleButtons.Count;i++){int n=shop.currentItemIndex+i;if(n<shop.forSale.Count){var item=shop.forSale[n];var stock=shop.itemPriceAndStock.GetValueOrDefault(item);labels[shop.forSaleButtons[i].bounds]="buy:"+item.DisplayName+" price="+stock?.Price+" stock="+stock?.Stock;}}
        if(m is DialogueBox dialogue && dialogue.responseCC!=null)
            for(int i=0;i<Math.Min(dialogue.responses.Length,dialogue.responseCC.Count);i++)labels[dialogue.responseCC[i].bounds]="response:"+dialogue.responses[i].responseKey+" "+dialogue.responses[i].responseText;
        if(m is LevelUpMenu level) {
            if(level.okButton!=null)labels[level.okButton.bounds]="确认技能升级";
            string Description(string field)=>string.Join("；",typeof(LevelUpMenu).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(level) as List<string>??new());
            if(level.leftProfession!=null)labels[level.leftProfession.bounds]=Description("leftProfessionDescription");
            if(level.rightProfession!=null)labels[level.rightProfession.bounds]=Description("rightProfessionDescription");
        }
        var components=new List<ClickableComponent>();
        // Read only known UI component types. Never traverse game state or arbitrary property getters.
        foreach(var owner in new[]{observed,m}.Distinct()) {
            owner.populateClickableComponentList();if(owner.allClickableComponents!=null)components.AddRange(owner.allClickableComponents);
            foreach(var field in owner.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public)) {
                object? value=field.GetValue(owner);
                if(value is ClickableComponent c)components.Add(c);
                if(value is InventoryMenu inv) {
                    for(int i=0;i<inv.inventory.Count;i++) {
                        var slot=inv.inventory[i];components.Add(slot);
                        bool locked=i>=inv.actualInventory.Count;
                        labels[slot.bounds]="inventory:"+field.Name+":"+i+(locked?" locked":"");
                        facts[slot.bounds]=new{kind="inventory_slot",slot=i,locked,available=!locked,item=locked?null:AgentToolRegistry.ItemInfo(inv.actualInventory[i])};
                    }
                }
            }
        }
        foreach(var c in components.Where(c=>c!=null && c.visible && c.bounds.Width>0 && c.bounds.Height>0).DistinctBy(c=>c.bounds))
            choices.Add(new("c"+choices.Count,labels.GetValueOrDefault(c.bounds,c.name??"component"),c.bounds,facts.GetValueOrDefault(c.bounds)));
        if(m is LevelUpMenu levelState) {
            if(!levelState.isActive || !levelState.CanReceiveInput())choices.Clear();
            else if(levelState.isProfessionChooser)choices.RemoveAll(c=>c.Bounds!=levelState.leftProfession?.bounds && c.Bounds!=levelState.rightProfession?.bounds);
            else choices.RemoveAll(c=>c.Bounds!=levelState.okButton?.bounds);
        }
        string text=m is NamingMenu nm?nm.title+"\n"+nm.textBox.Text:m is DialogueBox d?d.getCurrentString():m is LevelUpMenu lu?typeof(LevelUpMenu).GetField("title",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(lu)?.ToString()??"技能升级":"";
        if(m is DialogueBox {isQuestion:false})choices.Add(new("continue","继续对话",new Rectangle(m.xPositionOnScreen+16,m.yPositionOnScreen+16,32,32)));
        string held=m is CraftingPage cp?JsonSerializer.Serialize(AgentToolRegistry.ItemInfo(cp.heldItem)):m is MenuWithInventory mi?JsonSerializer.Serialize(AgentToolRegistry.ItemInfo(mi.heldItem)):"";
        token=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.GetType().Name+JsonSerializer.Serialize(choices)+text+held)))[..16];
        return new{type=m.GetType().Name,token,text,held,pending_output=HeldItem() is {} output?new{state="native_held_not_received",item=AgentToolRegistry.ItemInfo(output),can_receive=CapacityAdapter.CanReceive(Game1.player,output),resume="native_inventory_click",blocked="preserve_held_item; no_new_crafting_or_resource_request"}:null,choices=choices.Select(c=>new{c.Id,c.Label,Facts=(object?)c.Facts}),ready_to_close=m.readyToClose()};
    }
    private bool executionStopped;
    public object Choose(JsonElement args) {
        if(executionStopped)throw new InvalidOperationException("menu_execution_stopped_requires_review");
        var previous=observed;string expected=AgentToolRegistry.Text(args,"token");Read();
        if(previous!=observed || expected!=token || token.Length==0)throw new InvalidOperationException("stale_menu_read_again");
        string id=AgentToolRegistry.Text(args,"id");
        var choice=choices.FirstOrDefault(c=>c.Id==id)??throw new InvalidOperationException("unknown_menu_choice");
        var m=Content(observed!);var before=new{menu=Read(),state=NativeState()};string fingerprint=AgentJson.Encode(before);
        object? dispatched=null;
        try {
            if(choice.Facts!=null) {
                var f=JsonSerializer.SerializeToElement(choice.Facts);
                if(f.TryGetProperty("locked",out var locked)&&locked.GetBoolean())throw new InvalidOperationException("menu_slot_locked");
            }
            if(m is CarpenterMenu or GeodeMenu or PurchaseAnimalsMenu or JojaCDMenu or ForgeMenu)throw new InvalidOperationException("menu_business_requires_specific_executor");
            if(m is DialogueBox {isQuestion:true}&&!Game1.eventUp)throw new InvalidOperationException("menu_question_requires_specific_executor");
            // Guard business menus before generic click dispatch.
            // Business cells route to the same executors, including their budgets.
            if(m is CraftingPage cp) {
                var recipe=cp.pagesOfCraftingRecipes[cp.currentCraftingPage].FirstOrDefault(p=>p.Key.bounds==choice.Bounds).Value;
                if(recipe!=null) {
                    if(cp.heldItem!=null)throw new InvalidOperationException("production_output_pending_receive_before_new_recipe");
                    bool cooking=recipe.isCookingRecipe;string goal=AgentToolRegistry.Text(args,"goal_id");
                    if(ValidateRecipe==null||StartBusinessAction==null)throw new InvalidOperationException("menu_business_guard_missing");
                    ValidateRecipe(recipe,cooking,goal);
                    observed!.exitThisMenu();
                    dispatched=StartBusinessAction(cooking?"player.cook":"player.craft",JsonSerializer.SerializeToElement(new{recipe=recipe.name,count=1,goal_id=goal}));
                }
            }
            if(m is ShopMenu shop) {
                if(!choice.Label.StartsWith("buy:",StringComparison.Ordinal))throw new InvalidOperationException("menu_shop_use_authorized_buy_or_close");
                if(!args.TryGetProperty("purchase",out var purchase)||purchase.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("explicit_purchase_limits_required_use_player_buy");
                int index=shop.forSaleButtons.FindIndex(c=>c.bounds==choice.Bounds)+shop.currentItemIndex;
                if(index<0||index>=shop.forSale.Count||AgentToolRegistry.Text(purchase,"item")!=shop.forSale[index].QualifiedItemId||AgentToolRegistry.Text(purchase,"shop")!=shop.ShopId)throw new InvalidOperationException("menu_purchase_target_mismatch");
                dispatched=StartBusinessAction?.Invoke("player.buy",purchase)??throw new InvalidOperationException("menu_business_guard_missing");
            }
            if(dispatched==null) {
                if(HeldItem()!=null&&!choice.Label.StartsWith("inventory:",StringComparison.Ordinal))throw new InvalidOperationException("held_output_preserved_use_available_inventory_slot");
                if(choice.Label.Contains("trash",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("inventory_disposal_not_authorized");
                if(observed is LevelUpMenu levelChoice) {
                    if(!levelChoice.isActive||!levelChoice.CanReceiveInput())throw new InvalidOperationException("menu_wait_for_ready");
                    if(levelChoice.isProfessionChooser){var state=NativeMenuInput.ProfessionState(levelChoice);int index=choice.Bounds==levelChoice.leftProfession.bounds?0:1;NativeMenuInput.ChooseProfession(levelChoice,choice.Bounds);if(state.Choices.Count>index)ProfessionSelected?.Invoke(state.Skill,state.Level,state.Choices[index]);}
                    else levelChoice.okButtonClicked();
                } else {
                    if(observed is DialogueBox dialogueBox)dialogueBox.finishTyping();
                    bool right=args.TryGetProperty("right",out var flag)&&flag.ValueKind==JsonValueKind.True;
                    NativeMenuInput.ClickMenu(observed!,choice.Bounds,right);
                }
            }
        } catch(InvalidOperationException e) {Evidence?.Invoke("menu_choice_rejected",new{token=expected,id,reason=e.Message,before,after=new{menu=Read(),state=NativeState()}});throw;}
        var after=new{menu=Read(),state=NativeState()};bool changed=fingerprint!=AgentJson.Encode(after);
        int repeated=effectWatch.Observe(expected,id,changed||dispatched!=null);
        Evidence?.Invoke("menu_choice_effect",new{token=expected,id,changed,dispatched,repeated,before,after});
        if(repeated>=3){executionStopped=true;Stop?.Invoke("menu_no_effect_three:"+expected+":"+id);throw new InvalidOperationException("menu_no_effect_three");}
        if(!changed&&dispatched==null)throw new InvalidOperationException("menu_no_effect");
        return dispatched??new{status="native_menu_changed",menu=after.menu,inventory=AgentToolRegistry.Inventory(),note="仅核验菜单/库存变化，不代表业务目标完成"};
    }
    internal static string ValidateName(string name,int minimum=1) {
        name=name.Trim();if(name.Length<minimum||name.Length>24||name.Any(char.IsControl)||name.Contains('[')||name.Contains(']'))throw new InvalidOperationException("name_requires_1_to_24_plain_characters");
        string filtered=Utility.FilterDirtyWords(name).Trim();if(filtered.Length<minimum)throw new InvalidOperationException("native_name_filter_rejected");return filtered;
    }
    public object EnterText(JsonElement args) {
        var previous=observed;string expected=AgentToolRegistry.Text(args,"token");Read();
        if(previous!=observed||expected!=token||token.Length==0)throw new InvalidOperationException("stale_menu_read_again");
        if(observed is not NamingMenu naming)throw new InvalidOperationException("text_input_requires_native_naming_menu");
        string name=ValidateName(AgentToolRegistry.Text(args,"text"),naming.minLength);naming.textBox.Text=name;
        if(args.TryGetProperty("submit",out var flag)&&flag.ValueKind==JsonValueKind.True)NativeMenuInput.ClickMenu(naming,naming.doneNamingButton.bounds);
        return new{status="input_sent",menu=Read(),note="仅输入原生命名菜单；名称和出生结果须从实际角色/动物状态核验"};
    }
    public object Open(string page) {
        if(Game1.activeClickableMenu!=null || !Game1.player.CanMove || Game1.eventUp)throw new InvalidOperationException("cannot_open_menu_now");
        Game1.activeClickableMenu=page switch {
            "inventory"=>new GameMenu(0),"crafting"=>new GameMenu(4),"journal"=>new QuestLog(),_=>throw new InvalidOperationException("unknown_page")};
        return Read();
    }
    public object Scroll(string direction) {
        if(Menu()==null)throw new InvalidOperationException("menu_missing");
        if(direction is not ("up" or "down"))throw new InvalidOperationException("invalid_direction");
        Menu()!.receiveScrollWheelAction(direction=="up"?1:-1);return Read();
    }
    public object Close() {
        var m=Menu();if(m==null)return new{status="closed"};var c=Content(m);
        if(!m.readyToClose() || c is LevelUpMenu {isProfessionChooser:true} || c is CraftingPage {heldItem:not null} || c is MenuWithInventory {heldItem:not null})throw new InvalidOperationException("menu_not_safe_to_close");
        m.exitThisMenu();Reset();return new{status="closed"};
    }
}
