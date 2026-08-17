using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Together;

// LevelUpMenu processes profession clicks inside update, not receiveLeftClick.
// Scope the input to that native handler and restore SMAPI's input in finally.
// No OS cursor movement, profession mutation or global input interception.
internal sealed class NativeMenuInput : InputState {
    private readonly MouseState mouse;
    private NativeMenuInput(int x,int y,ButtonState button) {
        float scale=Game1.uiMode?Game1.options.uiScale:Game1.options.zoomLevel;
        mouse=new MouseState((int)Math.Ceiling(x*scale),(int)Math.Ceiling(y*scale),0,button,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
    }
    public override MouseState GetMouseState()=>mouse;
    public override KeyboardState GetKeyboardState()=>default;
    public override GamePadState GetGamePadState()=>default;
    public static void ChooseProfession(LevelUpMenu menu,Rectangle bounds) {
        if(!menu.isActive||!menu.isProfessionChooser||!menu.CanReceiveInput()||!menu.readyToClose())throw new InvalidOperationException("profession_menu_not_ready");
        var previous=Game1.input;var before=Game1.player.professions.ToHashSet();
        int x=bounds.Center.X,y=Math.Clamp(bounds.Center.Y,menu.yPositionOnScreen+193,menu.yPositionOnScreen+menu.height-1);
        try {
            var time=new GameTime(TimeSpan.Zero,TimeSpan.Zero);
            Game1.input=new NativeMenuInput(x,y,ButtonState.Released);menu.update(time);
            Game1.input=new NativeMenuInput(x,y,ButtonState.Pressed);menu.update(time);
        }finally {Game1.input=previous;}
        if(!Game1.player.professions.Any(p=>!before.Contains(p))||menu.isProfessionChooser)throw new InvalidOperationException("native_profession_choice_not_verified");
    }
}
