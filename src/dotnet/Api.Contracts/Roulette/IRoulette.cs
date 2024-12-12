namespace ActualChat.Roulette;

public interface IRoulette : IComputeService
{
    Task<ImmutableArray<ChatCandidate>> FindChatCandidates(Session session, Preferences filter, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatId> GetOrCreateChat(Session session, Symbol ownProfileId, Symbol peerProfileId, CancellationToken cancellationToken);
}
