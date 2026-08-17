using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
namespace Together;
public sealed partial class ModEntry {
    public void OpenAutoplay()=>Game1.activeClickableMenu=new AutoplayMenu(this);
    public void SetAutoplaySpeed(double value){Settings.AutoplayClockRate=AutoplaySpeed.Clock(value);Helper.WriteConfig(Settings);}
}
public sealed class AutoplayMenu:IClickableMenu {
    private readonly ModEntry mod;
    private readonly TextBox goal;
    private readonly List<(Rectangle Bounds,string Label,Action Action)> buttons=new();
    public AutoplayMenu(ModEntry mod):base(Game1.uiViewport.Width/2-440,Game1.uiViewport.Height/2-245,880,490,true) {
        this.mod=mod;goal=new TextBox(Game1.content.Load<Texture2D>("LooseSprites/textBox"),null,mod.Font,Color.DarkSlateGray){X=xPositionOnScreen+28,Y=yPositionOnScreen+138,Width=824,Text=mod.Data.Autoplay.Goal.Length>0?mod.Data.Autoplay.Goal:"一起经营农场：先照料作物，再按材料缺口分工采集、探索和制作；合理利用体力与白天，安全回家。",textLimit=800};
        Add(28,204,180,"开始 / 继续接管",()=>{exitThisMenu();mod.StartAutoplay(goal.Text);});
        Add(228,204,120,"暂停",()=>mod.PauseAutoplay("玩家暂停"));
    }
    private void Add(int x,int y,int w,string text,Action a)=>buttons.Add((new(xPositionOnScreen+x,yPositionOnScreen+y,w,40),text,a));
    public override void receiveLeftClick(int x,int y,bool playSound=true){base.receiveLeftClick(x,y,playSound);foreach(var b in buttons)if(b.Bounds.Contains(x,y)){b.Action();return;}goal.Selected=new Rectangle(goal.X,goal.Y,goal.Width,48).Contains(x,y);}
    public override void receiveKeyPress(Keys key){if(!goal.Selected)base.receiveKeyPress(key);else if(key==Keys.Escape)goal.Selected=false;}
    protected override void cleanupBeforeExit(){goal.Selected=false;base.cleanupBeforeExit();}
    public override void draw(SpriteBatch b) {
        b.Draw(Game1.staminaRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.55f);
        b.Draw(Game1.staminaRect,new Rectangle(xPositionOnScreen,yPositionOnScreen,width,height),new Color(248,239,216));
        void Text(string text,int x,int y,float scale=.8f)=>b.DrawString(mod.Font,text,new(xPositionOnScreen+x,yPositionOnScreen+y),new Color(40,68,56),0,Vector2.Zero,scale,SpriteEffects.None,1);
        Text("DeepSeek 自主游玩",28,22,1.1f);
        Text("控制玩家与同行伙伴。读取地图、查手册，按真实操作推进存档。",28,70);
        Text("这份存档的目标：",28,106);goal.Draw(b);
        foreach(var item in buttons){b.Draw(Game1.staminaRect,item.Bounds,new Color(214,226,205));b.DrawString(mod.Font,item.Label,new(item.Bounds.X+8,item.Bounds.Y+7),Color.DarkSlateGray,0,Vector2.Zero,.72f,SpriteEffects.None,1);}
        Text($"时间：原生正常速度 · 决策间隔：{mod.Settings.AutoplayDecisionDelayMs} 毫秒 · 每日上限：{mod.Settings.AutoplayMaxCallsPerDay} 次",28,272);
        Text($"任务队列：{mod.Data.Autoplay.Schedule.Tasks.Count(t=>t.state=="queued")} 等待 · {mod.Data.Autoplay.Schedule.Tasks.Count(t=>t.state=="running")} 执行 · {mod.Data.Autoplay.Schedule.Tasks.Count(t=>t.state is "failed" or "blocked" or "needs_review")} 需调整",28,308,.72f);
        Text(ModelRequestBudget.Display(),28,338,.65f);
        Text("F10 / 方向键接回控制；保存开始后先完成换日。",28,360,.65f);
        Text(Game1.parseText("状态："+(mod.Data.Autoplay.Status=="running"?"自主游玩中":mod.Data.Autoplay.Status=="paused"?"已暂停":"尚未开始")+" · "+mod.Data.Autoplay.Detail,mod.Font,1010),28,378,.72f);
        base.draw(b);drawMouse(b);
    }
}
