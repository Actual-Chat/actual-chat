using MemoryPack;

namespace ActualChat.MLSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_CreateChat(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MLSearchChatId MLSearchChatId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion
) : ISessionCommand<MLSearchChat>, IApiCommand;

public interface IMLSearch
{
    // Commands

    [CommandHandler]
    Task<MLSearchChat> OnCreate(MLSearch_CreateChat command, CancellationToken cancellationToken);
}
