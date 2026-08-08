using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewModdingAPI;

namespace Together;
public sealed partial class ModEntry {
    public WorldFacts Facts {get;private set;}=new();
    private int factsMinute=-1;
    private DateTime factsAt=DateTime.MinValue;
    private bool pendingAutonomous;
    private List<ActivityOption> pendingOptions=new();
    private int autoCursor;
    private static int Minute=>Game1.timeOfDay/100*60+Game1.timeOfDay%100;
    private void RefreshFacts(bool force=false) {
        if(!force && factsMinute==Minute && Facts.Day==Game1.Date.TotalDays && DateTime.UtcNow<factsAt)return;
        try {Facts=WorldReader.Read(Data.People.Keys.ToArray());factsMinute=Minute;factsAt=DateTime.UtcNow.AddSeconds(10);UpdateProjects();}
        catch(Exception e){Monitor.Log("World snapshot unavailable: "+e.GetType().Name,LogLevel.Warn);}
    }
    public void AddFarmProject() {
        if(Data.Projects.Any(x=>x.Kind=="farm" && x.Status=="active")){Notice="今天的农活已经在共同安排里。";return;}
        Data.Projects.Add(new(){Kind="farm",Title="一起照料农场",CreatedDay=Game1.Date.TotalDays,Owner=Selected});
        RefreshFacts(true);Say(Selected,"农场这边我来分担。我们也给自己留一点玩的时间。");
    }
    public void AddBundleProject() {
        RefreshFacts(true);
        if(Facts.Route=="joja"){Notice="这份存档选择了 Joja 路线；献祭安排不适用。";return;}
        var bundle=Facts.Bundles.Where(b=>!b.Complete && !Data.Projects.Any(p=>p.Kind=="bundle:"+b.Id && p.Status=="active"))
            .OrderByDescending(b=>b.Name.Contains(Game1.currentSeason,StringComparison.OrdinalIgnoreCase)).ThenByDescending(b=>b.Missing.Count(r=>r.Missing==0)).ThenBy(b=>b.Missing.OrderBy(r=>r.Missing).Take(Math.Max(0,b.RequiredSlots-b.CompletedSlots)).Sum(r=>r.Missing)).FirstOrDefault();
        if(bundle==null){Notice="没有读到尚未完成的献祭目标。";return;}
        Data.Projects.Add(new(){Kind="bundle:"+bundle.Id,Title="一起准备："+bundle.Name,CreatedDay=Game1.Date.TotalDays,Owner="together"});
        UpdateProjects();Say(Selected,"先把材料缺口记下来。提交献祭由你来，我不会擅自替你完成。");
    }
    public void SetPace(string pace) {if(new[]{"relaxed","balanced","focused"}.Contains(pace)){Data.Pace=pace;Notice="共同生活的节奏已调整。";}}
    public void CycleNearbyChest() {
        var chest=Game1.currentLocation.objects.Values.OfType<StardewValley.Objects.Chest>()
            .Where(c=>Vector2.Distance(c.TileLocation,Game1.player.Tile)<=2.5f && c.SpecialChestType==StardewValley.Objects.Chest.SpecialChestTypes.None)
            .OrderBy(c=>Vector2.DistanceSquared(c.TileLocation,Game1.player.Tile)).FirstOrDefault();
        if(chest==null){Notice="先走到普通箱子旁边，再设置它的用途。";return;}
        const string key="stardewagent.together/chest-role";
        string role=chest.modData.TryGetValue(key,out var current)?current:"none";
        role=role=="none"?"supplies":role=="supplies"?"output":role=="output"?"sell":"none";
        chest.modData[key]=role;
        Notice=role=="sell"?"这个箱子现在是待售箱：允许将未预留物品运到出货箱，过夜出售。":role=="supplies"?"这个箱子现在是原料箱：允许取用未预留的物品给机器补料。":role=="output"?"这个箱子现在是收货箱：允许伙伴把随身物资存进来。":"已取消这个箱子的同行用途。";
        RefreshFacts(true);
    }
    public void PauseProject(string id) {var p=Data.Projects.FirstOrDefault(x=>x.Id==id);if(p!=null)p.Status=p.Status=="paused"?"active":"paused";UpdateProjects();}
    private void UpdateProjects() {
        Data.Reservations.Clear();
        foreach(var project in Data.Projects.Where(p=>p.Status=="active")) {
            project.ObservedDay=Facts.Day;
            if(project.Kind=="farm") {
                project.Remaining=Facts.DryCrops+Facts.RipeCrops+Facts.AnimalsUnpetted+Facts.MachinesReady;
                project.Detail=$"待浇水 {Facts.DryCrops}，待收获 {Facts.RipeCrops}，待照料动物 {Facts.AnimalsUnpetted}，待收机器 {Facts.MachinesReady}";
                // Daily care is recurring: zero means today's observation, not a permanent completed flag.
            } else if(project.Kind.StartsWith("bundle:")) {
                var b=Facts.Bundles.FirstOrDefault(x=>"bundle:"+x.Id==project.Kind);
                if(b==null){project.Detail="该目标当前不可读取";continue;}
                if(b.Complete){project.Status="fulfilled";project.Remaining=0;project.Detail="存档确认已完成";continue;}
                // Bundles can permit a subset. Do not reserve every optional ingredient.
                project.Needs=b.Missing.OrderBy(r=>r.Missing).Take(Math.Max(0,b.RequiredSlots-b.CompletedSlots)).ToList();
                project.Remaining=project.Needs.Sum(r=>r.Missing);
                project.Detail=project.Remaining==0?"已经备齐这份安排中的材料，等你去提交":project.Needs.All(n=>n.Item=="(O)-1")?$"还差 {project.Remaining} 金币，先攒起来":$"还差 {project.Remaining} 件材料，最后由你提交";
                foreach(var need in project.Needs)Data.Reservations[need.Item]=Data.Reservations.GetValueOrDefault(need.Item)+need.Count;
            }
        }
        UpdateDevelopmentProjects();UpdateSharedGoals();BuildToday();
        if(Data.Projects.Count>40)Data.Projects=Data.Projects.TakeLast(40).ToList();
        Data.Reservations.Clear();
        foreach(var n in AllReservations())Data.Reservations[n.Item]=Data.Reservations.GetValueOrDefault(n.Item)+n.Count;
        Data.FarmPolicy.Enabled=Data.FarmHelp;
        api?.ConfigureFarm(JsonSerializer.Serialize(new{reservations=AllReservations().Select(n=>new{n.Item,n.Quality,n.Count}),policy=Data.FarmPolicy}));
    }
    private Situation SituationFor(string name,JsonElement actor,JsonElement world) {
        string location=actor.GetProperty("location").GetString()!;
        var npc=FindCharacter(name);
        var candidates=actor.GetProperty("candidates").EnumerateArray().ToArray();
        var arrangement=Data.Projects.LastOrDefault(p=>p.Kind=="farm" && p.Status=="active");
        bool farmPermission=Data.FarmHelp && (arrangement==null || arrangement.Owner=="together" || arrangement.Owner==name);
        return new(){Day=Game1.Date.TotalDays,Minute=Minute,Location=location,PlayerLocation=Game1.currentLocation.NameOrUniqueName,
            NearPlayer=npc?.currentLocation==Game1.currentLocation && Vector2.Distance(npc.Tile,Game1.player.Tile)<9,
            Threat=actor.GetProperty("in_combat").GetBoolean() || (npc?.currentLocation==Game1.currentLocation && world.GetProperty("threats").GetArrayLength()>0),
            Fishing=actor.GetProperty("fishing_available").GetBoolean(),
            CanReachBeach=actor.TryGetProperty("can_reach_beach",out var beachRoute) && beachRoute.GetBoolean(),FarmWater=Facts.DryCrops,FarmHarvest=Facts.RipeCrops,FarmResponsibility=farmPermission,CanReachFarm=farmPermission && actor.TryGetProperty("can_reach_farm",out var route) && route.GetBoolean(),
            Water=farmPermission?candidates.Count(c=>c.GetProperty("skill").GetString()=="water"):0,
            Harvest=farmPermission?candidates.Count(c=>c.GetProperty("skill").GetString()=="harvest"):0,
            Pet=farmPermission?candidates.Count(c=>c.GetProperty("skill").GetString()=="pet"):0,
            Collect=farmPermission?candidates.Count(c=>c.GetProperty("skill").GetString()=="collect"):0,
            Refill=farmPermission?candidates.Count(c=>c.GetProperty("skill").GetString()=="refill"):0,
            Deposit=Data.FarmHelp?candidates.Count(c=>c.GetProperty("skill").GetString()=="deposit"):0,
            Mine=candidates.Count(c=>c.GetProperty("skill").GetString()=="mine"),Pace=Data.Pace,
            FarmProject=Data.Projects.Any(p=>p.Kind=="farm" && p.Status=="active" && (p.Owner==name || p.Owner=="together"))};
    }
    private DateTime rejoinAt=DateTime.MinValue;
    private void TickLife() {
        RefreshFacts();var world=World();
        if(DateTime.UtcNow>=rejoinAt && Minute<12*60) {
            rejoinAt=DateTime.UtcNow.AddSeconds(10);
            foreach(var person in Data.People.Where(p=>p.Value.DailyCompanion==true && !Actor(world,p.Key).HasValue))api?.ResumeDay(person.Key);
            world=World();
        }
        foreach(var pair in Data.People.ToArray()) {
            var actor=Actor(world,pair.Key);if(!actor.HasValue || actor.Value.TryGetProperty("returning_home",out var homeward) && homeward.GetBoolean())continue;
            var s=SituationFor(pair.Key,actor.Value,world);var p=pair.Value;p.DailyCompanion=true;
            p.Life.Tick(s.Day,s.Minute,s.NearPlayer,p.Profile);
            if(p.ProposalAutonomous && p.Proposal!=null && s.Minute>=p.ProposalExpires) {p.Proposal=null;p.ProposalAutonomous=false;}

            SocialTick(pair.Key,p,s);
            if(p.Job?.Status is not ("active" or "waiting" or "paused") && p.Life.Suspended.Count>0) {
                if(p.Energy<40) {StartOption(pair.Key,new(){Id="rest",Title="把力气歇回来",Reason="还有没做完的安排，先照顾好自己",Steps=new(){new(){skill="rest"}}});continue;}
                p.Job=p.Life.Suspended[^1];p.Life.Suspended.RemoveAt(p.Life.Suspended.Count-1);
                p.Job.Status="active";p.Job.Command=null;p.Job.TravelCommand=null;p.Job.WaitingSeconds=0;
                p.Life.Intent=p.Job.Title;p.Life.Reason="继续被打断的安排";
            }
        }
        if(!Settings.Autonomy || Thinking || DateTime.UtcNow<autoAt)return;
        autoAt=DateTime.UtcNow.AddSeconds(Math.Clamp(Settings.AutoIntervalSeconds,15,600));
        var people=Data.People.OrderBy(x=>x.Key).ToArray();if(people.Length==0)return;
        for(int i=0;i<people.Length;i++) {
            var pair=people[(autoCursor+i)%people.Length];var p=pair.Value;
            if(p.Job?.Status is "active" or "waiting" or "paused" || p.Proposal is {decision:"negotiate"} || Minute-p.Life.LastDecisionMinute<20)continue;
            var actor=Actor(world,pair.Key);if(!actor.HasValue || actor.Value.TryGetProperty("returning_home",out var homeward) && homeward.GetBoolean())continue;
            var options=OptionsFor(pair.Key,p,SituationFor(pair.Key,actor.Value,world),actor.Value);if(options.Count==0)continue;
            autoCursor=(autoCursor+i+1)%people.Length;p.Life.LastDecisionMinute=Minute;
            autoAt=DateTime.UtcNow.AddSeconds(Math.Clamp(Settings.AutoIntervalSeconds*(1.5-p.Profile.Temperament.Initiative/100.0),15,600));
            EnsureBudget();
            string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
            if(Data.Calls>=Math.Clamp(Settings.MaxCallsPerDay,1,100) || !File.Exists(file)) {p.Life.DecisionSource="local-budget-or-offline";StartOption(pair.Key,options[0]);return;}
            pendingGoalWork=null;pendingName=pair.Key;pendingGeneration=generation;pendingAutonomous=true;pendingOptions=options;
            var context=new{event_type="autonomous_choice",npc=pair.Key,profile=p.Profile,needs=p.Life.ModelState(),energy=p.Energy,social=PromptSocial(p),today=Data.Today.Take(12),
                shared_goals=GoalContext(),options=options.Take(12),projects=Data.Projects.Where(x=>x.Status=="active").Take(8),recent=MemoryRecall.Select(p.Life.Experiences,string.Join("，",options.Take(3).Select(o=>o.Title)),Game1.Date.TotalDays,4),
                note="从 options 选一个 option_id，用 accept 直接开始自己的安排，steps=[]。只有邀请玩家参与才 negotiate。允许安静地做事。"};
            Data.Calls++;RecordUsage();pendingKnowledge=false;pending=ModelClient.Ask(file,Settings.Model,context,true);return;
        }
    }
    private void StartOption(string name,ActivityOption option,string? speech=null) {
        if(option.Id.StartsWith("goal:") && speech!=null)speech=GoalPlanner.WorkSpeech(speech,"accept",option.Steps[0].location??"");
        var p=Person(name);p.Life.Intent=option.Title;p.Life.Reason=option.Reason;
        var d=new Decision{decision="accept",title=option.Title,speech=speech??option.Reason,steps=option.Steps};
        StartFor(name,d,false,"autonomous",option.Id,option.Reason);
        if(p.Social.CanOpen(Game1.Date.TotalDays) && FindCharacter(name)?.currentLocation==Game1.currentLocation) {
            OpenTopic(name,speech??(option.Id=="fish"?"我想在这里钓会儿鱼。你忙完了可以过来找我。":option.Title+"，你按自己的节奏来。"),"activity:"+p.Job?.Id);
            p.Life.LastSpeechMinute=Minute;
        }
    }
    private bool HandleAutonomousReply(ModelReply result) {
        if(!pendingAutonomous)return false;
        var p=Person(pendingName);
        using var doc=JsonDocument.Parse(result.Json);var root=doc.RootElement;
        string? id=root.TryGetProperty("option_id",out var field)?field.GetString():null;
        var option=pendingOptions.FirstOrDefault(o=>o.Id==id);
        if(option==null)throw new InvalidOperationException("模型没有选择一个可执行活动。");
        string? speech=root.TryGetProperty("speech",out var say)?say.GetString():null;
        if(speech?.Length>400)throw new InvalidOperationException("自主发言过长。");
        // Candidate is revalidated against the fresh map before starting, not only its ID.
        var world=World();var actor=Actor(world,pendingName);
        if(!actor.HasValue)return true;
        var fresh=OptionsFor(pendingName,p,SituationFor(pendingName,actor.Value,world),actor.Value).FirstOrDefault(o=>o.Id==id);
        if(fresh==null || p.Job?.Status is "active" or "waiting" or "paused")return true;
        string mode=root.TryGetProperty("decision",out var modeField)?modeField.GetString()??"":"";
        p.Life.DecisionSource="model";p.Life.LastDecisionError="";
        if(mode=="negotiate") {
            if(p.Life.LastInvitationDay==Game1.Date.TotalDays || !p.Social.CanOpen(Game1.Date.TotalDays)){StartOption(pendingName,fresh);return true;}
            p.Life.LastInvitationDay=Game1.Date.TotalDays;p.ProposalAutonomous=true;p.ProposalExpires=Minute+30;p.Proposal=new(){decision="negotiate",title=fresh.Title,speech=speech??fresh.Title,steps=fresh.Steps};
            OpenTopic(pendingName,p.Proposal.speech,"invitation:"+id+":"+Game1.Date.TotalDays);
        } else if(mode=="accept")StartOption(pendingName,fresh,speech);
        else if(mode=="chat") {if(!string.IsNullOrWhiteSpace(speech))OpenTopic(pendingName,speech,"model:"+Guid.NewGuid().ToString("N"));p.Life.LastSpeechMinute=Minute;}
        else throw new InvalidOperationException("自主决策类型无效。");
        return true;
    }
    private void StartFor(string name,Decision d,bool forced,string origin="player",string optionId="",string reason="") {
        var p=Person(name);var existing=p.Job;
        if(existing?.Status is "active" or "waiting" or "paused") {
            if(origin=="autonomous" || (existing.Origin=="player" && !existing.FollowMode && !forced)){Notice="已有一个约定；可以先停止它，或明确强制插入新任务。";return;}
            if(existing.TravelCommand!=null){api!.CancelAction(existing.TravelCommand);existing.TravelCommand=null;}
            if(existing.Command!=null) {
                using var receipt=JsonDocument.Parse(api!.CancelAction(existing.Command));
                if(receipt.RootElement.GetProperty("status").GetString()=="succeeded")ApplyResult(name,p,receipt.RootElement);
                else RecordInterrupted(p,existing,receipt.RootElement);
                existing.Command=null;
            }
            if(existing.FollowMode)existing.Status="cancelled";
            if(existing.Status is not ("fulfilled" or "cancelled")) {existing.Status="paused";if(p.Life.Suspended.Count<3)p.Life.Suspended.Add(existing);else{Notice="已有三个待继续的安排，请先处理它们。";return;}}
        }
        if(!Actor(World(),name).HasValue){Notice="先邀请同行，再开始约定。";return;}
        p.Job=new(){Title=d.title,Steps=d.steps.Select(s=>new Step{skill=s.skill,count=s.count,location=s.location,target_item=s.target_item}).ToList(),Forced=forced,Origin=origin,OptionId=optionId,Reason=reason};
        p.Proposal=null;p.ProposalAutonomous=false;p.Life.Intent=d.title;p.Life.Reason=reason;
        if(forced){p.Bond=Math.Max(0,p.Bond-1);p.Social.Relationship.Apply(p.Job.Id,"forced",Game1.Date.TotalDays);} // once per forced commitment, never once per crop
        AddLine(p.Memories,origin=="autonomous"?"自己的安排":"约定",d.title+"："+d.PlanText());
        Notice="安排开始；关闭面板后会行动。";
    }
    private void CheckpointJobs() {
        generation++;pending=null;pendingGoalWork=null;
        foreach(var pair in Data.People) {
            var p=pair.Value;var job=p.Job;
            if(job==null || job.Status is not ("active" or "waiting")){p.Social.CloseDay(Game1.Date.TotalDays,p.Life.Experiences,Data.Projects);continue;}
            try {
                if(job.TravelCommand!=null){api?.CancelAction(job.TravelCommand);job.TravelCommand=null;}
                if(job.Command!=null && api!=null) {
                    using var result=JsonDocument.Parse(api.CancelAction(job.Command));
                    if(result.RootElement.GetProperty("status").GetString()=="succeeded")ApplyResult(pair.Key,p,result.RootElement);
                    else RecordInterrupted(p,job,result.RootElement);
                    job.Command=null;
                }
            } catch {job.Command=null;job.TravelCommand=null;}
            if(job.Status!="fulfilled"){job.Status="paused";job.Detail="过夜前暂停，醒来重新核对目标";}
            p.Social.CloseDay(Game1.Date.TotalDays,p.Life.Experiences,Data.Projects);
        }
    }
    public string LifeSummary()=> $"现在想：{Current.Life.Intent}\n因为：{Current.Life.Reason}\n自己的心愿：{Current.Life.Wish}（{Current.Life.WishProgress}/{Current.Life.WishTarget}）\n兴趣需求 {(int)Current.Life.Interest} · 想找你 {(int)Current.Life.Company} · 想换换花样 {(int)Current.Life.Variety}\n被打断的安排 {Current.Life.Suspended.Count} 个\n随身物资："+string.Join(" / ",Facts.Stock.Where(s=>s.Location=="npc_pouch:"+Selected).Select(s=>s.Name+" ×"+s.Count));
}
