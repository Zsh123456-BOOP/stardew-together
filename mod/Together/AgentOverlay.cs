using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private Rectangle overlayBounds;
    private string[] overlayLines=Array.Empty<string>();
    private string overlayTitle="同行 · 等待任务";
    private int overlayOffset,overlayVisible;
    private bool overlayCollapsed;
    private DateTime overlayNext;
    private double overlayBuildMs;
    private int overlayWheelEvents,overlayWheelHandled;
    private Point overlayLastCursor;
    private void SetupAgentOverlay() {
        Helper.Events.GameLoop.UpdateTicked+=(_,_)=>RefreshAgentOverlay();
        Helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{overlayLines=Array.Empty<string>();overlayBounds=Rectangle.Empty;overlayOffset=0;};
        Helper.Events.Input.MouseWheelScrolled+=(_,e)=>{
            overlayWheelEvents++;overlayLastCursor=new Point(Game1.getMouseX(true),Game1.getMouseY(true));
            if(!OverlayInteractive()||overlayVisible==0||!overlayBounds.Contains(overlayLastCursor))return;
            overlayWheelHandled++;
            Helper.Input.SuppressScrollWheel();
            overlayOffset=Math.Clamp(overlayOffset-Math.Sign(e.Delta)*3,0,Math.Max(0,overlayLines.Length-overlayVisible));
        };
        Helper.Events.Input.ButtonPressed+=(_,e)=>{
            if(e.Button!=SButton.MouseLeft||!OverlayInteractive())return;
            var at=new Point(Game1.getMouseX(true),Game1.getMouseY(true));if(!overlayBounds.Contains(at))return;
            Helper.Input.Suppress(e.Button);
            if(at.Y<overlayBounds.Y+34){overlayCollapsed=!overlayCollapsed;overlayNext=DateTime.MinValue;}
            else if(at.Y>overlayBounds.Bottom-26){overlayOffset=0;overlayNext=DateTime.MinValue;}
        };
        Helper.Events.Display.RenderedHud+=(_,e)=>DrawAgentOverlay(e.SpriteBatch);
    }
    private bool OverlayInteractive()=>Context.IsWorldReady&&Settings.AgentOverlay&&Game1.activeClickableMenu==null&&overlayBounds.Width>0;
    private void RefreshAgentOverlay() {
        if(!Context.IsWorldReady||!Settings.AgentOverlay){overlayBounds=Rectangle.Empty;return;}
        if(DateTime.UtcNow<overlayNext)return;overlayNext=DateTime.UtcNow.AddMilliseconds(250);
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        int width=Math.Clamp(Game1.uiViewport.Width/3,280,340),x=Math.Max(8,Game1.uiViewport.Width-width-20),y=Math.Min(290,Math.Max(50,Game1.uiViewport.Height/3));
        bool compact=overlayCollapsed||Game1.activeClickableMenu!=null||Game1.uiViewport.Height<500;
        string mode=!AutoplayRunning?"已暂停":preparation!=null?"准备物资":agentPending!=null?"安排接下来的事":WorkActorBusy("player")||playerExecutor.Busy?"正在忙碌":Game1.eventUp?"观看剧情":"等待安排";
        overlayTitle="同行 · "+mode;
        var lines=new List<string>();
        void Add(string text) {
            if(string.IsNullOrWhiteSpace(text))return;
            // Character wrapping also handles Chinese and long words; no raw
            // JSON/receipt dump can reach this display surface.
            string row="";
            foreach(char c in text) {
                if(c=='\n'||Font.MeasureString(row+c).X*.78f>width-36){lines.Add(row);row="";if(c=='\n')continue;}
                row+=c;
            }
            if(row.Length>0)lines.Add(row);
        }
        string goal=OverlaySentence(Data.Autoplay.Goal,76);
        if(goal.Length>0)Add("想做  "+goal);
        var work=semanticJobs.Values.FirstOrDefault(j=>j.actor=="player"&&j.status=="running");
        var action=playerExecutor.Current;
        string now=preparation!=null?(preparation.Phase=="return"?"物资准备好了，回去继续干活":"去仓库存取这次需要的东西"):
            work!=null?OverlayWork(work.goal):action is {status:"running"}?OverlayTool(action.skill):mode;
        if(work!=null&&work.completed>0)now+="，已处理 "+work.completed+" 处";
        Add("现在  "+now);
        var next=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.state!="running").Take(5).ToArray();
        if(next.Length>0) {
            Add("接下来");
            foreach(var task in next)Add("  · "+(task.spec.tool=="work.run"?OverlayWork(AgentToolRegistry.Text(task.spec.args,"goal")):OverlayTool(task.spec.tool)));
        }
        // Only the model's explicit short explanation is eligible. Technical
        // payloads, error codes, receipts and IDs stay in the diagnostic logs.
        string reason=OverlaySentence(Data.Autoplay.Plan,110);
        if(reason.Length>0&&reason!=goal)Add("打算  "+reason);
        var blocked=Data.Autoplay.Schedule.Tasks.LastOrDefault(t=>t.state=="failed"||t.wait_reason!=null);
        if(!AutoplayRunning)Add("暂停中，等待你继续");
        else if(preparation==null&&work==null&&!playerExecutor.Busy&&blocked!=null)Add("暂时受阻  "+OverlayBlock(blocked.error??blocked.wait_reason));
        if(Data.Partner.Enabled)Add(Data.Autoplay.Survival.NativePlayerOnly?"小禾陪着你，暂不参与劳动":"伙伴状态以实际行动为准");
        overlayLines=lines.ToArray();
        int available=Math.Max(112,Math.Min(360,Game1.uiViewport.Height-y-100));
        int height=compact?38:Math.Min(available,Math.Max(136,64+overlayLines.Length*25));
        overlayBounds=new(x,y,width,height);overlayVisible=compact?0:Math.Max(1,(height-64)/25);
        overlayOffset=Math.Clamp(overlayOffset,0,Math.Max(0,overlayLines.Length-overlayVisible));
        overlayBuildMs=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    private static string OverlaySentence(string? text,int limit) {
        if(string.IsNullOrWhiteSpace(text)||text.IndexOfAny(new[]{'{','}','[',']','_','`'})>=0||!text.Any(c=>c>='\u4e00'&&c<='\u9fff'))return "";
        text=text.Replace("\n"," ").Replace("\r"," ").Trim();
        return text.Length>limit?text[..limit]+"…":text;
    }
    private static string OverlayWork(string goal)=>goal switch {
        "plant"=>"整理田块、播种和浇水","cleanup"=>"清理规划区域","water"=>"给作物浇水","refill"=>"给水壶补水","harvest"=>"收获成熟作物","store"=>"把物资存进仓库","withdraw"=>"从仓库取物资","storage_expand"=>"准备新的储物箱","wood"=>"收集木材","stone"=>"收集石头","hardwood"=>"收集硬木","fiber"=>"清理杂草、收集纤维","forage"=>"寻找可采集的东西","fish"=>"钓鱼","mine_trip"=>"下矿收集资源","volcano_trip"=>"探索火山","pet"=>"照顾动物","feed"=>"给动物喂食","milk"=>"挤奶","shear"=>"剪羊毛","animal_collect"=>"收取动物产品","clear_dead"=>"清除枯萎作物",_=>"处理已安排的工作"
    };
    private static string OverlayTool(string tool)=>tool switch {
        "player.move" or "player.travel" or "player.service"=>"前往下一个地点","player.sleep"=>"回家睡觉","player.procure" or "player.buy"=>"购买需要的物资","player.craft"=>"制作需要的物品","player.cook"=>"准备料理","player.machine"=>"照料加工设备","player.eat"=>"吃点东西恢复体力","player.place" or "player.place_facility"=>"放置设施","player.build"=>"安排农场建设","player.ship" or "player.ship_items"=>"出售已安排的产品","player.read_mail"=>"查看信件","player.collect_home_gifts"=>"领取初始物资","player.work" or "player.use_tool"=>"处理眼前的劳动","player.social"=>"与村民交流",_=>"执行已安排的操作"
    };
    private static string OverlayBlock(string? code) {
        code??="";
        if(code.Contains("capacity")||code.Contains("inventory"))return "背包或仓库装不下，正在等待腾出空间";
        if(code.Contains("loadout_missing"))return "还缺这次劳动需要的工具或材料";
        if(code.Contains("path")||code.Contains("unreachable"))return "暂时走不到目标位置";
        if(code.Contains("stamina")||code.Contains("energy"))return "体力不足，需要补给或休息";
        if(code.Contains("closed")||code.Contains("opening"))return "还没到营业时间";
        if(code.Contains("dependency"))return "前面的准备工作尚未完成";
        if(code.Contains("event")||code.Contains("menu"))return "需要先处理当前对话或菜单";
        return "当前工作遇到阻碍，需要重新安排";
    }
    private void DrawAgentOverlay(SpriteBatch b) {
        if(!Context.IsWorldReady||!Settings.AgentOverlay||overlayBounds.Width==0)return;
        var box=overlayBounds;float opacity=Math.Clamp(Settings.AgentOverlayOpacity,.2f,.9f);
        b.Draw(Game1.staminaRect,box,new Color(27,32,30)*opacity);
        b.Draw(Game1.staminaRect,new Rectangle(box.X,box.Y,2,box.Height),new Color(158,183,151)*.85f);
        b.DrawString(Font,overlayTitle,new Vector2(box.X+13,box.Y+7),new Color(242,233,210),0,Vector2.Zero,.82f,SpriteEffects.None,1);
        b.DrawString(Font,overlayVisible==0?"＋":"－",new Vector2(box.Right-27,box.Y+7),Color.LightGray,0,Vector2.Zero,.75f,SpriteEffects.None,1);
        if(overlayVisible==0)return;
        for(int row=0;row<overlayVisible&&overlayOffset+row<overlayLines.Length;row++)b.DrawString(Font,overlayLines[overlayOffset+row],new Vector2(box.X+13,box.Y+39+row*25),new Color(238,238,227),0,Vector2.Zero,.78f,SpriteEffects.None,1);
        if(overlayLines.Length>overlayVisible){int track=box.Height-68,thumb=Math.Max(16,track*overlayVisible/overlayLines.Length);int top=box.Y+39+(track-thumb)*overlayOffset/Math.Max(1,overlayLines.Length-overlayVisible);b.Draw(Game1.staminaRect,new Rectangle(box.Right-6,top,2,thumb),new Color(158,183,151)*.85f);}
        b.DrawString(Font,overlayLines.Length>overlayVisible?"滚轮查看更多":"点击标题收起",new Vector2(box.X+13,box.Bottom-21),new Color(190,198,187),0,Vector2.Zero,.6f,SpriteEffects.None,1);
    }
}
