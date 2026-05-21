namespace ActualChat.Chat;

public sealed class PlaceMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public PlaceId PlaceId => (PlaceId)Id.TargetId;
    public Place? Place { get; init; }
}
