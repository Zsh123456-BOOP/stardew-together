using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Together;

// LevelUpMenu processes profession clicks inside update, not receiveLeftClick.
// Scope the input to that native handler and restore SMAPI's input in finally.
// No OS cursor movement, profession mutation or global input interception.
internal sealed class NativeMenuInput : InputState {
    internal static (int Skill,int Level,List<int> Choices) ProfessionState(LevelUpMenu menu) {
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        int Read(string name)=>(int)(typeof(LevelUpMenu).GetField(name,flags)?.GetValue(menu)??throw new InvalidOperationException("native_profession_schema_changed"));
        var choices=typeof(LevelUpMenu).GetField("professionsToChoose",flags)?.GetValue(menu) as List<int>??new();
        return(Read("currentSkill"),Read("currentLevel"),choices.ToList());
    }
    private readonly MouseState mouse;
    private NativeMenuInput(int x,int y,ButtonState button,ButtonState right=ButtonState.Released) {
        float scale=Game1.uiMode?Game1.options.uiScale:Game1.options.zoomLevel;
        mouse=new MouseState((int)Math.Ceiling(x*scale),(int)Math.Ceiling(y*scale),0,button,ButtonState.Released,right,ButtonState.Released,ButtonState.Released);
    }
    public override MouseState GetMouseState()=>mouse;
    public override KeyboardState GetKeyboardState()=>default;
    public override GamePadState GetGamePadState()=>default;
    public static bool InteractWorld(Point tile) {
        var input=Game1.input;var previous=Game1.oldMouseState;
        try {
            int x=tile.X*64+32-Game1.viewport.X,y=tile.Y*64+32-Game1.viewport.Y;
            Game1.oldMouseState=new MouseState(x,y,0,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
            Game1.input=new NativeMenuInput(x,y,ButtonState.Released,ButtonState.Pressed);
            return Game1.tryToCheckAt(tile.ToVector2(),Game1.player);
        }finally{Game1.input=input;Game1.oldMouseState=previous;}
    }
    public static void PressAction(Point tile) {
        var input=Game1.input;var mouse=Game1.oldMouseState;var keys=Game1.oldKBState;var pad=Game1.oldPadState;
        try {
            int x=tile.X*64+32-Game1.viewport.X,y=tile.Y*64+32-Game1.viewport.Y;
            var scoped=new NativeMenuInput(x,y,ButtonState.Released,ButtonState.Pressed);Game1.input=scoped;
            Game1.oldMouseState=new MouseState();Game1.oldKBState=default;Game1.oldPadState=default;
            Game1.pressActionButton(default,scoped.GetMouseState(),default);
        }finally {Game1.input=input;Game1.oldMouseState=mouse;Game1.oldKBState=keys;Game1.oldPadState=pad;}
    }
    public static void ClickWorld(IClickableMenu menu,Point tile) {
        int rawX=tile.X*64+32-Game1.viewport.X,rawY=tile.Y*64+32-Game1.viewport.Y;
        int x=(int)Utility.ModifyCoordinateForUIScale(rawX),y=(int)Utility.ModifyCoordinateForUIScale(rawY);
        var input=Game1.input;var mouse=Game1.oldMouseState;
        try {
            Game1.input=new NativeMenuInput(x,y,ButtonState.Pressed);
            // World menus read the previous mouse using zoomLevel, independently
            // of the UI-scaled receiveLeftClick coordinates.
            Game1.oldMouseState=new MouseState((int)(rawX*Game1.options.zoomLevel),(int)(rawY*Game1.options.zoomLevel),0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
            menu.receiveLeftClick(x,y);
        }finally {Game1.input=input;Game1.oldMouseState=mouse;}
    }
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
