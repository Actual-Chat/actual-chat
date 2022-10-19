namespace ActualChat.Users;

public class ChatUserSettingsBackend : IChatUserSettingsBackend
{
    private static ITextSerializer<ChatUserSettings> Serializer => ChatUserSettingsFrontend.Serializer;

    private IServerKvasBackend ServerKvasBackend { get; }

    public ChatUserSettingsBackend(IServiceProvider services)
        => ServerKvasBackend = services.GetRequiredService<IServerKvasBackend>();

    // [ComputeMethod]
    public virtual async Task<ChatUserSettings?> Get(string userId, string chatId, CancellationToken cancellationToken)
    {
        var prefix = ServerKvasBackend.GetUserPrefix(userId);
        var key = ChatUserSettingsFrontend.GetKey(chatId);
        var json = await ServerKvasBackend.Get(prefix, key, cancellationToken).ConfigureAwait(false);
        if (json == null)
            return null;
        try {
            return Serializer.Read(json);
        }
        catch {
            return null;
        }
    }
}
