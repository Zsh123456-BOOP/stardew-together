namespace Together;
public static class CropGrowth {
    // Mirrors the phase reduction policy of HoeDirt.applySpeedIncreases in 1.6.15.
    // The first stage retains a day; reduction makes at most three passes. Do not
    // replace this with round(sum * speed), which differs on short phases.
    public static int[] Stages(IReadOnlyList<int> original,float fertilizer,bool agriculturist,bool paddy) {
        var stages=original.ToArray();float boost=fertilizer+(agriculturist?.1f:0)+(paddy?.25f:0);
        int remaining=(int)Math.Ceiling(stages.Sum()*boost);
        for(int pass=0;pass<3&&remaining>0;pass++)for(int i=0;i<stages.Length&&remaining>0;i++) {
            if(stages[i]>(i==0?1:0)){stages[i]--;remaining--;}
        }
        return stages;
    }
    public static int SeasonEnd(int currentSeason,int day,IReadOnlySet<int> growsIn,bool ignoresSeasons,int maxDays=56) {
        if(ignoresSeasons)return day+maxDays;
        int end=28;
        for(int advance=1;advance<4&&end-day<maxDays;advance++) {
            if(!growsIn.Contains((currentSeason+advance)%4))break;
            end+=28;
        }
        return Math.Min(end,day+maxDays);
    }
}
