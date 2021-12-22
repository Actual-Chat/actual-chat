namespace ActualChat.Chat;

public interface IChatUserConfigurations
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string> GetTranscriptionLanguage(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> SetTranscriptionLanguage(SetTranscriptionLanguageCommand command, CancellationToken cancellationToken);

    public record SetTranscriptionLanguageCommand(Session Session, string ChatId, string Language) : ISessionCommand<Unit>;
}
