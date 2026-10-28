namespace ActualChat.Users;

/// <summary>
/// Lets <see cref="UserSettingsUI"/> read <see cref="ChatUserSettings"/> through a call the UI
/// makes anyway, instead of one more <see cref="IUserSettings.Get"/> call per chat. Passed to the UI
/// scope's UserSettingsUI only; null means "no answer here", and the settings are read directly.
/// </summary>
public interface IChatUserSettingsSource
{
    Task<ChatUserSettings?> Get(ChatId chatId, CancellationToken cancellationToken);
}
