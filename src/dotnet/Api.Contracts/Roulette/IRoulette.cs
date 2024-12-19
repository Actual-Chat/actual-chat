using MemoryPack;

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

    // Commands

    [CommandHandler]
    Task OnDeclineChatRoulette(Roulette_DeclineChatRoulette command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Roulette_DeclineChatRoulette(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId
) : IApiCommand;
