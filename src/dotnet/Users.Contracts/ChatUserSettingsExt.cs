namespace ActualChat.Users;

public static class ChatUserSettingsExt
{
    // TODO: replace with language settings
    public static LanguageId LanguageOrDefault(this ChatUserSettings? settings)
        => settings?.Language.ValidOrDefault() ?? LanguageId.Default.Value;
}
