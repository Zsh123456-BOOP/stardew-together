using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace Together;
// Only replace input during the original fishing update. Never change fish/bar
// position, catch progress, RNG, stamina, time, or the native reward callbacks.
internal sealed class FishingInput : InputState {
    private readonly bool pressed;
    private FishingInput(bool pressed){this.pressed=pressed;}
    public override MouseState GetMouseState()=>Mouse(pressed);
    public override KeyboardState GetKeyboardState()=>default;
    public override GamePadState GetGamePadState()=>default;
    private static MouseState Mouse(bool held)=>new(0,0,0,held?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
    private static PlayerExecutor? owner;
    public static void Install(string id,PlayerExecutor executor) {
        owner=executor;var harmony=new Harmony(id+".native-fishing-input");
        harmony.Patch(AccessTools.Method(typeof(BobberBar),nameof(BobberBar.update)),prefix:new HarmonyMethod(typeof(FishingInput),nameof(BarPrefix)),finalizer:new HarmonyMethod(typeof(FishingInput),nameof(Restore)));
        harmony.Patch(AccessTools.Method(typeof(FishingRod),nameof(FishingRod.tickUpdate)),prefix:new HarmonyMethod(typeof(FishingInput),nameof(RodPrefix)),finalizer:new HarmonyMethod(typeof(FishingInput),nameof(Restore)));
    }
    private sealed record SavedInput(InputState Input,MouseState Mouse,KeyboardState Keyboard,GamePadState Pad);
    private static SavedInput Apply(bool pressed) {
        var saved=new SavedInput(Game1.input,Game1.oldMouseState,Game1.oldKBState,Game1.oldPadState);
        Game1.input=new FishingInput(pressed);Game1.oldMouseState=Mouse(pressed);Game1.oldKBState=default;Game1.oldPadState=default;return saved;
    }
    private static void BarPrefix(BobberBar __instance,out SavedInput? __state) {
        __state=null;if(owner?.OwnsFishing!=true||Game1.activeClickableMenu!=__instance)return;
        float center=__instance.bobberBarPos+__instance.bobberBarHeight/2f;
        float fish=__instance.bobberPosition+16;
        // Predict bar momentum rather than toggling only on position, reducing
        // overshoot. This is normal button feedback, with no guaranteed catch.
        bool press=center+__instance.bobberBarSpeed*9>fish+__instance.bobberSpeed*3;
        __state=Apply(press);
    }
    private static void RodPrefix(FishingRod __instance,Farmer who,out SavedInput? __state) {
        __state=null;if(owner?.OwnsFishing!=true||who!=Game1.player||who.CurrentTool!=__instance)return;
        __state=Apply(__instance.isTimingCast&&__instance.castingPower<owner.FishingCastPower);
    }
    private static void Restore(SavedInput? __state) {
        if(__state==null)return;
        Game1.input=__state.Input;Game1.oldMouseState=__state.Mouse;Game1.oldKBState=__state.Keyboard;Game1.oldPadState=__state.Pad;
    }
}
