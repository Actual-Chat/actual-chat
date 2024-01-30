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
        => authorRules != null
            ? new (placeId, authorRules.Author, authorRules.Account, (PlacePermissions)(int)authorRules.Permissions)
            : null;
}
