namespace ActualChat.Feedback;

public interface IFeedback
{
    [CommandHandler]
    public Task CreateFeatureRequest(FeatureRequestCommand command, CancellationToken cancellationToken);

    public record FeatureRequestCommand(Session Session, string Feature) : ISessionCommand<Unit>
    {
        public int Rating { get; init; }
        public int AnswerId { get; init; }
        public string Comment { get; init; } = "";
    }
}
