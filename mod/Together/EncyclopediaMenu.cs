using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Together;

public sealed class EncyclopediaMenu:IClickableMenu {
    private readonly ModEntry mod;
    private readonly TextBox search,question,note;
    private readonly List<(Rectangle Rect,string Label,Action Click)> buttons=new();
    private IReadOnlyList<KnowledgeHit> results=Array.Empty<KnowledgeHit>();
    private KnowledgePacket? packet;
    private string previous="",kind="all",selected="";
    private int offset,scroll,revision=-1;
    private bool favorites,followingAnswer;
    private DateTime searchAt=DateTime.MinValue;
    private readonly Stack<string> history=new();
    private string previewId="";
    private Item? previewItem;
    private string lastBody="";
    private string[] wrapped=Array.Empty<string>();
    private static readonly Color Paper=new(248,239,216),Ink=new(43,65,57),Green=new(65,109,86),Gold=new(150,104,45);
    // Lookup Anything's documented optional custom-menu compatibility.
    public Item? HoveredItem {get;private set;}
    public NPC? HoveredNpc {get;private set;}
    internal KnowledgePacket? Evidence=>packet;
    internal void CheckButton(string label) {
        var button=buttons.First(b=>b.Label==label);receiveLeftClick(button.Rect.Center.X,button.Rect.Center.Y,false);
    }
    public EncyclopediaMenu(ModEntry mod,string query=""):base(0,0,Math.Min(1120,Game1.uiViewport.Width-24),Math.Min(760,Game1.uiViewport.Height-24),false) {
        this.mod=mod;xPositionOnScreen=(Game1.uiViewport.Width-width)/2;yPositionOnScreen=(Game1.uiViewport.Height-height)/2;
        search=Input(22,106,274,query,120);question=Input(324,height-143,width-458,"",240);note=Input(324,height-92,width-458,"",240);
        search.OnEnterPressed+=_=>Search();question.OnEnterPressed+=_=>Ask();note.OnEnterPressed+=_=>SaveNote();
        Rebuild();Search();
    }
    private TextBox Input(int x,int y,int w,string text,int limit)=>new(Game1.content.Load<Texture2D>("LooseSprites/textBox"),null,mod.Font,Ink){X=xPositionOnScreen+x,Y=yPositionOnScreen+y,Width=w,Height=40,Text=text,textLimit=limit};
    private void Button(int x,int y,int w,string label,Action click)=>buttons.Add((new Rectangle(xPositionOnScreen+x,yPositionOnScreen+y,w,32),label,click));
    private void Rebuild() {
        buttons.Clear();Button(width-58,16,36,"×",()=>exitThisMenu());Button(22,60,90,"同行",()=>{exitThisMenu();mod.Open();});
        Button(122,60,90,"日历",()=>Show("guide:calendar"));Button(222,60,90,"农场",()=>Show("guide:growth"));Button(322,60,90,"献祭",()=>Show("guide:bundle"));
        Button(422,60,108,favorites?"全部条目":"我的收藏",()=>{favorites=!favorites;offset=0;Search();});
        Button(542,60,170,mod.Data.Knowledge.DiscoveredOnly?"范围：随探索解锁":"范围：完整资料",()=>{mod.ToggleKnowledgeMode();selected="";packet=null;followingAnswer=false;scroll=0;Search();Rebuild();});
        Button(724,60,90,"刷新",()=>{mod.RefreshKnowledgeFacts();Search();if(selected!="")Show(selected,false);});
        Button(824,60,90,"最近",()=>{results=mod.Data.Knowledge.Recent.Select(id=>mod.Knowledge.Index.Get(id)).Where(e=>e!=null && mod.Knowledge.Visible(e)).Select(e=>new KnowledgeHit(e!,1,"最近查阅")).ToArray();offset=0;});
        Button(22,156,130,KnowledgeRules.Kind(kind)=="all"?"全部分类":KnowledgeRules.Kind(kind),()=>{var kinds=new[]{"all","item","crop","fish","npc","recipe","machine","location","guide"};kind=kinds[(Array.IndexOf(kinds,kind)+1)%kinds.Length];offset=0;Search();Rebuild();});
        Button(164,156,132,"搜索",Search);
        Button(22,height-53,70,"上一页",()=>{offset=Math.Max(0,offset-PageSize);});Button(226,height-53,70,"下一页",()=>{offset=Math.Min(Math.Max(0,results.Count-1),offset+PageSize);});
        Button(324,106,70,"返回",()=>{if(history.Count>0)Show(history.Pop(),false);});
        Button(405,106,90,"收藏",()=>{if(selected=="")return;if(!mod.Data.Knowledge.Favorites.Add(selected))mod.Data.Knowledge.Favorites.Remove(selected);});
        Button(506,106,122,"加入计划",()=>{if(selected!="")mod.PinKnowledge(selected);});
        Button(640,106,108,"相关条目",()=>{
            if(mod.Knowledge.Index.Get(selected) is {} e){var links=e.Links.Concat(mod.Knowledge.Index.Entries.Where(x=>x.Links.Contains(e.Id)).Select(x=>x.Id));results=links.Distinct().Select(id=>mod.Knowledge.Index.Get(id)).Where(x=>x!=null&&mod.Knowledge.Visible(x)).Select(x=>new KnowledgeHit(x!,1,"关联资料")).ToArray();offset=0;}
        });
        Button(760,106,130,"共同心愿",()=>mod.OpenGoals());
        Button(width-124,height-143,100,"问问伙伴",Ask);Button(width-124,height-92,100,"保存便签",SaveNote);
    }
    private int PageSize=>Math.Max(3,(height-265)/57);
    private void Search() {
        previous=search.Text;offset=0;
        results=mod.Knowledge.Index.Search(search.Text,e=>mod.Knowledge.Visible(e) && (!favorites || mod.Data.Knowledge.Favorites.Contains(e.Id)),kind,20000);
    }
    private void Show(string id,bool push=true) {
        var entry=mod.Knowledge.Index.Get(id);if(entry==null || !mod.Knowledge.Visible(entry))return;
        followingAnswer=false;
        if(push && selected!="" && id!=selected)history.Push(selected);
        selected=id;packet=mod.Knowledge.Query(entry.Name,id);mod.Data.Knowledge.Visit(id);note.Text=mod.Data.Knowledge.Notes.GetValueOrDefault(id,"");scroll=0;
    }
    private void Ask() {
        string q=question.Text.Trim();if(q.Length==0)q=selected==""?search.Text:"请解释这个条目和我们今天有什么关系。";
        if(q.Length==0)return;
        mod.AskKnowledge(q,selected.Length==0?null:selected);packet=mod.LastKnowledge;followingAnswer=true;scroll=0;question.Text="";
    }
    private void SaveNote(){if(selected=="")return;mod.Data.Knowledge.Note(selected,note.Text);Show(selected,false);}
    public override void update(GameTime time) {
        base.update(time);foreach(var f in new[]{search,question,note})f.Update();
        if(followingAnswer && mod.LastKnowledge!=null && packet!=mod.LastKnowledge){packet=mod.LastKnowledge;scroll=0;}
        if(revision!=mod.Knowledge.Revision){revision=mod.Knowledge.Revision;packet=null;Search();if(selected!="")Show(selected,false);}
        if(previous!=search.Text && DateTime.UtcNow>=searchAt){Search();searchAt=DateTime.UtcNow.AddMilliseconds(250);}
    }
    public override void receiveLeftClick(int x,int y,bool playSound=true) {
        foreach(var b in buttons)if(b.Rect.Contains(x,y)){Game1.playSound("smallSelect");b.Click();return;}
        for(int i=0;i<PageSize && offset+i<results.Count;i++)if(new Rectangle(xPositionOnScreen+22,yPositionOnScreen+204+i*57,274,52).Contains(x,y)){Show(results[offset+i].Entry.Id);return;}
        foreach(var f in new[]{search,question,note}){f.Selected=new Rectangle(f.X,f.Y,f.Width,44).Contains(x,y);if(f.Selected)f.SelectMe();}
    }
    public override void receiveKeyPress(Keys key){if(key==Keys.Escape){exitThisMenu();return;}if(key==Keys.Tab){var a=new[]{search,question,note};int i=Array.FindIndex(a,x=>x.Selected);foreach(var f in a)f.Selected=false;a[(i+1)%a.Length].SelectMe();}}
    public override void receiveScrollWheelAction(int direction){if(Game1.getMouseX()<xPositionOnScreen+310)offset=Math.Clamp(offset+(direction>0?-1:1),0,Math.Max(0,results.Count-PageSize));else scroll=Math.Max(0,scroll+(direction>0?-3:3));}
    protected override void cleanupBeforeExit(){foreach(var f in new[]{search,question,note}){f.Selected=false;if(Game1.keyboardDispatcher.Subscriber==f)Game1.keyboardDispatcher.Subscriber=null;}base.cleanupBeforeExit();}
    private void Text(SpriteBatch b,string text,int x,int y,Color? color=null,float scale=.8f)=>b.DrawString(mod.Font,text,new Vector2(xPositionOnScreen+x,yPositionOnScreen+y),color??Ink,0,Vector2.Zero,scale,SpriteEffects.None,1);
    private string Short(string s,int w){while(s.Length>1 && mod.Font.MeasureString(s).X*.8f>w)s=s[..^1];return s;}
    public override void draw(SpriteBatch b) {
        b.Draw(Game1.staminaRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.5f);
        b.Draw(Game1.staminaRect,new Rectangle(xPositionOnScreen,yPositionOnScreen,width,height),Paper);
        Text(b,"共同手册 · 我们的资料、农事和发现",22,18,Green,1f);
        Text(b,Short(mod.Knowledge.Status,280),width-355,22,Gold,.7f);
        b.Draw(Game1.staminaRect,new Rectangle(xPositionOnScreen+307,yPositionOnScreen+104,2,height-158),Green*.25f);
        HoveredItem=null;HoveredNpc=null;
        for(int i=0;i<PageSize && offset+i<results.Count;i++) {
            var hit=results[offset+i];var rect=new Rectangle(xPositionOnScreen+22,yPositionOnScreen+204+i*57,274,52);
            bool hover=rect.Contains(Game1.getMouseX(),Game1.getMouseY());
            b.Draw(Game1.staminaRect,rect,hit.Entry.Id==selected?new Color(212,227,201):hover?new Color(233,227,207):new Color(242,234,215));
            Text(b,Short((mod.Data.Knowledge.Favorites.Contains(hit.Entry.Id)?"★ ":"")+hit.Entry.Name,256),30,211+i*57);
            Text(b,KnowledgeRules.Kind(hit.Entry.Kind)+" · "+hit.Reason,30,235+i*57,Gold,.58f);
            if(hover){if(hit.Entry.Id.StartsWith("(")){if(previewId!=hit.Entry.Id){previewId=hit.Entry.Id;previewItem=KnowledgeCatalog.PreviewItem(previewId);}HoveredItem=previewItem;}if(hit.Entry.Id.StartsWith("npc:"))HoveredNpc=Game1.getCharacterFromName(hit.Entry.Id[4..]);}
        }
        if(results.Count==0)Text(b,"未找到条目，可试名称或分类。",22,215,Gold,.7f);
        Text(b,$"{Math.Min(offset+1,results.Count)}–{Math.Min(offset+PageSize,results.Count)} / {results.Count}",100,height-47,Gold,.7f);
        var p=packet;string body=p==null?"左侧搜索或点击上方日历。\n\n输入名字、别名或少量错字；范围默认随探索解锁。\n\n下方输入问题让伙伴解释，也可以留下自己的便签。":
            p.Observed+"\n"+(p==mod.LastKnowledge && mod.KnowledgeAnswer.Length>0?"伙伴说："+mod.KnowledgeAnswer+"\n\n":"")+
            string.Join("\n\n",p.Facts.Select(f=>f.Label+"\n"+f.Value+"\n依据："+f.Source+(f.Support=="partial"?" · 部分支持":"")));
        if(lastBody!=body){lastBody=body;wrapped=KnowledgeRules.Wrap(body,(width-362)/.78f,c=>mod.Font.MeasureString(c.ToString()).X);}
        string[] lines=wrapped;int visible=Math.Max(3,(height-330)/23);
        scroll=Math.Clamp(scroll,0,Math.Max(0,lines.Length-visible));
        for(int i=0;i<visible && scroll+i<lines.Length;i++)Text(b,lines[scroll+i],324,155+i*23,Ink,.78f);
        foreach(var f in new[]{search,question,note})f.Draw(b,false);
        if(question.Text.Length==0)Text(b,"输入问题，伙伴会查阅真实资料…",334,height-131,Gold,.7f);
        if(note.Text.Length==0)Text(b,"写一句我们的便签…",334,height-80,Gold,.7f);
        foreach(var button in buttons){b.Draw(Game1.staminaRect,button.Rect,new Color(221,226,206));var size=mod.Font.MeasureString(button.Label)*.72f;b.DrawString(mod.Font,button.Label,new Vector2(button.Rect.Center.X-size.X/2,button.Rect.Center.Y-size.Y/2),Ink,0,Vector2.Zero,.72f,SpriteEffects.None,1);}
        Text(b,Short(mod.Thinking?"伙伴正在查阅和思考；本地资料仍可浏览。":mod.Notice,width-365),324,height-38,Green,.66f);drawMouse(b);
    }
}
