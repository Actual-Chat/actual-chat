using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public partial class ChatUserSettingsService : DbServiceBase<UsersDbContext>, IChatUserSettings, IChatUserSettingsBackend
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
    public virtual async Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await Get(user.Id, chatId, cancellationToken).ConfigureAwait(false);

        var options = await _auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        var serializedSettings = options[$"{chatId}::settings"] as string;
        return serializedSettings.IsNullOrEmpty()
            ? null
            : SystemJsonSerializer.Default.Read<ChatUserSettings>(serializedSettings);
    }

    // [CommandHandler]
    public virtual async Task<Unit> Set(IChatUserSettings.SetCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) return default!;

        var (session, chatId, settings) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated) {
            var command1 = new IChatUserSettingsBackend.UpsertCommand(user.Id, chatId, settings);
            await _commander.Call(command1, true, cancellationToken).ConfigureAwait(false);
        }
        else {
            var serializedSettings = SystemJsonSerializer.Default.Write(settings);
            var updatedPair = KeyValuePair.Create($"{chatId}::settings", serializedSettings);
            var command2 = new ISessionOptionsBackend.UpsertCommand(session, updatedPair);
            await _commander.Call(command2, true, cancellationToken).ConfigureAwait(false);
        }
        return default!;
    }
}
