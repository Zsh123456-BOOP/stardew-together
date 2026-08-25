namespace Together;

// Only tools which act on locally observed tiles/targets carry a map precondition.
// Travel, semantic work and native menus validate their own destinations/state.
public static class ToolLocationContract {
    public static bool RequiresObservedLocation(string tool)=>tool is "player.move" or "player.use_tool" or "player.interact" or "player.place" or "player.work" or "player.fish" or "player.combat" or "player.mine_descend";
    public static string Bind(string tool,string observed,string? previousTool,string previousLocation,string previousDestination,string explicitLocation="") {
        if(!RequiresObservedLocation(tool))return "";
        if(explicitLocation.Length>0)return explicitLocation;
        if(previousTool==null)return observed;
        if(previousTool is "player.travel" or "player.service"&&previousDestination.Length>0)return previousDestination;
        if(previousLocation.Length>0)return previousLocation;
        throw new InvalidOperationException("local_action_after_unbound_task_requires_explicit_location");
    }
}
