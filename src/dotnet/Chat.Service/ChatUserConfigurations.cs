using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public partial class ChatUserConfigurations : DbServiceBase<ChatDbContext>, IChatUserConfigurations, IChatUserConfigurationsBackend
{
    private readonly IAuth _auth;
    private readonly RedisSequenceSet<ChatUserConfiguration> _idSequences;
    private readonly ICommander _commander;

    public ChatUserConfigurations(
        IAuth auth,
        RedisSequenceSet<ChatUserConfiguration> idSequences,
        ICommander commander,
        IServiceProvider serviceProvider
    ) : base(serviceProvider)
    {
        _auth = auth;
        _idSequences = idSequences;
        _commander = commander;
    }

    private const string DefaultLanguage = "ru-RU";

    public virtual async Task<string> GetTranscriptionLanguage(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        string? langauge;
        if (user.IsAuthenticated)
            langauge = await GetTranscriptionLanguageFromUserConfiguration(user.Id, chatId, cancellationToken).ConfigureAwait(false);
        else
            langauge = await GetTranscriptionLanguageFromSessionInfo(session, chatId, cancellationToken).ConfigureAwait(false);
        return langauge.NullIfEmpty() ?? DefaultLanguage;
    }

    public virtual async Task<Unit> SetTranscriptionLanguage(IChatUserConfigurations.SetTranscriptionLanguageCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            return default!;
        }

        var (session, chatId, language) = command;

        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);

        if (!user.IsAuthenticated)
            await SetTranscriptionLanguageInSessionInfo(session, chatId, language, cancellationToken).ConfigureAwait(false);
        else
            await SetTranscriptionLanguageInUserConfiguration(user.Id, chatId, language, cancellationToken).ConfigureAwait(false);

        return default!;
    }

    [ComputeMethod]
    protected virtual async Task<string?> GetTranscriptionLanguageFromSessionInfo(Session session, string chatId, CancellationToken cancellationToken)
    {
        var sessionInfo = await _auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var langauge = sessionInfo.Options[$"{chatId}::transcriptionLanguage"] as string;
        return langauge;
    }

    [ComputeMethod]
    public virtual async Task<string?> GetTranscriptionLanguageFromUserConfiguration(string userId, string chatId, CancellationToken cancellationToken)
    {
        var chatAuthorOptions = await Get(userId, chatId, cancellationToken).ConfigureAwait(false);
        var langauge = chatAuthorOptions?.Options["transcriptionLanguage"] as string;
        return langauge;
    }

    protected virtual async Task SetTranscriptionLanguageInSessionInfo(Session session, string chatId, string language, CancellationToken cancellationToken)
    {
        var updateOptionCommand = new ISessionOptionsBackend.UpdateCommand(session, new($"{chatId}::transcriptionLanguage", language));
        await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
    }

    protected virtual async Task<Unit> SetTranscriptionLanguageInUserConfiguration(string userId, string chatId, string language, CancellationToken cancellationToken)
    {
        _ = await GetOrCreate(userId, chatId, cancellationToken).ConfigureAwait(false);
        var updateOptionCommand = new IChatUserConfigurationsBackend.UpdateCommand(userId, chatId, new("transcriptionLanguage", language));
        return await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
