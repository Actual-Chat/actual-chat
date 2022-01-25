namespace ActualChat.Chat;

public interface IInviteCodesBackend
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<InviteCode?> GetByValue(string inviteCode, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<InviteCode>> Get(string chatId, string userId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<InviteCode> Generate(GenerateCommand command, CancellationToken cancellationToken);

    public record GenerateCommand(InviteCode InviteCode) : ICommand<InviteCode>, IBackendCommand;
}
