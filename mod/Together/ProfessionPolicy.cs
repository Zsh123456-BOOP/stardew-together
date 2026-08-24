using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    internal object SetProfessionPolicy(JsonElement args) {
        int skill=AgentToolRegistry.Number(args,"skill",-1),level=AgentToolRegistry.Number(args,"level",-1),profession=AgentToolRegistry.Number(args,"profession",-1);
        if(skill is <0 or >4||level is not (5 or 10)||profession/6!=skill||profession<0||level==5&&profession%6>1||level==10&&profession%6<2)throw new InvalidOperationException("invalid_profession_policy");
        var choices=Data.Autoplay.ProfessionChoices;
        int? plannedBase=choices.TryGetValue(skill+":5",out int first)?first:Game1.player.professions.Where(p=>p/6==skill&&p%6<2).Select(p=>(int?)p).FirstOrDefault();
        if(level==10&&plannedBase.HasValue&&(profession%6<=3?0:1)!=plannedBase.Value%6)throw new InvalidOperationException("profession_conflicts_with_level5_branch");
        if(level==5&&choices.TryGetValue(skill+":10",out int last)&&(last%6<=3?0:1)!=profession%6)throw new InvalidOperationException("profession_conflicts_with_level10_branch");
        choices[skill+":"+level]=profession;Persist();return new{status="profession_policy_saved",skill,level,profession,note="只保存未来选择；不会修改已获得职业，执行时必须出现在真实菜单选项内。"};
    }
    internal void RememberProfession(int skill,int level,int profession) {
        Data.Autoplay.ProfessionChoices[skill+":"+level]=profession;
        Data.Autoplay.Record("profession_choice",AgentJson.Encode(new{skill,level,profession}));
    }
    private bool ApplyProfessionPolicy(LevelUpMenu menu) {
        if(!AutoplayRunning||!menu.isActive||!menu.isProfessionChooser||!menu.CanReceiveInput()||!menu.readyToClose())return false;
        var state=NativeMenuInput.ProfessionState(menu);
        if(!Data.Autoplay.ProfessionChoices.TryGetValue(state.Skill+":"+state.Level,out int expected))return false;
        int choice=state.Choices.IndexOf(expected);if(choice is not (0 or 1))return false;
        NativeMenuInput.ChooseProfession(menu,(choice==0?menu.leftProfession:menu.rightProfession).bounds);
        RememberProfession(state.Skill,state.Level,expected);return true;
    }
}
