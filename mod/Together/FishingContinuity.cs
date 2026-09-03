namespace Together;
// Elapsed active game-update seconds, not wall time while the app is suspended.
public static class FishingContinuity {
    public static double PhaseLimit(string phase)=>phase switch {
        "fishing_minigame"=>180,"fishing_wait_bite"=>90,"fishing_reward"=>30,
        "fishing_reel_without_menu"=>5,"fishing_release"=>5,_=>15
    };
    public static string? Deadline(int startDay,int day,int until,int time)=>day!=startDay?"day_changed_replan":time>=until?"time_reserve_reached":null;
}
