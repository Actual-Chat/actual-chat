namespace ActualChat.Roulette;

public interface IRoulette : IComputeService
{
    Task<ImmutableArray<ChatCandidate>> FindChatCandidates(Session session, Preferences filter, CancellationToken cancellationToken);

    // [ComputeMethod]
    // Task<ChatRoulette?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatRouletteProfiles?> GetProfiles(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatId> GetOrCreateChat(Session session, Symbol ownProfileId, Symbol peerProfileId, CancellationToken cancellationToken);
}
