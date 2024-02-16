namespace ActualChat.Chat;

public static class ChatExt
{
    public static Place ToPlace(this Chat chat)
    {
        chat.Require(Chat.MustBePlaceRoot);
        return new Place(chat.Id.PlaceId, chat.Version) {
            CreatedAt = chat.CreatedAt,
            IsPublic = chat.IsPublic,
            Title = chat.Title,
            MediaId = chat.MediaId,
            Picture = chat.Picture,
            Rules = chat.Rules.ToPlaceRules(chat.Id.PlaceId)!,
        };
    }

    public static PlaceRules? ToPlaceRules(this AuthorRules? authorRules, PlaceId placeId)
    {
        if (authorRules == null)
            return null;

        var placePermissions = (PlacePermissions)(int)authorRules.Permissions;
        if ((placePermissions & PlacePermissions.Owner) == 0) // Only Owners can invite so far.
            placePermissions &= ~PlacePermissions.Invite;
        return new (placeId, authorRules.Author, authorRules.Account, placePermissions);
    }
}
