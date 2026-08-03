using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Together;

public sealed class CompanionMenu:IClickableMenu {
    private readonly ModEntry mod;
    private readonly List<(Rectangle Rect,string Text,Action Click)> buttons=new();
    private readonly List<TextBox> fields=new();
    private TextBox? chat;
    private int tab,scroll;
    private static readonly Color Paper=new(248,239,216),Ink=new(43,65,57),Green=new(65,109,86),Gold=new(184,132,66);
    public CompanionMenu(ModEntry mod,int initialTab=0):base(0,0,Math.Min(1000,Game1.uiViewport.Width-40),Math.Min(670,Game1.uiViewport.Height-32),false) {
        this.mod=mod; tab=initialTab; xPositionOnScreen=(Game1.uiViewport.Width-width)/2;yPositionOnScreen=(Game1.uiViewport.Height-height)/2;
        Rebuild();
    }
    private void Button(int x,int y,int w,int h,string label,Action action)=>buttons.Add((new Rectangle(xPositionOnScreen+x,yPositionOnScreen+y,w,h),label,action));
    private TextBox Input(int x,int y,int w,string value,int limit=240) {
        var box=new TextBox(Game1.content.Load<Texture2D>("LooseSprites/textBox"),null,mod.Font,Ink){X=xPositionOnScreen+x,Y=yPositionOnScreen+y,Width=w,Height=40,Text=value,textLimit=limit};
        fields.Add(box);return box;
    }
    private void Rebuild() {
        foreach(var box in fields)box.Selected=false;
        fields.Clear();buttons.Clear();chat=null;
        Button(width-64,18,40,36,"×",()=>exitThisMenu());
        Button(24,112,130,36,"换个队友",()=>{
            var names=mod.Names();int index=Array.IndexOf(names,mod.Selected);mod.Select(names[(index+1)%names.Length]);scroll=0;Rebuild();
        });
        Button(166,112,130,36,"邀请同行",()=>mod.Recruit());
        Button(310,112,130,36,"各忙各的",()=>mod.Dismiss());
        for(int i=0;i<5;i++){int next=i;Button(width-500+i*95,112,89,36,new[]{"聊天","人设","经历","心事","农场"}[i],()=>{tab=next;scroll=0;Rebuild();});}
        if(tab==0) {
            chat=Input(32,height-112,width-178,"",400);
            chat.OnEnterPressed+=_=>Send();
            Button(width-130,height-112,98,44,"发送",Send);
            Button(28,height-205,110,36,"同意约定",()=>mod.AcceptProposal(false));
            Button(148,height-205,90,36,"下次吧",()=>mod.Decline());
            Button(248,height-205,90,36,"强制",()=>mod.AcceptProposal(true));
            Button(348,height-205,90,36,"停止",()=>mod.Cancel());
            Button(448,height-205,90,36,"继续",()=>mod.Resume());
            var shortcuts=new[]{("mine","挖矿"),("water","浇水"),("fish","钓鱼"),("guard","保护我"),("rest","休息")};
            for(int i=0;i<shortcuts.Length;i++){var item=shortcuts[i];Button(28+i*108,height-158,98,30,item.Item2,()=>mod.Quick(item.Item1));}
            Button(width-204,height-158,172,30,mod.Settings.Autonomy?"自主生活：开":"自主生活：关",()=>{mod.ToggleAuto();Rebuild();});
        } else if(tab==1) {
            var names=new[]{"冒险搭子","钓鱼搭子","贴心恋人","农场伙伴"};
            for(int i=0;i<4;i++){string name=names[i];Button(28+i*166,178,152,36,name,()=>{mod.SetPreset(name);Rebuild();});}
            var p=mod.Current.Profile;
            int gap=Math.Clamp((height-350)/5,38,54);
            Input(152,230,width-188,p.Role,80);Input(152,230+gap,width-188,p.Traits,160);
            Input(152,230+gap*2,width-188,p.Likes,160);Input(152,230+gap*3,width-188,p.Dislikes,160);Input(152,230+gap*4,width-188,p.Style,160);
            Button(28,height-108,168,42,"保存我的人设",()=>{
                var profile=mod.Current.Profile;profile.Role=fields[0].Text;profile.Traits=fields[1].Text;
                profile.Likes=fields[2].Text;profile.Dislikes=fields[3].Text;profile.Style=fields[4].Text;
                mod.Persist();Game1.playSound("coin");
            });
        } else if(tab==4) {
            Button(28,176,140,36,"一起管农场",()=>{mod.AddFarmProject();Rebuild();});
            Button(182,176,164,36,"准备一项献祭",()=>{mod.AddBundleProject();Rebuild();});
            Button(360,176,132,36,mod.Data.FarmHelp?"农活自主：开":"农活自主：关",()=>{mod.Data.FarmHelp=!mod.Data.FarmHelp;Rebuild();});
            Button(506,176,186,36,"切换附近箱子用途",()=>mod.CycleNearbyChest());
            int projectRow=0;
            foreach(var project in mod.Data.Projects.Where(p=>p.Status is "active" or "paused").Take(3)) {
                string id=project.Id;Button(width-122,338+72*projectRow++,98,28,project.Status=="active"?"暂停计划":"继续计划",()=>{mod.PauseProject(id);Rebuild();});
            }
            var paces=new[]{("relaxed","慢慢生活"),("balanced","劳逸结合"),("focused","推进目标")};
            for(int i=0;i<paces.Length;i++){var pace=paces[i];Button(28+i*160,height-102,150,36,(mod.Data.Pace==pace.Item1?"● ":"")+pace.Item2,()=>{mod.SetPace(pace.Item1);Rebuild();});}
        }
    }
    private void Send() {
        if(chat==null || string.IsNullOrWhiteSpace(chat.Text))return;
        mod.Send(chat.Text.Trim());chat.Text="";
        // Return to play so NPCs can move while the model replies asynchronously.
        exitThisMenu();
    }
    public override void receiveLeftClick(int x,int y,bool playSound=true) {
        foreach(var button in buttons)if(button.Rect.Contains(x,y)){if(playSound)Game1.playSound("smallSelect");button.Click();return;}
        foreach(var box in fields) {
            box.Selected=new Rectangle(box.X,box.Y,box.Width,48).Contains(x,y);
            if(box.Selected)box.SelectMe();
        }
    }
    public override void receiveKeyPress(Keys key) {
        if(key==Keys.Escape){exitThisMenu();return;}
        if(key==Keys.Tab && fields.Count>0){int i=fields.FindIndex(f=>f.Selected);foreach(var box in fields)box.Selected=false;fields[(i+1)%fields.Count].SelectMe();return;}
    }
    public override void receiveScrollWheelAction(int direction){
        scroll=tab==0?Math.Max(0,scroll+(direction>0?60:-60)):Math.Clamp(scroll+(direction>0?1:-1),0,Math.Max(0,mod.Current.Memories.Count-1));
    }
    protected override void cleanupBeforeExit(){foreach(var box in fields)box.Selected=false;if(Game1.keyboardDispatcher.Subscriber is TextBox subscriber && fields.Contains(subscriber))Game1.keyboardDispatcher.Subscriber=null;base.cleanupBeforeExit();}
    public override void update(GameTime time){foreach(var box in fields)box.Update();base.update(time);}
    private void Text(SpriteBatch b,string text,int x,int y,Color? color=null,float scale=1)=>b.DrawString(mod.Font,text,new Vector2(xPositionOnScreen+x,yPositionOnScreen+y),color??Ink,0,Vector2.Zero,scale,SpriteEffects.None,1);
    private void Panel(SpriteBatch b,int x,int y,int w,int h,Color color){b.Draw(Game1.staminaRect,new Rectangle(xPositionOnScreen+x,yPositionOnScreen+y,w,h),color);}
    private string Wrap(string text,int available)=>Game1.parseText(text,mod.Font,available);
    public override void draw(SpriteBatch b) {
        b.Draw(Game1.staminaRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.55f);
        Panel(b,-5,-5,width+10,height+10,Ink);Panel(b,0,0,width,height,Paper);Panel(b,0,0,width,100,Green);
        var npc=Game1.getCharacterFromName(mod.Selected);
        if(npc!=null)b.Draw(npc.Portrait,new Rectangle(xPositionOnScreen+22,yPositionOnScreen+14,74,74),new Rectangle(0,0,64,64),Color.White);
        Text(b,"同行 / TOGETHER",114,13,Paper,1.12f);
        Text(b,(npc?.displayName??mod.Selected)+"  ·  "+mod.Current.Profile.Role,114,46,Paper,.85f);
        Text(b,$"精力 {mod.Current.Energy}   亲近 {mod.Current.Bond}   ·   {mod.Current.Mood}",114,72,new Color(220,231,208),.72f);
        Text(b,$"今日对话 {mod.Data.Calls}/{mod.Settings.MaxCallsPerDay}",width-248,69,Paper,.72f);
        Panel(b,22,158,width-44,2,new Color(210,200,175));
        if(tab==0)DrawChat(b);
        else if(tab==1) {
            var labels=new[]{"关系称呼","性格","喜欢什么","不喜欢什么","说话方式"};
            for(int i=0;i<labels.Length;i++)Text(b,labels[i],30,240+i*Math.Clamp((height-350)/5,38,54));
            Text(b,"称呼由你自定义；游戏原生好感与婚姻仍按存档读取。",220,height-98,Gold,.78f);
        } else if(tab==3) {
            Text(b,Wrap(mod.LifeSummary(),width-80),32,186,Ink,.9f);
            Text(b,"自己的安排可以被你的请求打断；处理完后会尝试接着做。",32,height-122,Green,.78f);
        } else if(tab==4) {
            var f=mod.Facts;
            string season=f.Season switch {"spring"=>"春","summer"=>"夏","fall"=>"秋","winter"=>"冬",_=>f.Season};
            string route=f.Route switch {"joja"=>"Joja 路线","community_complete"=>"社区中心已完成",_=>"尚未加入 Joja"};
            Text(b,$"{season} · {f.Time/100}:{f.Time%100:00}  |  资金 {f.Money} 金  |  {route}",28,232,Green,.82f);
            Text(b,Wrap(string.Join("；",f.Alerts),width-80),28,268,Ink,.8f);
            int y=342;
            foreach(var project in mod.Data.Projects.Where(x=>x.Status is "active" or "paused").Take(3)) {
                if(y>height-170)break;
                Text(b,Wrap((project.Status=="paused"?"已暂停 · ":"")+project.Title+"："+project.Detail+(project.Needs.Count>0?"\n"+string.Join(" / ",project.Needs.Take(3).Select(n=>n.Name+"缺"+n.Missing)):""),width-200),28,y,Ink,.78f);y+=72;
            }
            if(!mod.Data.Projects.Any(p=>p.Status=="active"))Text(b,"先选一个共同安排。大事由你决定，小事我们分担。",28,344,Gold,.8f);
        } else {
            Text(b,"我们一起做过的事",28,178,Green);
            var entries=mod.Current.Memories.AsEnumerable().Reverse().Skip(scroll).Take(5).ToArray();int y=220;
            if(entries.Length==0)Text(b,"一起完成一个小约定，这里就会留下记忆。",32,y,Gold,.85f);
            foreach(var entry in entries){if(y>height-130)break;Text(b,$"第 {entry.Day+1} 天  ·  {entry.Who}",32,y,Gold,.75f);y+=27;Text(b,Wrap(entry.Text,width-84),32,y,Ink,.82f);y+=64;}
        }
        foreach(var box in fields)box.Draw(b,false);
        foreach(var button in buttons) {
            bool hover=button.Rect.Contains(Game1.getMouseX(),Game1.getMouseY());
            b.Draw(Game1.staminaRect,button.Rect,hover?new Color(213,226,202):new Color(229,222,201));
            var size=mod.Font.MeasureString(button.Text)*.83f;
            b.DrawString(mod.Font,button.Text,new Vector2(button.Rect.Center.X-size.X/2,button.Rect.Center.Y-size.Y/2),Ink,0,Vector2.Zero,.83f,SpriteEffects.None,1);
        }
        Text(b,Wrap(mod.Thinking?"正在想怎么回答你… 可以关闭面板继续玩。":mod.Notice,width-56),28,height-48,Green,.75f);
        drawMouse(b);
    }
    private void DrawChat(SpriteBatch b) {
        int top=176,end=height-302;
        var lines=mod.Current.Chat.Select(l=>(Entry:l,Rows:Wrap(l.Text,width-88).Split('\n'))).ToArray();
        int total=lines.Sum(l=>33+l.Rows.Length*28);
        scroll=Math.Clamp(scroll,0,Math.Max(0,total-(end-top)));
        int y=top+Math.Min(0,end-top-total)+scroll;
        if(lines.Length==0)Text(b,"先打个招呼，或挑一个小活动。你们可以边玩边商量。",30,top,Gold,.85f);
        foreach(var line in lines) {
            if(y>=top && y+22<=end)Text(b,line.Entry.Who,30,y,line.Entry.Who=="你"?Gold:Green,.75f);y+=25;
            foreach(var text in line.Rows){if(y>=top && y+25<=end)Text(b,text,44,y,Ink,.85f);y+=28;}
            y+=8;
        }
        Panel(b,22,height-291,width-44,74,new Color(234,226,199));
        var proposal=mod.Current.Proposal;
        Text(b,proposal!=null?"小约定 · "+proposal.title:ModEntry.JobText(mod.Current.Job),34,height-283,Green,.9f);
        Text(b,Wrap(proposal?.PlanText()??mod.Current.Job?.Detail??"同意才开始；拒绝后可换个提议，强制会影响亲近感。",width-80),34,height-250,Ink,.76f);
    }
}
