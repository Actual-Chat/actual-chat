using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public partial class ChatUserSettingsService : DbServiceBase<ChatDbContext>, IChatUserSettings, IChatUserSettingsBackend
{
    private readonly IAuth _auth;
    private readonly ICommander _commander;

    public ChatUserSettingsService(
        IAuth auth,
        ICommander commander,
        IServiceProvider serviceProvider
    ) : base(serviceProvider)
    {
        _auth = auth;
        _commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<LanguageId> GetLanguage(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        LanguageId language;
        if (user.IsAuthenticated)
            language = await GetChatUserSettingsLanguage(user.Id, chatId, cancellationToken).ConfigureAwait(false);
        else
            language = await GetSessionInfoLanguage(session, chatId, cancellationToken).ConfigureAwait(false);
        return language.ValidOrDefault();
    }

    // [CommandHandler]
    public virtual async Task<Unit> SetLanguage(IChatUserSettings.SetLanguageCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            return default!;
        }

        var (session, chatId, language) = command;

        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            await SetSessionInfoLanguage(session, chatId, language, cancellationToken).ConfigureAwait(false);
        else
            await SetChatUserSettingsLanguage(user.Id, chatId, language, cancellationToken).ConfigureAwait(false);
        return default!;
    }

    [ComputeMethod]
    public virtual async Task<LanguageId> GetChatUserSettingsLanguage(string userId, string chatId, CancellationToken cancellationToken)
    {
        var settings = await Get(userId, chatId, cancellationToken).ConfigureAwait(false);
        return settings?.Language ?? LanguageId.None;
    }

    [ComputeMethod]
    protected virtual async Task<LanguageId> GetSessionInfoLanguage(Session session, string chatId, CancellationToken cancellationToken)
    {
        var sessionInfo = await _auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var language = sessionInfo.Options[$"{chatId}::language"] as string ?? "";
        return new LanguageId(language).ValidOrDefault();
    }

    protected virtual async Task<Unit> SetChatUserSettingsLanguage(string userId, string chatId, LanguageId language, CancellationToken cancellationToken)
    {
        _ = await GetOrCreate(userId, chatId, cancellationToken).ConfigureAwait(false);
        var updateOptionCommand = new IChatUserSettingsBackend.SetLanguageCommand(userId, chatId, language);
        return await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
    }

    protected virtual async Task SetSessionInfoLanguage(Session session, string chatId, LanguageId language, CancellationToken cancellationToken)
    {
        var update = KeyValuePair.Create($"{chatId}::language", language.Value);
        var updateOptionCommand = new ISessionOptionsBackend.UpdateCommand(session, update);
        await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
