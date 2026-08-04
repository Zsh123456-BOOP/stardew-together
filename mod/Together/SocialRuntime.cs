using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewModdingAPI;

namespace Together;
public sealed partial class ModEntry {
    private void SocialTick(string name,Companion p,Situation s) {
        var social=p.Social;
        if(social.Mode=="holiday" && social.HolidayDay!=s.Day)social.Mode="normal";
        social.PlaySeconds+=1;
        if(social.Challenge is {Status:"active"} challenge) {
            challenge.PlayerDone=Facts.Progress.GetValueOrDefault("fish_species")>challenge.StartingFishSpecies;
            if(challenge.PlayerDone && challenge.CompanionCatch!="")challenge.Status="fulfilled";
            else if(s.Day>challenge.Deadline)challenge.Status="expired";
        }
        if(p.Job?.Status=="active") {
            p.Job.ObservedSeconds+=1;
            // Presence is only co-presence, not proof the player helped with a particular harvest.
            if(s.NearPlayer)p.Job.SharedSeconds+=1;
        }
        if(social.Wishes.Count==0 || social.Wishes.All(w=>w.Status!="active") && social.Wishes.Max(w=>w.CreatedDay)<s.Day) {
            string skill=p.Profile.Likes.Contains("钓鱼")?"fish":p.Profile.Likes.Contains("挖矿") || p.Profile.Likes.Contains("冒险")?"mine":"harvest";
            social.Wishes.Add(new(){Skill=skill,Title=skill=="fish"?"这周钓到三种不同的东西":skill=="mine"?"带回三次真正挖到的收获":"亲手收好三次成熟作物",CreatedDay=s.Day});
            social.Wishes=social.Wishes.TakeLast(8).ToList();
        }
        if(!Settings.Autonomy || !s.NearPlayer || Thinking || !social.CanOpen(s.Day))return;
        var completedProject=Data.Projects.FirstOrDefault(project=>project.Status=="fulfilled" && !social.CelebratedProjects.Contains(project.Id));
        if(completedProject!=null) {
            social.CelebratedProjects.Add(completedProject.Id);social.TheirTurn=true;
            OpenTopic(name,"“"+completedProject.Title+"”这件事，存档里真的完成了。折腾了这么些天，今天想把一小段时间留给我们自己。","project:"+completedProject.Id);return;
        }
        if(social.Challenge is {Status:"fulfilled",Celebrated:false} done) {
            done.Celebrated=true;
            OpenTopic(name,"我们的收获清单打勾啦。你钓到了新图鉴，我也真的带回了东西。各自的过程不一样，但都值得记一笔。",done.Id);return;
        }
        var oldPlace=p.Life.Experiences.LastOrDefault(e=>e.Location==s.Location && e.Day<s.Day-1 && e.PlayerParticipated && !social.Topics.Any(t=>t.EventId==e.Id));
        if(oldPlace!=null && s.Minute<12*60 && p.Life.LastInvitationDay!=s.Day) {
            p.Life.LastInvitationDay=s.Day;OpenTopic(name,"又走到这里了。想起那回我"+oldPlace.Summary+"，你也在旁边。",oldPlace.Id);return;
        }
        var habit=social.Habits.FirstOrDefault(h=>h.Enabled && h.Confirmed && h.LastInvitedDay!=s.Day && h.Location==s.Location);
        if(habit!=null && p.Job?.Status is not ("active" or "waiting")) {
            habit.LastInvitedDay=s.Day;
            string speech="又到这里啦。今天要不要留一小会儿，一起"+Decision.Labels[habit.Skill]+"？";
            p.Proposal=new(){decision="negotiate",title=habit.Title,speech=speech,steps=new(){new(){skill=habit.Skill,location=habit.Location}}};
            p.ProposalAutonomous=true;p.ProposalExpires=s.Minute+30;OpenTopic(name,speech,"habit:"+habit.Skill+":"+s.Day);return;
        }
        var experience=p.Life.Experiences.LastOrDefault(e=>!e.Shared && e.Day>=s.Day-3);
        if(experience!=null) {
            string when=experience.Day==s.Day?"刚才":experience.Day==s.Day-1?"昨天":"前几天";
            string speech=when+"我"+experience.Summary+"。"+(experience.PlayerParticipated?"你当时也在旁边，我记得。":"回头碰到你，就想跟你说一声。");
            experience.Shared=true;OpenTopic(name,speech,experience.Id);return;
        }
        var topic=social.Topics.LastOrDefault(t=>!t.Answered && t.Day<s.Day && t.ExpiresDay>=s.Day);
        if(topic!=null) {
            // Carry an unanswered topic in model context; don't repeatedly nag the player to answer.
            return;
        }
        if(s.Minute>=18*60 && p.Life.HabitDay!=s.Day) {
            p.Life.HabitDay=s.Day;
            string speech=social.Mode=="holiday"?"今天没急着赶进度，倒是挺喜欢这样慢慢晃。":Facts.DryCrops+Facts.RipeCrops==0?"农田这边收拾好了。我想把剩下的时间留给自己，也留一点给你。":"还有些活，明天也可以接着想。你今天玩得开心吗？";
            OpenTopic(name,speech,"evening:"+s.Day);
        }
    }
    private void OpenTopic(string name,string speech,string source) {
        var p=Person(name);if(!p.Social.CanOpen(Game1.Date.TotalDays))return;
        p.Social.Open(Game1.Date.TotalDays,speech,source);p.Life.LastSpeechMinute=Minute;Say(name,speech);
    }
    private void RememberResult(string name,Companion p,Job job,string skill,JsonElement result) {
        int day=Game1.Date.TotalDays;var social=p.Social;
        if(skill=="gift")social.GiftDay=day;
        if(job.Origin=="player" && !job.Forced)social.TheirTurn=true;
        else if(job.Origin=="autonomous" && job.OptionId is "fish" or "mine" or "beach_trip")social.TheirTurn=false;
        var experience=p.Life.Experiences.LastOrDefault();
        bool near=job.SharedSeconds>=5 && job.SharedSeconds>=job.ObservedSeconds*.5;
        string location=FindCharacter(name)?.currentLocation.NameOrUniqueName??"";
        if(experience!=null){experience.Location=location;experience.PlayerParticipated=near;experience.Skill=skill;}
        if(job.Origin=="player" && !job.Forced)social.Relationship.Apply(job.Id,"promise_kept",day);
        if(near)social.ObserveShared(job.Id,skill,location,day,!job.Forced && job.Origin=="player");
        foreach(var wish in social.Wishes.Where(w=>w.Status=="active" && w.Skill==skill)) {
            if(result.TryGetProperty("evidence",out var e) && e.TryGetProperty("caught_items",out var items) && items.GetArrayLength()>0)
                foreach(var item in items.EnumerateArray()){string id=item.GetString()!;if(!wish.Evidence.Contains(id))wish.Evidence.Add(id);}
            else if(skill!="fish" && !wish.Evidence.Contains(job.Id))wish.Evidence.Add(job.Id);
            if(wish.Evidence.Count>=wish.Target)wish.Status="fulfilled";
        }
        if(skill=="fish" && social.Challenge is {Status:"active"} challenge && result.TryGetProperty("evidence",out var proof)
            && proof.TryGetProperty("caught_items",out var caught) && caught.GetArrayLength()>0)challenge.CompanionCatch=caught[0].GetString()??"";
        var activeWish=social.Wishes.LastOrDefault();
        if(activeWish!=null){p.Life.Wish=activeWish.Title;p.Life.WishProgress=activeWish.Evidence.Count;p.Life.WishTarget=activeWish.Target;}
    }
    public void SetSocialMode(string mode) {
        if(mode is not ("normal" or "quiet" or "holiday"))return;
        Current.Social.Mode=mode;if(mode=="holiday")Current.Social.HolidayDay=Game1.Date.TotalDays;Notice=mode=="quiet"?"可以安静地陪着。普通主动聊天已暂停，不影响关系。":mode=="holiday"?"今天少安排劳动，把时间留给喜欢的事。":"恢复平时的相处方式。";Persist();
    }
    public void ConfirmHabit(int index) {
        if(index<0 || index>=Current.Social.Habits.Count)return;
        var h=Current.Social.Habits[index];if(h.Days.Count<3){Notice="先自愿一起度过三天这样的时光，再决定是不是我们的小习惯。";return;}if(!h.Confirmed){h.Confirmed=true;h.Enabled=true;}else h.Enabled=!h.Enabled;
        Notice=h.Enabled?"记住这个小习惯了。到时候会偶尔邀请，不强求。":"这个习惯先放下，不会影响关系。";Persist();
    }
    public void ClearMemories() {
        generation++;pending=null;Current.Social.Forget();Current.Chat.Clear();Current.Memories.Clear();Current.Life.Experiences.Clear();Current.LastDecision=null;Current.Proposal=null;Current.ProposalAutonomous=false;bubbles.Remove(Selected);
        Notice="已清除这位伙伴的聊天、经历、话题、偏好、习惯和日记；关系与正在执行的工作保留。";Persist();
    }
    public void ExportMemories() {
        var directory=Path.Combine(Helper.DirectoryPath,"memories");Directory.CreateDirectory(directory);
        string name=string.Concat(Selected.Where(char.IsLetterOrDigit));
        File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(new{save_id=Game1.uniqueIDForThisGame.ToString(),day=Game1.Date.TotalDays,npc=Selected,profile=Current.Profile,social=Current.Social,experiences=Current.Life.Experiences},jsonOptions));
        Notice="这位伙伴的经历已导出到 Mod 的 memories 文件夹。";
    }
    private bool HandleLocalConversation(string message) {
        if(message.StartsWith("日记：") || message.StartsWith("日记:")) {
            Current.Social.PlayerNotes[Game1.Date.TotalDays]=message[3..].Trim();
            var entry=Current.Social.Diary.FirstOrDefault(d=>d.Day==Game1.Date.TotalDays);if(entry!=null)entry.PlayerNote=message[3..].Trim();
            Notice="你写的话已记在今天日记的玩家留言里。";Persist();return true;
        }
        if(message.StartsWith("记住：") || message.StartsWith("记住:")) {
            string preference=message[3..].Trim();if(preference.Length==0)return true;
            Current.Social.Preferences.Add(preference);Current.Social.Preferences=Current.Social.Preferences.TakeLast(24).ToList();
            AddLine(Current.Chat,"你",message);Say(Selected,"好，我记住你刚才说的了。以后想改，直接告诉我。");Persist();return true;
        }
        if(message.StartsWith("忘记：") || message.StartsWith("忘记:")) {
            string value=message[3..].Trim();if(value.Length==0)return true;Current.Social.Preferences.RemoveAll(p=>p.Contains(value));
            Notice="已删除匹配的偏好记录。";Persist();return true;
        }
        return false;
    }
    public void StartChallenge() {
        RefreshFacts(true);Current.Social.Challenge=new(){Day=Facts.Day,Deadline=Facts.Day+2,StartingFishSpecies=Facts.Progress.GetValueOrDefault("fish_species")};
        Notice="三天小挑战：你钓一种新图鉴，伙伴带回一份真实渔获。各自分享，不比较效率。";Persist();
    }
    public void ChangeTrait(string name) {
        var p=Current.Profile.Temperament;
        int Next(int v)=>(Math.Clamp(v,0,100)+20)%120;
        switch(name){case "主动":p.Initiative=Next(p.Initiative);break;case "社交":p.Sociability=Next(p.Sociability);break;case "风险":p.RiskTolerance=Next(p.RiskTolerance);break;case "耐心":p.Patience=Next(p.Patience);break;case "计划":p.Planning=Next(p.Planning);break;}
        Persist();
    }
}
