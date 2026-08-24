using System.Reflection;
using System.Text.Json;
using StardewValley;
using StardewValley.Events;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    private IClickableMenu? familySubmittedMenu;
    internal object ReadFamily() {
        var p=Game1.player;var home=Utility.getHomeOfFarmer(p);var friendship=p.spouse==null?null:p.GetSpouseFriendship();
        return new{spouse=p.spouse,married=p.isMarriedOrRoommates(),children=p.getChildren().Select(c=>new{name=c.Name,age=c.Age}).ToArray(),
            next_birthing_day=friendship?.NextBirthingDate?.TotalDays,house_level=home.upgradeLevel,crib_style=home.cribStyle.Value,policy=Data.Autoplay.Family,
            note="出生只在原生条件和随机事件满足后发生；同意策略不会生成事件或缩短孕期。婚恋对象由独立关系策略选择。"};
    }
    internal object SetFamilyPolicy(JsonElement args) {
        var old=Data.Autoplay.Family;var next=new FamilyPolicy{Partner=old.Partner,AcceptChildren=old.AcceptChildren,TargetChildren=old.TargetChildren,ChildNames=old.ChildNames.ToList(),AutoNameAnimals=old.AutoNameAnimals};
        if(args.TryGetProperty("partner",out var partner)) {
            if(partner.ValueKind!=JsonValueKind.String)throw new InvalidOperationException("partner_requires_native_character_name");next.Partner=partner.GetString()!;
            if(next.Partner.Length>0&&(!Game1.characterData.TryGetValue(next.Partner,out var character)||!character.CanBeRomanced))throw new InvalidOperationException("partner_must_be_native_romance_candidate");
            if(next.Partner.Length>0&&(Game1.player.isMarriedOrRoommates()||Game1.player.isEngaged())&&Game1.player.spouse!=next.Partner)throw new InvalidOperationException("partner_conflicts_with_existing_marriage_or_engagement");
        }
        if(args.TryGetProperty("accept_children",out var accept)){if(accept.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("accept_children_requires_boolean");next.AcceptChildren=accept.GetBoolean();}
        if(args.TryGetProperty("target_children",out var target)){if(!target.TryGetInt32(out int n)||n is <0 or >2)throw new InvalidOperationException("invalid_target_children");next.TargetChildren=n;}
        if(args.TryGetProperty("auto_name_animals",out var auto)){if(auto.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("auto_name_animals_requires_boolean");next.AutoNameAnimals=auto.GetBoolean();}
        if(args.TryGetProperty("child_names",out var names)) {
            if(names.ValueKind!=JsonValueKind.Array||names.GetArrayLength()>2||names.EnumerateArray().Any(n=>n.ValueKind!=JsonValueKind.String))throw new InvalidOperationException("invalid_child_names");
            next.ChildNames=names.EnumerateArray().Select(n=>NativeMenuTools.ValidateName(n.GetString()!)).ToList();
        }
        if(next.ChildNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=next.ChildNames.Count)throw new InvalidOperationException("child_names_must_differ");
        if(next.AcceptChildren==true&&(next.TargetChildren==0||next.ChildNames.Count<next.TargetChildren))throw new InvalidOperationException("provide_names_for_target_children_before_accepting");
        Data.Autoplay.Family=next;Persist();return ReadFamily();
    }
    private bool ApplyFamilyNightPolicy() {
        if(!AutoplayRunning)return false;
        var menu=Game1.activeClickableMenu;if(menu==null)return false;if(ReferenceEquals(menu,familySubmittedMenu))return true;
        var policy=Data.Autoplay.Family;int question=Game1.farmEvent is QuestionEvent qe?(int?)typeof(QuestionEvent).GetField("whichQuestion",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(qe)??-1:-1;
        if(question==1&&menu is DialogueBox {isQuestion:true} dialogue&&policy.AcceptChildren.HasValue) {
            bool yes=policy.AcceptChildren.Value&&Game1.player.getChildrenCount()<policy.TargetChildren;
            string key=yes?"Yes":"Not";int i=Array.FindIndex(dialogue.responses,r=>r.responseKey==key);
            if(i<0||dialogue.responseCC==null||i>=dialogue.responseCC.Count)return false;
            dialogue.finishTyping();NativeMenuInput.ClickMenu(dialogue,dialogue.responseCC[i].bounds);familySubmittedMenu=menu;
            Data.Autoplay.Record("family_night_input",AgentJson.Encode(new{kind="pregnancy_response",answer=key,evidence="native_question_callback",next_birthing_day=Game1.player.GetSpouseFriendship()?.NextBirthingDate?.TotalDays}));return true;
        }
        if(menu is not NamingMenu naming)return false;
        string name,kind;
        if(Game1.farmEvent is BirthingEvent) {
            int index=Game1.player.getChildrenCount();if(index>=policy.ChildNames.Count)return false;
            name=policy.ChildNames[index];kind="child_name";
        }else if(question==2&&policy.AutoNameAnimals) {
            // Keep the native generated name unless already used. Do not draw extra game RNG.
            var used=new HashSet<string>(Game1.getFarm().animals.Values.Select(a=>a.Name).Concat(Game1.getFarm().buildings.Select(b=>b.GetIndoors()).OfType<AnimalHouse>().SelectMany(h=>h.animals.Values.Select(a=>a.Name))),StringComparer.OrdinalIgnoreCase);
            string seed=naming.textBox.Text.Trim();if(seed.Length==0||seed.Any(char.IsControl)||seed.Contains('[')||seed.Contains(']'))seed="Momo";
            if(seed.Length>18)seed=seed[..18];name=seed;int suffix=2;while(used.Contains(name))name=seed+suffix++;kind="animal_name";
        }else return false;
        naming.textBox.Text=NativeMenuTools.ValidateName(name,naming.minLength);NativeMenuInput.ClickMenu(naming,naming.doneNamingButton.bounds);familySubmittedMenu=menu;
        Data.Autoplay.Record("family_night_input",AgentJson.Encode(new{kind,name,status="input_sent",note="实际出生和成员数量由原生事件完成后重新读取"}));return true;
    }
}
