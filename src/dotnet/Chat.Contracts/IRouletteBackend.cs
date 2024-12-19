using ActualChat.Roulette;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IRouletteBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatRouletteFull?> GetChatRoulette(ChatRouletteId id, CancellationToken cancellationToken);

    // Commands
    [CommandHandler]
    Task<ChatRouletteFull> OnChangeChatRoulette(
        RouletteBackend_ChangeChatRoulette command,
        CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatRouletteFull(ChatRouletteId Id, long Version = 0)
    : ChatRoulette(Id, Version)
{
    [DataMember, MemoryPackOrder(3)] public UserId UserId1 { get; init; }
    [DataMember, MemoryPackOrder(4)] public UserId UserId2 { get; init; }

    public ChatRoulette ToChatRoulette()
        => new (Id, Version) { ChatId = ChatId };
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteBackend_ChangeChatRoulette(
    [property: DataMember, MemoryPackOrder(0)] ChatRouletteId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatRouletteFull> Change
) : IBackendCommand<ChatRouletteFull>;
