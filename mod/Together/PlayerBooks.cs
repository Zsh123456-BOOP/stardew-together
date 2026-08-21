using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Together;
public sealed partial class PlayerExecutor {
    private string bookItem="";
    private int bookBefore;
    private uint bookPower;
    private int[] bookExperience=Array.Empty<int>();
    private HashSet<string> bookRecipes=new();
    private void StartReadBook(JsonElement args) {
        SelectSlot(args,true);
        if(Game1.player.ActiveObject is not {} book||book.Category is not (-102 or -103))throw new InvalidOperationException("carried_readable_book_required");
        string expected=AgentToolRegistry.Text(args,"item");if(expected.Length>0&&book.QualifiedItemId!=expected)throw new InvalidOperationException("expected_book_slot_changed");
        if(Game1.eventUp||Game1.isFestival()||Game1.player.swimming.Value||Game1.player.bathingClothes.Value||Game1.player.onBridge.Value)throw new InvalidOperationException("native_book_reading_unavailable_here");
        ValidateConsumption?.Invoke(new Dictionary<Item,int>{{book,1}},"","read:"+book.QualifiedItemId);
        bookItem=book.QualifiedItemId;bookBefore=Game1.player.Items.Where(i=>i?.QualifiedItemId==bookItem).Sum(i=>i.Stack);bookPower=Game1.player.stats.Get(book.ItemId);
        bookExperience=Game1.player.experiencePoints.ToArray();bookRecipes=KnownRecipeKeys();
        var location=Game1.currentLocation;Point center=Game1.player.TilePoint;
        Point? clear=new[]{new Point(center.X,center.Y+1),new Point(center.X+1,center.Y),new Point(center.X-1,center.Y),new Point(center.X,center.Y-1)}.Where(p=>Passable(location,p)&&!location.objects.ContainsKey(p.ToVector2())&&!location.terrainFeatures.ContainsKey(p.ToVector2())&&location.isCharacterAtTile(p.ToVector2())==null&&location.doesTileHaveProperty(p.X,p.Y,"Action","Buildings")==null&&location.doesTileHaveProperty(p.X,p.Y,"TouchAction","Back")==null).Select(p=>(Point?)p).FirstOrDefault();
        if(clear==null)throw new InvalidOperationException("move_to_clear_reading_space");
        Face(clear.Value);NativeMenuInput.PressAction(clear.Value);
        if(bookBefore-Game1.player.Items.Where(i=>i?.QualifiedItemId==bookItem).Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_book_consumption_not_verified");
        Current!.phase="reading_book";
    }
    private void TickReadBook() {
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("book_menu_requires_review");
        if(!Game1.player.CanMove||Game1.player.freezePause>0)return;
        string raw=ItemRegistry.Create(bookItem).ItemId;uint power=Game1.player.stats.Get(raw);var experience=Game1.player.experiencePoints.ToArray();var learned=KnownRecipeKeys().Except(bookRecipes).ToArray();
        bool changed=power>bookPower||experience.Where((value,index)=>value>bookExperience[index]).Any()||learned.Length>0;
        Current!.effects.Add(new{kind="native_book_read",book=bookItem,consumed=1,power_before=bookPower,power_after=power,experience_before=bookExperience,experience_after=experience,recipes_learned=learned});
        Current.completed=changed?1:0;Finish(changed?"succeeded":"failed",changed?null:"book_effect_not_verified");
    }
}
