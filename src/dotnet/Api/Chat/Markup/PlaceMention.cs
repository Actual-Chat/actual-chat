namespace ActualChat.Chat;

public sealed class PlaceMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    public PlaceId PlaceId => (PlaceId)Id.Target;
    public Place? Place { get; init; }
}
