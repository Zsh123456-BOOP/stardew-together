namespace Together;
public sealed class CustomPartner {
    public bool Enabled {get;set;}=true;
    public string DisplayName {get;set;}="小禾";
    // Native sprite/portrait asset reference; no villager instance is recruited.
    public string Appearance {get;set;}="Leah";
    public string Location {get;set;}="Farm";
    public int X {get;set;}=64;
    public int Y {get;set;}=16;
    public bool Created {get;set;}
}
