namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialPlace
{
    public static readonly Place Unavailable = new(null!, 0) {
        Title = "This Place is unavailable",
        Rules = PlaceRules.None(null!),
    };
    public static readonly Place Loading = new(null!, -1) {
        Title = "Loading...",
        Rules = PlaceRules.None(null!),
    };
    public static readonly Place NoPlaceSelected = new(null!, -2) {
        Title = "Select a Place",
        Rules = PlaceRules.None(null!),
    };
}
