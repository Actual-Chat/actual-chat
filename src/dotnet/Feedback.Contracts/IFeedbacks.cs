namespace ActualChat.Feedback;

public interface IFeedbacks
{
    [CommandHandler]
    public Task CreateFeatureRequest(FeatureRequestCommand command, CancellationToken cancellationToken);

    public record FeatureRequestCommand(Session Session, string Feature) : ISessionCommand<Unit>
    {
        public int Rating { get; init; }
        public string Comment { get; init; } = "";
    }
}
