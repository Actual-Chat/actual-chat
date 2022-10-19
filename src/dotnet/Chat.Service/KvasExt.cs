using ActualChat.Kvas;

namespace ActualChat.Chat;

public static class KvasExt
{
    public static async ValueTask<ChatAuthorSettings> GetAuthorSettings(this IKvas kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.Get<ChatAuthorSettings>(ChatAuthorSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetAuthorSettings(this IKvas kvas, ChatAuthorSettings value, CancellationToken cancellationToken)
        => kvas.Set(ChatAuthorSettings.KvasKey, value, cancellationToken);
}
