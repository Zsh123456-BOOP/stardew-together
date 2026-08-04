using Microsoft.Xna.Framework;
using StardewValley;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private static void AnimateHoe(NPC npc) {
        // Vanilla Tools sheet: basic hoe starts on row 1; pickaxe is row 5.
        // The body remains at its checked interaction tile throughout the stroke.
        int facing=npc.FacingDirection;
        float depth=(npc.GetBoundingBox().Bottom+(facing==0?-32:2))/10000f;
        bool side=facing is 1 or 3;
        var source=new Rectangle(side?32:facing==0?48:0,16,16,32);
        var offset=side?new Vector2(facing==1?16:-16,-103):new Vector2(0,facing==0?-128:-80);
        var lift=new TemporaryAnimatedSprite(Game1.toolSpriteSheet.Name,source,100f,1,0,npc.Position+offset,false,facing==3,depth,0,Color.White,4,0,0,0);
        var strike=new TemporaryAnimatedSprite(Game1.toolSpriteSheet.Name,source,300f,1,0,
            npc.Position+(side?new Vector2(facing==1?64:-64,-48):offset+new Vector2(0,24)),false,facing==3,
            depth,0,Color.White,4,0,side?(facing==1?MathHelper.PiOver2:-MathHelper.PiOver2):0,0){delayBeforeAnimationStart=100};
        Game1.Multiplayer.broadcastSprites(npc.currentLocation,lift,strike);
    }
}
