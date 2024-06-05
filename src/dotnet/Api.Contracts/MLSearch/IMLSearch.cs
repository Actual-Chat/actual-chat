using MemoryPack;

using MLSearchChatId = ActualChat.ChatId;

namespace ActualChat.MLSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_CreateChat(
    // TODO: [Andrew K] Ensure we have some form of form token 
    // to prevent replay on accidental double clicks.
    // Fix if it is not generally available.
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<MLSearchChat>, IApiCommand;

public interface IMLSearch : IComputeService
{
    // Commands

    [CommandHandler]
    Task<MLSearchChat> OnCreate(MLSearch_CreateChat command, CancellationToken cancellationToken);
}
