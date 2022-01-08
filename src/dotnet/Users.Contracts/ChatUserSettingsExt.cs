namespace ActualChat.Users;

public static class ChatUserSettingsExt
{
    public static LanguageId LanguageOrDefault(this ChatUserSettings? settings)
        => settings?.Language.ValidOrDefault() ?? LanguageId.Default.Value;
}
