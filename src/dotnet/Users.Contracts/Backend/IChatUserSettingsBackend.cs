namespace ActualChat.Users;

public interface IChatUserSettingsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatUserSettings?> Get(string userId, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatUserSettings> Upsert(UpsertCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpsertCommand(
        [property: DataMember] string UserId,
        [property: DataMember] string ChatId,
        [property: DataMember] ChatUserSettings Settings)
        : ICommand<ChatUserSettings>, IBackendCommand;
}
