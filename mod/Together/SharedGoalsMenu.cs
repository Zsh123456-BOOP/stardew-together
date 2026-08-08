using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed class SharedGoalsMenu:IClickableMenu {
    private readonly ModEntry mod;
    private readonly List<(Rectangle Rect,string Label,Action Click)> buttons=new();
    private int selected,offset;
    private string goalId="";
    private static readonly Color Ink=new(43,65,57),Paper=new(248,239,216),Green=new(65,109,86);
    private SharedGoal? Goal=>mod.Data.SharedGoals.FirstOrDefault(g=>g.Id==goalId);
    public SharedGoalsMenu(ModEntry mod):base(0,0,Math.Min(1120,Game1.uiViewport.Width-24),Math.Min(740,Game1.uiViewport.Height-24),false) {
        this.mod=mod;goalId=mod.Data.SharedGoals.LastOrDefault(g=>g.Status=="active")?.Id??mod.Data.SharedGoals.LastOrDefault()?.Id??"";
        xPositionOnScreen=(Game1.uiViewport.Width-width)/2;yPositionOnScreen=(Game1.uiViewport.Height-height)/2;Rebuild();
    }
    private void Button(int x,int y,int w,string text,Action click)=>buttons.Add((new(xPositionOnScreen+x,yPositionOnScreen+y,w,32),text,click));
    private void Rebuild() {
        buttons.Clear();Button(width-55,16,34,"×",()=>exitThisMenu());
        Button(24,66,110,"共同手册",()=>mod.OpenKnowledge());Button(146,66,110,"上个心愿",()=>Cycle(-1));Button(268,66,110,"下个心愿",()=>Cycle(1));
        Button(390,66,160,"切换聊天伙伴",()=>{var names=mod.Names();mod.Select(names[(Array.IndexOf(names,mod.Selected)+1)%names.Length]);Rebuild();});
        if(Goal is not {} goal)return;
        Button(562,66,110,goal.Status=="paused"?"继续心愿":"暂停心愿",()=>{mod.ToggleSharedGoal(goal.Id);Rebuild();});
        Button(806,66,86,"数量－",()=>{mod.ChangeGoalCount(goal.Id,-1);Rebuild();});Button(904,66,86,"数量＋",()=>{mod.ChangeGoalCount(goal.Id,1);Rebuild();});
        Button(684,66,110,"刷新进度",()=>{mod.RefreshKnowledgeFacts();Rebuild();});
        Button(24,height-125,100,"上页材料",()=>{offset=Math.Max(0,offset-PageSize);Rebuild();});
        Button(136,height-125,100,"下页材料",()=>{offset=Math.Min(Math.Max(0,goal.Nodes.Count-PageSize),offset+PageSize);Rebuild();});
        var node=goal.Nodes.ElementAtOrDefault(selected);
        if(node==null || goal.Status!="active")return;
        Button(256,height-125,90,"我来做",()=>{mod.AssignGoalNode(goal.Id,node.Id,"player");Rebuild();});
        Button(358,height-125,90,"一起做",()=>{mod.AssignGoalNode(goal.Id,node.Id,"together");Rebuild();});
        Button(460,height-125,150,"商量交给伙伴",()=>mod.RequestGoalWork(goal.Id,node.Id));
        Button(622,height-125,120,"强制这一步",()=>mod.RequestGoalWork(goal.Id,node.Id,true));
        Button(754,height-125,100,"查材料",()=>mod.OpenKnowledge(node.Name));
        if(mod.Current.Proposal?.option_id?.StartsWith("goal:")==true) {
            Button(256,height-80,150,"同意伙伴提议",()=>{mod.AcceptProposal(false);Rebuild();});
            Button(418,height-80,120,"换个安排",()=>{mod.Decline();Rebuild();});
        }
    }
    private int PageSize=>Math.Max(2,(height-350)/64);
    private void Cycle(int direction) {
        var goals=mod.Data.SharedGoals;if(goals.Count==0)return;
        int index=goals.FindIndex(g=>g.Id==goalId);goalId=goals[(index+direction+goals.Count)%goals.Count].Id;offset=selected=0;Rebuild();
    }
    private void Text(SpriteBatch b,string text,int x,int y,float scale=.75f,Color? color=null)=>b.DrawString(mod.Font,text,new(xPositionOnScreen+x,yPositionOnScreen+y),color??Ink,0,Vector2.Zero,scale,SpriteEffects.None,0);
    private string Fit(string text,int w,float scale=.75f) {
        while(text.Length>0 && mod.Font.MeasureString(text).X*scale>w)text=text[..^1];return text;
    }
    public override void draw(SpriteBatch b) {
        b.Draw(Game1.staminaRect,new Rectangle(xPositionOnScreen,yPositionOnScreen,width,height),Paper);
        Text(b,"我们的共同心愿",24,20,1f);Text(b,"商量对象："+mod.Selected,width-280,28,.7f);
        if(Goal is {} goal) {
            Text(b,Fit(goal.Title+" ×"+goal.Count+" · "+(goal.Status=="fulfilled"?"已经实现":goal.Status=="paused"?"暂时放一放":"慢慢一起准备"),width-50),24,115,.84f,Green);
            Text(b,Fit(goal.Summary,width-50),24,150,.72f);
            int row=0;foreach(var node in goal.Nodes.Skip(offset).Take(PageSize)) {
                int y=194+row*64;
                if(offset+row==selected)b.Draw(Game1.staminaRect,new Rectangle(xPositionOnScreen+20,yPositionOnScreen+y-5,width-40,60),new Color(222,231,204));
                string owner=node.Owner=="player"?"我":node.Owner=="together"?"一起":node.Owner;
                string status=node.Status switch {"ready"=>"已备齐","locked"=>"待解锁","player_step"=>"等你制作","waiting"=>"等材料","processing"=>"加工/待收","blocked"=>"需换途径",_=>"待准备"};
                Text(b,Fit($"{node.Name}：{node.Owned}/{node.Required}（加工中 {node.InProgress}） · {status} · {owner}",width-55),28,y);
                Text(b,Fit(node.Reason,width-55,.64f),28,y+28,.64f);row++;
            }
            var last=goal.History.LastOrDefault();if(last!=null)Text(b,Fit($"共同记录 · 第 {last.Day} 天：{last.Text}",width-50,.66f),24,height-170,.66f);
        } else Text(b,"去共同手册选一个想获得的物品，点“加入计划”。也可以聊天说“我想要……” 。",24,140,.7f);
        foreach(var button in buttons){b.Draw(Game1.staminaRect,button.Rect,new Color(218,222,192));Text(b,button.Label,button.Rect.X-xPositionOnScreen+7,button.Rect.Y-yPositionOnScreen+6,.66f);}
        Text(b,Fit(mod.Current.Proposal?.option_id?.StartsWith("goal:")==true?mod.Current.Proposal.speech:mod.Notice,width-48,.65f),24,height-35,.65f);
        drawMouse(b);
    }
    public override void update(GameTime time){base.update(time);Rebuild();}
    public override void receiveLeftClick(int x,int y,bool playSound=true) {
        foreach(var button in buttons.ToArray())if(button.Rect.Contains(x,y)){button.Click();return;}
        int row=(y-yPositionOnScreen-189)/64;
        if(x>=xPositionOnScreen+20 && x<xPositionOnScreen+width-20 && y>=yPositionOnScreen+189 && row<PageSize && Goal!=null && offset+row<Goal.Nodes.Count){selected=offset+row;Rebuild();}
    }
    internal void CheckButton(string label){var button=buttons.First(b=>b.Label==label);receiveLeftClick(button.Rect.Center.X,button.Rect.Center.Y,false);}
    public override void receiveKeyPress(Keys key){if(key==Keys.Escape)exitThisMenu();}
    public override void receiveScrollWheelAction(int direction){if(Goal==null)return;offset=Math.Clamp(offset+(direction<0?1:-1),0,Math.Max(0,Goal.Nodes.Count-PageSize));Rebuild();}
}
