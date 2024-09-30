namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialPlace
{
    public static readonly Place Unavailable = new(default, 0) {
        Title = "This place is unavailable",
        Rules = PlaceRules.None(default),
    };
    public static readonly Place Loading = new(default, -1) {
        Title = "Loading...",
        Rules = PlaceRules.None(default),
    };
    public static readonly Place NoPlaceSelected = new(default, -2) {
        Title = "Select a place",
        Rules = PlaceRules.None(default),
    };
}
