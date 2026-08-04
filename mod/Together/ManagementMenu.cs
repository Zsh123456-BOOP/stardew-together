using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace Together;
public sealed partial class CompanionMenu {
    private int selectedSeed;
    private bool confirmForget;
    private SeedFact[] Seeds=>mod.Facts.Seeds.Where(s=>s.Seasons.Contains(mod.Facts.Season)).OrderBy(s=>s.Days).ToArray();
    private SeedFact? Seed=>Seeds.Length==0?null:Seeds[Math.Abs(selectedSeed)%Seeds.Length];
    private ProgressGoal[] Goals=>mod.Facts.Goals.Where(g=>!g.Complete).OrderBy(g=>g.Deadline<0?int.MaxValue:g.Deadline).ToArray();
    private void BuildManagement() {
        string[] pages={"地块","购物与预算","共同目标","今日分工"};
        for(int i=0;i<pages.Length;i++){int page=i;Button(28+i*182,176,172,34,(managePage==i?"● ":"")+pages[i],()=>{managePage=page;choiceIndex=0;Rebuild();});}
        if(managePage==0) {
            Button(28,226,170,36,"换一种当季种子",()=>{selectedSeed++;Rebuild();});
            Button(212,226,228,36,"脚下划定 3×3 种植区",()=>{if(Seed!=null)mod.AddPlantingArea(Seed.Item);Rebuild();});
            Button(454,226,270,36,mod.Data.FarmPolicy.ClearDesignatedPlots?"地块杂草树枝清理：开":"地块杂草树枝清理：关",()=>{mod.Data.FarmPolicy.ClearDesignatedPlots=!mod.Data.FarmPolicy.ClearDesignatedPlots;mod.RefreshManagement();Rebuild();});
            int row=0;foreach(var area in mod.Data.FarmPolicy.Areas.TakeLast(4)) {
                string id=area.Id;Button(width-148,326+row++*54,120,30,"取消此地块",()=>{mod.RemoveArea(id);Rebuild();});
            }
        } else if(managePage==1) {
            var policy=mod.Data.FarmPolicy;
            Input(200,238,140,policy.DailyBudget.ToString(),6);Input(520,238,140,policy.KeepGold.ToString(),7);
            Button(688,238,124,36,"保存预算",()=>{if(int.TryParse(fields[0].Text,out int budget) && int.TryParse(fields[1].Text,out int keep))mod.SetFarmBudget(budget,keep);});
            Button(28,294,170,36,"换种子",()=>{selectedSeed++;Rebuild();});
            Button(214,294,210,36,"买 9 份所选种子",()=>{if(Seed!=null)mod.AddShopping("SeedShop",Seed.Item,9,500);Rebuild();});
            Button(438,294,170,36,"买 12 份干草",()=>{mod.AddShopping("AnimalShop","(O)178",12,50);Rebuild();});
            int row=0;foreach(var order in policy.Shopping.Where(o=>o.Enabled).TakeLast(3)){string id=order.Id;Button(width-142,399+row++*48,112,30,"取消采购",()=>{mod.StopShopping(id);Rebuild();});}
        } else if(managePage==2) {
            Button(28,228,140,36,"上个目标",()=>{choiceIndex=Math.Max(0,choiceIndex-1);Rebuild();});
            Button(182,228,140,36,"下个目标",()=>{choiceIndex++;Rebuild();});
            Button(336,228,180,36,"加入共同安排",()=>{if(Goals.Length>0)mod.AddProgressProject(Goals[choiceIndex%Goals.Length].Id);Rebuild();});
            int row=0;foreach(var project in mod.Data.Projects.Where(p=>p.Status is "active" or "paused").TakeLast(3)) {
                string id=project.Id;Button(width-300,432+row*45,140,30,"分工："+(project.Owner=="player"?"我":project.Owner=="together"?"一起":project.Owner),()=>{mod.AssignProject(id);Rebuild();});
                Button(width-148,432+row++*45,120,30,project.Status=="paused"?"继续":"暂停",()=>{mod.PauseProject(id);Rebuild();});
            }
        } else {
            Button(28,226,140,34,"上一页",()=>{choiceIndex=Math.Max(0,choiceIndex-1);Rebuild();});
            Button(182,226,140,34,"下一页",()=>{choiceIndex++;Rebuild();});
        }
    }
    private void DrawManagement(SpriteBatch b) {
        if(managePage==0) {
            Text(b,Seed==null?"未读取到种子":$"{Seed.Name} · 生长 {Seed.Days} 天 · 季节 {string.Join("/",Seed.Seasons)}",28,282,Green,.8f);
            int row=0;foreach(var area in mod.Data.FarmPolicy.Areas.TakeLast(4))Text(b,$"{SocialState.PlaceName(area.Location)} ({area.X},{area.Y}) {area.Width}×{area.Height} · {ItemRegistry.GetDataOrErrorItem(area.Seed).DisplayName}",28,330+row++*54,Ink,.76f);
            Text(b,Wrap("先站到地块左上角，再划定。伙伴从原料箱取种子，先翻空地再播种；临近换季不能成熟时会保留种子。",width-68),28,height-118,Gold,.76f);
        } else if(managePage==1) {
            Text(b,"每天最多花",28,247,Ink,.82f);Text(b,"至少留下",368,247,Ink,.82f);
            Text(b,"所选："+(Seed?.Name??"无")+$" · 今日已花 {mod.Facts.SpentToday} 金；清单不会每天重复下单",28,348,Green,.74f);
            int row=0;foreach(var order in mod.Data.FarmPolicy.Shopping.Where(o=>o.Enabled).TakeLast(3))Text(b,$"{ItemRegistry.GetDataOrErrorItem(order.Item).DisplayName} ×{order.Count}（已买 {mod.Facts.Purchased.GetValueOrDefault(order.Id)}） · 单价上限 {order.MaxUnitPrice}",28,404+row++*48,Ink,.76f);
            Text(b,Wrap("原料箱用于取料，收货箱用于存放，待售箱表示允许出售；用途在农场页切换。保留资金与项目预留物资优先。",width-68),28,height-112,Gold,.74f);
        } else if(managePage==2) {
            if(Goals.Length>0) {
                var g=Goals[choiceIndex%Goals.Length];
                Text(b,Wrap(g.Title+(g.Deadline>=0?$" · 剩 {g.Deadline-mod.Facts.Day} 天":"")+"\n"+g.PlayerStep+"\n"+string.Join(" / ",g.Needs.Take(4).Select(n=>n.Name+" 缺"+n.Missing)),width-70),28,292,Ink,.8f);
            }
            int row=0;foreach(var p in mod.Data.Projects.Where(p=>p.Status is "active" or "paused").TakeLast(3))Text(b,Wrap(p.Title,width-340),28,436+row++*45,Green,.73f);
        } else {
            int y=286;foreach(var n in mod.Data.Today.Skip(choiceIndex*4).Take(4)) {
                Text(b,Wrap(n.Title+" · "+(n.Owner=="player"?"你来":n.Owner=="together"?"一起":n.Owner)+"\n"+n.Reason+(n.Location==""?"":" · "+SocialState.PlaceName(n.Location)),width-70),28,y,Ink,.78f);y+=76;
            }
        }
    }
    private void BuildSocial() {
        var modes=new[]{("normal","平常相处"),("quiet","安静陪着"),("holiday","放假一天")};
        for(int i=0;i<modes.Length;i++){var mode=modes[i];Button(28+i*184,180,174,36,(mod.Current.Social.Mode==mode.Item1?"● ":"")+mode.Item2,()=>{mod.SetSocialMode(mode.Item1);Rebuild();});}
        Button(28,226,140,34,"切换相处内容",()=>{choiceIndex=(choiceIndex+1)%4;Rebuild();});
        Button(184,226,148,34,"导出经历",()=>mod.ExportMemories());
        Button(width-190,226,162,34,confirmForget?"再次点击确认清除":"清除这位的记忆",()=>{if(confirmForget){mod.ClearMemories();confirmForget=false;}else confirmForget=true;Rebuild();});
        if(choiceIndex==1) {
            for(int i=0;i<mod.Current.Social.Habits.Count && i<3;i++) {
                int index=i;var h=mod.Current.Social.Habits[i];
                Button(width-210,352+i*68,180,32,h.Confirmed?(h.Enabled?"暂停这个习惯":"恢复这个习惯"):"定为我们的小习惯",()=>{mod.ConfirmHabit(index);Rebuild();});
            }
        } else if(choiceIndex==2) {
            Button(28,350,240,38,"约一个三天收获小挑战",()=>{mod.StartChallenge();Rebuild();});
            Button(284,350,172,38,"暂停小挑战",()=>{if(mod.Current.Social.Challenge is {} c)c.Status="cancelled";mod.Persist();Rebuild();});
        } else if(choiceIndex==3) {
            var t=mod.Current.Profile.Temperament;var values=new[]{("主动",t.Initiative),("社交",t.Sociability),("风险",t.RiskTolerance),("耐心",t.Patience),("计划",t.Planning)};
            for(int i=0;i<values.Length;i++){var v=values[i];Button(28+i*150,348,140,36,v.Item1+" "+v.Item2,()=>{mod.ChangeTrait(v.Item1);Rebuild();});}
        }
    }
    private void DrawSocial(SpriteBatch b) {
        var s=mod.Current.Social;var r=s.Relationship;
        Text(b,$"信任 {r.Trust} · 相处舒适 {r.Comfort} · 合作默契 {r.Cooperation}",28,284,Green,.85f);
        int y=340;
        if(choiceIndex==0) {
            foreach(var entry in s.Diary.TakeLast(3).Reverse()) {Text(b,Wrap($"第 {entry.Day+1} 天 · "+entry.Text+(entry.PlayerNote==""?"":" · 你的留言："+entry.PlayerNote),width-70),28,y,Ink,.76f);y+=76;}
            if(s.Diary.Count==0)Text(b,"今晚收工时，会根据真实经历记下一页日记。",28,y,Gold,.8f);
        } else if(choiceIndex==1) {
            foreach(var h in s.Habits.Take(3)){Text(b,Wrap(h.Title+"\n已经有 "+h.Days.Count+" 天的共同相处记录",width-258),28,y,Ink,.78f);y+=68;}
            if(s.Habits.Count==0)Text(b,"自愿一起做些喜欢的事，之后可以定为小习惯。",28,y,Gold,.8f);
        }
        if(choiceIndex==2) {
            var challenge=s.Challenge;
            Text(b,Wrap(challenge==null?"挑一个不用赶进度的小约定，一起收集点值得分享的东西。":$"状态 {(challenge.Status=="active"?"进行中":challenge.Status=="fulfilled"?"已达成":challenge.Status=="expired"?"已到期":"已暂停")} · 截止第 {challenge.Deadline+1} 天\n你钓到新图鉴：{(challenge.PlayerDone?"有":"还没有")} · 伙伴渔获：{(challenge.CompanionCatch==""?"还没有":ItemRegistry.GetDataOrErrorItem(challenge.CompanionCatch).DisplayName)}",width-70),28,416,Ink,.8f);
        } else if(choiceIndex==3)Text(b,Wrap("点击每项调整。主动：多久考虑自己的安排；社交：陪伴和分享意愿；风险：对矿洞活动的倾向；耐心：疲惫时愿意商量多久；计划：共同项目与农场分工的优先级。具体接受或拒绝还会结合人设、关系和环境。",width-70),28,416,Ink,.8f);
        Text(b,"聊天输入“记住：……”记录偏好；“忘记：……”删除匹配偏好。",28,height-98,Green,.75f);
    }
}
