namespace ActualChat.Feedback;

public interface IFeedbacks : IComputeService
{
    [CommandHandler]
    public Task CreateFeatureRequest(FeatureRequestCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record FeatureRequestCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string Feature
        ) : ISessionCommand<Unit>
    {
        [DataMember] public int Rating { get; init; }
        [DataMember] public string Comment { get; init; } = "";
    }
}
