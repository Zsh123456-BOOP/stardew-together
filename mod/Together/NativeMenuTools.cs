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
    private sealed record Choice(string Id,string Label,Rectangle Bounds);
    private readonly List<Choice> choices=new();
    private IClickableMenu? observed;
    private string token="";
    public void Reset(){observed=null;token="";choices.Clear();}
    private static IClickableMenu? Menu()=>Game1.activeClickableMenu;
    private static IClickableMenu Content(IClickableMenu m)=>m is GameMenu g?g.GetCurrentPage():m;
    public object Read() {
        choices.Clear();observed=Menu();if(observed==null){token="";return new{type="none"};}
        var m=Content(observed);var labels=new Dictionary<Rectangle,string>();
        if(m is CraftingPage craft && craft.currentCraftingPage<craft.pagesOfCraftingRecipes.Count)
            foreach(var pair in craft.pagesOfCraftingRecipes[craft.currentCraftingPage])labels[pair.Key.bounds]="craft:"+pair.Value.name+" "+pair.Value.DisplayName+" ingredients="+pair.Value.doesFarmerHaveIngredientsInInventory();
        if(m is ShopMenu shop)
            for(int i=0;i<shop.forSaleButtons.Count;i++){int n=shop.currentItemIndex+i;if(n<shop.forSale.Count)labels[shop.forSaleButtons[i].bounds]="buy:"+shop.forSale[n].DisplayName;}
        if(m is DialogueBox dialogue && dialogue.responseCC!=null)
            for(int i=0;i<Math.Min(dialogue.responses.Length,dialogue.responseCC.Count);i++)labels[dialogue.responseCC[i].bounds]="response:"+dialogue.responses[i].responseKey+" "+dialogue.responses[i].responseText;
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
                        labels[slot.bounds]="inventory:"+field.Name+":"+i+" "+JsonSerializer.Serialize(AgentToolRegistry.ItemInfo(i<inv.actualInventory.Count?inv.actualInventory[i]:null));
                    }
                }
            }
        }
        foreach(var c in components.Where(c=>c!=null && c.visible && c.bounds.Width>0 && c.bounds.Height>0).DistinctBy(c=>c.bounds))
            choices.Add(new("c"+choices.Count,labels.GetValueOrDefault(c.bounds,c.name??"component"),c.bounds));
        string text=m is DialogueBox d?string.Join("\n",d.dialogues):"";
        string held=m is CraftingPage cp?JsonSerializer.Serialize(AgentToolRegistry.ItemInfo(cp.heldItem)):m is MenuWithInventory mi?JsonSerializer.Serialize(AgentToolRegistry.ItemInfo(mi.heldItem)):"";
        token=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.GetType().Name+JsonSerializer.Serialize(choices)+text+held)))[..16];
        return new{type=m.GetType().Name,token,text,held,choices=choices.Select(c=>new{c.Id,c.Label}),ready_to_close=m.readyToClose()};
    }
    public object Choose(JsonElement args) {
        var previous=observed;string expected=AgentToolRegistry.Text(args,"token");Read();
        if(previous!=observed || expected!=token || token.Length==0)throw new InvalidOperationException("stale_menu_read_again");
        var choice=choices.FirstOrDefault(c=>c.Id==AgentToolRegistry.Text(args,"id"))??throw new InvalidOperationException("unknown_menu_choice");
        bool right=args.TryGetProperty("right",out var flag)&&flag.ValueKind==JsonValueKind.True;
        if(right)observed!.receiveRightClick(choice.Bounds.Center.X,choice.Bounds.Center.Y);else observed!.receiveLeftClick(choice.Bounds.Center.X,choice.Bounds.Center.Y);
        return new{status="input_sent",menu=Read(),inventory=AgentToolRegistry.Inventory()};
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
        if(!m.readyToClose() || c is CraftingPage {heldItem:not null} || c is MenuWithInventory {heldItem:not null})throw new InvalidOperationException("menu_not_safe_to_close");
        m.exitThisMenu();Reset();return new{status="closed"};
    }
}
