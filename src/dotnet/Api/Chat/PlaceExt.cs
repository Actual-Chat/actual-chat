namespace ActualChat.Chat;

public static class PlaceExt
{
    public static bool IsMember(this Place place)
        => place.Rules.IsMember();

    public static bool IsMember(this PlaceRules placeRules)
        => placeRules.Author is { HasLeft: false };
}
