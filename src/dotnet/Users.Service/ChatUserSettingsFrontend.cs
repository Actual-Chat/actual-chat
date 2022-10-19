using ActualChat.Kvas;

namespace ActualChat.Users;

public class ChatUserSettingsFrontend : IChatUserSettings
{
    internal static ITextSerializer<ChatUserSettings> Serializer { get; } =
        SystemJsonSerializer.Default.ToTyped<ChatUserSettings>();

    private IServerKvas ServerKvas { get; }
    private ICommander Commander { get; }

    public ChatUserSettingsFrontend(IServiceProvider services)
    {
        ServerKvas = services.ServerKvas();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var jsonOpt = await ServerKvas.Get(session, GetKey(chatId), cancellationToken).ConfigureAwait(false);
        if (!jsonOpt.IsSome(out var json))
            return null;
        try {
            return Serializer.Read(json);
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
        var json = Serializer.Write(settings);
        await ServerKvas.Set(session, GetKey(chatId), json, cancellationToken).ConfigureAwait(false);
    }

    // Private & internal methods

    internal static string GetKey(string chatId)
        => $"@ChatUserSettings({chatId})";
}
