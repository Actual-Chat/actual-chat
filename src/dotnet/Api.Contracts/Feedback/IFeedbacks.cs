using MemoryPack;

namespace ActualChat.Feedback;

public interface IFeedbacks : IComputeService
{
    [CommandHandler]
    public Task OnCreateFeatureRequest(Feedbacks_FeatureRequest command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Feedbacks_FeatureRequest(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string Feature
) : ISessionCommand<Unit>
{
    [DataMember, MemoryPackOrder(2)] public int Rating { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Comment { get; init; } = "";
}
