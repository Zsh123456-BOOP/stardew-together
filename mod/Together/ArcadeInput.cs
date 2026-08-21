using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Minigames;

namespace Together;
// Scoped native inputs only: no changes to enemies, RNG, physics, lives or stats.
internal sealed class ArcadeInput : InputState {
    private readonly Keys[] keys;
    private ArcadeInput(Keys[] keys){this.keys=keys;}
    public override KeyboardState GetKeyboardState()=>new(keys);
    public override MouseState GetMouseState()=>default;
    public override GamePadState GetGamePadState()=>default;
    private static PlayerExecutor? owner;
    private static readonly System.Reflection.FieldInfo? KartPlayer=AccessTools.Field(typeof(MineCart),"player"),KartEntities=AccessTools.Field(typeof(MineCart),"_entities"),KartOver=AccessTools.Field(typeof(MineCart),"gameOver"),KartLives=AccessTools.Field(typeof(MineCart),"livesLeft"),KartScore=AccessTools.Field(typeof(MineCart),"score"),KartTheme=AccessTools.Field(typeof(MineCart),"currentTheme"),KartRespawn=AccessTools.Field(typeof(MineCart),"respawnCounter");
    private static readonly System.Reflection.FieldInfo? JumpAge=AccessTools.Field(typeof(MineCart.MineCartCharacter),"_jumpFloatAge"),SpeedMultiplier=AccessTools.Field(typeof(MineCart.MineCartCharacter),"_speedMultiplier");
    private static Vector2 previousPrairieMove;
    internal static void ResetPolicy(){previousPrairieMove=Vector2.Zero;}
    internal static void Install(string id,PlayerExecutor executor) {
        owner=executor;var harmony=new Harmony(id+".native-arcade-input");
        harmony.Patch(AccessTools.Method(typeof(AbigailGame),nameof(AbigailGame.tick)),prefix:new HarmonyMethod(typeof(ArcadeInput),nameof(PrairiePrefix)),finalizer:new HarmonyMethod(typeof(ArcadeInput),nameof(Restore)));
        harmony.Patch(AccessTools.Method(typeof(MineCart),nameof(MineCart.tick)),prefix:new HarmonyMethod(typeof(ArcadeInput),nameof(KartPrefix)),finalizer:new HarmonyMethod(typeof(ArcadeInput),nameof(Restore)));
        harmony.Patch(AccessTools.Method(typeof(MineCart),nameof(MineCart.submitHighScore)),postfix:new HarmonyMethod(typeof(ArcadeInput),nameof(ScoreSubmitted)));
    }
    private static void ScoreSubmitted(MineCart __instance){if(owner?.OwnsArcade==true&&Game1.currentMinigame==__instance&&KartScore?.GetValue(__instance) is int score)owner.ObserveArcadeScore(score);}
    private sealed record Saved(InputState Input);
    private static void PrairiePrefix(AbigailGame __instance,out Saved? __state) {
        __state=null;if(owner?.OwnsArcade!=true||Game1.currentMinigame!=__instance)return;
        try{var keys=owner.ArcadeKeys(__instance);__state=new(Game1.input);Game1.input=new ArcadeInput(keys);}catch(Exception e){owner.ArcadeInputFault(e.GetType().Name);}
    }
    private static void KartPrefix(MineCart __instance,out Saved? __state) {
        __state=null;if(owner?.OwnsArcade!=true||Game1.currentMinigame!=__instance)return;
        try{bool jump=owner.ArcadeJump(__instance);__state=new(Game1.input);Game1.input=new ArcadeInput(jump?new[]{Keys.Space}:Array.Empty<Keys>());}catch(Exception e){owner.ArcadeInputFault(e.GetType().Name);}
    }
    private static void Restore(Saved? __state){if(__state!=null)Game1.input=__state.Input;}
    internal static object? ReadState()=>Game1.currentMinigame switch {
        AbigailGame g=>new{game="prairie",wave=AbigailGame.whichWave,world=AbigailGame.world,g.lives,g.coins,g.score,g.died,position=g.playerPosition,monsters=AbigailGame.monsters.Count,bullets=AbigailGame.enemyBullets.Count,game_over=AbigailGame.gameOver},
        MineCart g=>new{game="kart",phase=g.gameState.ToString(),score=KartScore?.GetValue(g),lives=KartLives?.GetValue(g),theme=KartTheme?.GetValue(g),position=(KartPlayer?.GetValue(g) as MineCart.MineCartCharacter)?.position,game_over=KartGameOver(g)},
        _=>null
    };
    internal static Keys PrairieMoveKey(AbigailGame game,int direction) {
        var binding=direction switch{0=>Game1.options.moveUpButton,1=>Game1.options.moveRightButton,2=>Game1.options.moveDownButton,_=>Game1.options.moveLeftButton};
        Keys key=game.GetBoundKey(binding);return key is Keys.None or Keys.Up or Keys.Right or Keys.Down or Keys.Left?new[]{Keys.W,Keys.D,Keys.S,Keys.A}[direction]:key;
    }
    internal static Keys[] PrairieKeys(AbigailGame game,int frame) {
        if(AbigailGame.onStartMenu||AbigailGame.scrollingMap||AbigailGame.endCutscene||AbigailGame.deathTimer>0||game.motionPause>0)return Array.Empty<Keys>();
        var center=game.playerBoundingBox.Center.ToVector2();var enemies=AbigailGame.monsters.Where(m=>m.health>0).OrderBy(m=>Vector2.DistanceSquared(m.position.Center.ToVector2(),center)).Take(32).ToArray();
        Vector2 goal=new(384,384);
        var pickup=AbigailGame.powerups.OrderBy(p=>Vector2.DistanceSquared(p.position.ToVector2()+new Vector2(24),center)).FirstOrDefault();
        if(pickup!=null)goal=pickup.position.ToVector2()+new Vector2(24);
        if(AbigailGame.waitingForPlayerToMoveDownAMap)goal=new(384,748);
        if(AbigailGame.merchantShopOpen) {
            var desired=game.storeItems.Where(i=>game.getPriceForItem(i.Value)<=game.coins).OrderBy(i=>i.Value is >=6 and <=8?0:i.Value is >=0 and <=2?1:2).ThenBy(i=>game.getPriceForItem(i.Value)).ToArray();
            if(desired.Length>0)goal=game.shoppingCarpetNoPickup.Intersects(game.playerBoundingBox)?new Vector2(384,480):desired[0].Key.Center.ToVector2();
        }
        // Reverse breadth-first distances avoid local-potential traps behind walls.
        var distances=PrairieDistances(game,goal);Vector2 chosen=Vector2.Zero;float best=float.NegativeInfinity;
        float speed=3+game.runSpeedLevel*.6f;
        foreach(var direction in Directions) {
            var step=direction==Vector2.Zero?direction:Vector2.Normalize(direction);var endpoint=center+step*speed*9;
            bool blocked=false;float danger=0;
            for(int t=1;t<=9;t+=2) {
                var p=center+step*speed*t;var box=new Rectangle((int)p.X-12,(int)p.Y-12,24,24);
                if(AbigailGame.isCollidingWithMap(box)||game.merchantBox.Intersects(box)&&!game.merchantBox.Intersects(game.playerBoundingBox)){blocked=true;break;}
                foreach(var enemy in enemies) {
                    float gap=Vector2.Distance(p,enemy.position.Center.ToVector2())-34-enemy.speed*t;
                    danger+=gap<0?500:120/(gap+8);
                }
                foreach(var bullet in AbigailGame.enemyBullets) {
                    float gap=Vector2.Distance(p,bullet.position.ToVector2()+bullet.motion.ToVector2()*t)-20;
                    danger+=gap<0?1000:80/(gap+8);
                }
            }
            if(blocked)continue;
            int tx=Math.Clamp((int)endpoint.X/24,0,31),ty=Math.Clamp((int)endpoint.Y/24,0,31);
            float route=distances[tx,ty]<0?1000:distances[tx,ty]*24+Vector2.Distance(endpoint,new Vector2(tx*24+12,ty*24+12));
            float score=-danger*7-route*.045f+Vector2.Dot(step,previousPrairieMove)*.8f;
            if(enemies.Length>0&&!AbigailGame.waitingForPlayerToMoveDownAMap)score-=Math.Max(0,70-Math.Min(Math.Min(endpoint.X,768-endpoint.X),Math.Min(endpoint.Y,768-endpoint.Y)))*.08f;
            if(score>best){best=score;chosen=step;}
        }
        previousPrairieMove=chosen;var keys=new List<Keys>();
        if(chosen.X<0)keys.Add(PrairieMoveKey(game,3));if(chosen.X>0)keys.Add(PrairieMoveKey(game,1));if(chosen.Y<0)keys.Add(PrairieMoveKey(game,0));if(chosen.Y>0)keys.Add(PrairieMoveKey(game,2));
        // Shoot along the closest legal 8-way direction. Native bullet collision
        // remains authoritative, including boss invulnerability and obstacles.
        if(enemies.Length>0) {
            Vector2 aim=enemies[0].position.Center.ToVector2()-center;
            if(Math.Abs(aim.X)>Math.Abs(aim.Y)*.4142f)keys.Add(aim.X<0?Keys.Left:Keys.Right);
            if(Math.Abs(aim.Y)>Math.Abs(aim.X)*.4142f)keys.Add(aim.Y<0?Keys.Up:Keys.Down);
            if(game.heldItem!=null&&frame%20==0&&(enemies.Length>=8||aim.Length()<110||AbigailGame.shootoutLevel))keys.Add(Keys.Enter);
        }
        return keys.ToArray();
    }
    private static readonly Vector2[] Directions={Vector2.Zero,new(1,0),new(-1,0),new(0,1),new(0,-1),new(1,1),new(1,-1),new(-1,1),new(-1,-1)};
    private static int[,] PrairieDistances(AbigailGame game,Vector2 goal) {
        var distance=new int[32,32];var open=new bool[32,32];Point nearest=Point.Zero;float nearestDistance=float.MaxValue;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++) {
            distance[x,y]=-1;var box=new Rectangle(x*24+1,y*24+1,22,22);open[x,y]=!AbigailGame.isCollidingWithMap(box)&&!box.Intersects(game.merchantBox);
            float d=Vector2.DistanceSquared(box.Center.ToVector2(),goal);if(open[x,y]&&d<nearestDistance){nearestDistance=d;nearest=new(x,y);}
        }
        if(nearestDistance==float.MaxValue)return distance;
        var queue=new Queue<Point>();queue.Enqueue(nearest);distance[nearest.X,nearest.Y]=0;
        while(queue.TryDequeue(out var p))foreach(var d in Directions.Skip(1).Take(4)) {
            int x=p.X+(int)d.X,y=p.Y+(int)d.Y;if(x<0||y<0||x>=32||y>=32||!open[x,y]||distance[x,y]>=0)continue;
            distance[x,y]=distance[p.X,p.Y]+1;queue.Enqueue(new(x,y));
        }
        return distance;
    }
    internal static bool KartGameOver(MineCart game)=>(bool?)KartOver?.GetValue(game)??false;
    internal static bool KartJump(MineCart game,int frame) {
        if(game.gamePaused)throw new InvalidOperationException("native_arcade_paused");
        if(game.gameState is MineCart.GameStates.Title or MineCart.GameStates.Map or MineCart.GameStates.Cutscene)return frame%12==0;
        if(game.gameState!=MineCart.GameStates.Ingame||game.deathTimer>0||(int?)KartRespawn?.GetValue(game)>0)return false;
        if(KartPlayer?.GetValue(game) is not MineCart.MineCartCharacter player||KartEntities?.GetValue(game) is not List<MineCart.Entity> entities)throw new InvalidOperationException("native_kart_state_unavailable");
        if(!player.IsActive())return false;
        var obstacles=entities.OfType<MineCart.Obstacle>().Where(e=>e.IsActive()&&e.position.X>player.position.X-24&&e.position.X<player.position.X+300).Select(e=>e.GetBounds()).ToArray();
        float best=float.NegativeInfinity;bool hold=false;
        // Receding-horizon calculation on copied scalar state. Do not tick or
        // clone live entities (their callbacks consume RNG and change progress).
        foreach(float delay in player.IsGrounded()?new[]{0f,.08f,.16f,.24f,.36f,2f}:new[]{0f})foreach(float duration in new[]{0f,.08f,.16f,.28f,.42f,.65f}) {
            float score=PredictKart(game,player,obstacles,delay,duration);
            bool first=delay==0&&duration>0;
            if(first==game.isJumpPressed)score+=.02f;
            if(score>best){best=score;hold=first;}
        }
        // A landed held jump needs a release edge before a new jump is queued.
        return !(player.IsGrounded()&&game.isJumpPressed)&&hold;
    }
    private static float PredictKart(MineCart game,MineCart.MineCartCharacter player,Rectangle[] obstacles,float delay,float duration) {
        Vector2 p=player.position,v=player.velocity;float gravity=player.gravity,age=(float?)JumpAge?.GetValue(player)??0,mult=(float?)SpeedMultiplier?.GetValue(player)??1;
        bool grounded=player.IsGrounded(),jumping=player.IsJumping(),started=false;float grace=player.jumpGracePeriod,score=0;
        const float dt=1f/60;
        for(int frame=0;frame<90;frame++) {
            float time=frame*dt;bool pressed=time>=delay&&time<delay+duration;
            if(!started&&pressed&&(grounded||grace>0)){started=true;grounded=false;jumping=true;age=gravity=0;v.Y=-player.jumpStrength;}
            if(!pressed&&jumping&&player.forcedJumpTime<=time){jumping=false;gravity=0;v.Y=Math.Max(-30,v.Y);}
            var tracks=game.GetTracksForXPosition(p.X);
            var track=tracks?.Where(t=>t.IsActive()&&t.CanLandHere(p)).OrderBy(t=>Math.Abs(t.GetYAtPoint(p.X)-p.Y)).FirstOrDefault();
            if(track!=null&&v.Y>=0){p.Y=track.GetYAtPoint(p.X);v.Y=gravity=0;grounded=true;jumping=false;}
            else if(grounded) {
                track=tracks?.FirstOrDefault(t=>t.IsActive()&&t.CanLandHere(p+new Vector2(0,2)));
                if(track!=null)p.Y=track.GetYAtPoint(p.X);else{grounded=false;grace=MineCart.maxJumpGraceTime;v.Y=player.GetMaxFallSpeed();gravity=0;}
            }
            if(!grounded) {
                if(jumping){age+=dt;if(age<player.jumpFloatDuration){gravity=0;v.Y=-player.jumpStrength*age/player.jumpFloatDuration;}else if(v.Y<=-60)gravity+=dt*player.jumpGravity;else{v.Y=-30;jumping=false;gravity=0;}}
                else gravity+=dt*player.fallGravity;
                v.Y+=dt*gravity;
            }
            if(grounded&&track!=null)mult=track.trackType==MineCart.Track.TrackType.SlimeUpSlope?.5f:track.trackType==MineCart.Track.TrackType.IceDownSlope?Math.Min(3,mult+dt*2):MathHelper.Lerp(mult,1,Math.Min(1,dt*6));
            p.X+=dt*v.X*mult;p.Y+=dt*v.Y;v.Y=Math.Min(v.Y,player.GetMaxFallSpeed());grace-=dt;
            var bounds=new Rectangle((int)p.X-5,(int)p.Y-13,10,13);
            if(obstacles.Any(o=>o.Intersects(bounds)))return -10000+frame*8;
            if(p.Y>(game.bottomTile+3)*game.tileSize)return -15000+frame*8;
            score+=grounded?.04f:0;
        }
        // Prefer safe landing opportunities over jumping for arbitrary distance.
        var landing=game.GetTracksForXPosition(p.X);
        return score+(grounded?8:landing?.Any(t=>t.IsActive()&&t.GetYAtPoint(p.X)>=p.Y)==true?3:-10)-duration*.2f;
    }
}
