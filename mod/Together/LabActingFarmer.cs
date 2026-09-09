using System.Reflection;
using StardewValley;

namespace Together;
// Stage-0 experiment only. Callers must enforce the AgentLab triple gate.
internal sealed class ActingFarmer:IDisposable {
    private static readonly FieldInfo PlayerField=typeof(Game1).GetField("_player",BindingFlags.Static|BindingFlags.NonPublic)
        ??throw new InvalidOperationException("native_player_field_changed");
    private static bool active;
    private readonly Farmer previous;
    private readonly int thread=Environment.CurrentManagedThreadId;
    private readonly int tick=Game1.ticks;
    private bool disposed;
    private ActingFarmer(Farmer actor) {
        if(PlayerField.IsInitOnly||PlayerField.FieldType!=typeof(Farmer)||!PlayerField.IsStatic)throw new InvalidOperationException("native_player_field_changed");
        if(active||Game1.activeClickableMenu!=null||Game1.eventUp)throw new InvalidOperationException("acting_farmer_requires_no_menu_or_event_or_nested_scope");
        previous=Game1.player;active=true;
        // The property setter unloads the old Farmer. Do not invoke that setter.
        try {PlayerField.SetValue(null,actor);if(!ReferenceEquals(Game1.player,actor))throw new InvalidOperationException("acting_farmer_not_installed");}
        catch {PlayerField.SetValue(null,previous);active=false;throw;}
    }
    public static ActingFarmer As(Farmer actor)=>new(actor);
    public void Dispose() {
        if(disposed)return;disposed=true;
        try {PlayerField.SetValue(null,previous);}
        finally {active=false;}
        if(!ReferenceEquals(Game1.player,previous)||Game1.ticks!=tick||Environment.CurrentManagedThreadId!=thread) {
            Game1.paused=true;
            throw new InvalidOperationException("FATAL_acting_farmer_restore_or_tick_assertion");
        }
    }
}
