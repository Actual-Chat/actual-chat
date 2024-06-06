using MemoryPack;

using MLSearchChatId = ActualChat.ChatId;

namespace ActualChat.MLSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_CreateChat(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string Title,
    [property: DataMember, MemoryPackOrder(2)] MediaId? MediaId
) : ISessionCommand<MLSearchChat>, IApiCommand;

public interface IMLSearch : IComputeService
{
    // Commands

    [CommandHandler]
    Task<MLSearchChat> OnCreate(MLSearch_CreateChat command, CancellationToken cancellationToken);
}
