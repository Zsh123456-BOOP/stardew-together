namespace Together;

// Bound deliberation without hiding failures or using paid polling as a timer.
public sealed class AgentDecisionPacing {
    public int QueriesWithoutProgress {get;private set;}
    private int observedProgress=-1;
    public int Observe(int verifiedActions,bool scheduledAction,bool reviewError) {
        if(verifiedActions!=observedProgress){QueriesWithoutProgress=0;observedProgress=verifiedActions;}
        else QueriesWithoutProgress++;
        return QueriesWithoutProgress<3?1:QueriesWithoutProgress<6?8:20;
    }
    public static bool CanDefer(bool workCovered,bool needsMenuChoice)=>workCovered&&!needsMenuChoice;
    public void Reset(){QueriesWithoutProgress=0;observedProgress=-1;}
    public static bool RoutineWake(string reason)=>reason.StartsWith("actor_ready:")||reason.StartsWith("idle_actors:")||reason=="player_needs_next_plan"||reason=="farm_investment_complete"||reason=="environment_changed";
}
