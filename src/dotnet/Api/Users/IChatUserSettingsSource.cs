namespace ActualChat.Users;

/// <summary>
/// Lets <see cref="UserSettingsUI"/> read <see cref="ChatUserSettings"/> through a call the client
/// makes anyway, instead of one more <see cref="IUserSettings.Get"/> call per chat. Registered on
/// clients only; null means "no answer here", and the settings are read directly.
/// </summary>
public interface IChatUserSettingsSource
{
    Task<ChatUserSettings?> Get(ChatId chatId, CancellationToken cancellationToken);
}
