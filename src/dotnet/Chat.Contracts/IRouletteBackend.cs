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

    // Events
    [EventHandler]
    Task OnAuthorLeftEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken);
}



[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteBackend_ChangeChatRoulette(
    [property: DataMember, MemoryPackOrder(0)] ChatRouletteId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatRouletteFullDiff> Change
) : IBackendCommand<ChatRouletteFull>;
