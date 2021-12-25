namespace ActualChat.Chat;

public interface IChatUserSettings
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<LanguageId> GetLanguage(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> SetLanguage(SetLanguageCommand command, CancellationToken cancellationToken);

    public record SetLanguageCommand(Session Session, string ChatId, LanguageId Language) : ISessionCommand<Unit>;
}
