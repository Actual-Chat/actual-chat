using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public partial class ChatUserSettingsService : DbServiceBase<UsersDbContext>, IChatUserSettings, IChatUserSettingsBackend
{
    private static ITextSerializer<ChatUserSettings> Serializer { get; } =
        SystemJsonSerializer.Default.ToTyped<ChatUserSettings>();

    private readonly IAuth _auth;
    private readonly ICommander _commander;

    public ChatUserSettingsService(IAuth auth, ICommander commander)
        : base(commander.Services)
    {
        _auth = auth;
        _commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user != null)
            return await Get(user.Id, chatId, cancellationToken).ConfigureAwait(false);

        var options = await _auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        var serializedSettings = options[$"ChatUserSettings::{chatId}"] as string;
        if (serializedSettings.IsNullOrEmpty())
            return null;
        try {
            return Serializer.Read(serializedSettings);
        }
        catch {
            return null;
        }
    }

    // [CommandHandler]
    public virtual async Task Set(IChatUserSettings.SetCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) return;

        var (session, chatId, settings) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user != null) {
            var command1 = new IChatUserSettingsBackend.UpsertCommand(user.Id, chatId, settings);
            await _commander.Call(command1, true, cancellationToken).ConfigureAwait(false);
        }
        else {
            var serializedSettings = Serializer.Write(settings);
            var updatedPair = KeyValuePair.Create($"ChatUserSettings::{chatId}", serializedSettings);
            var command2 = new ISessionOptionsBackend.UpsertCommand(session, updatedPair);
            await _commander.Call(command2, true, cancellationToken).ConfigureAwait(false);
        }
    }
}
