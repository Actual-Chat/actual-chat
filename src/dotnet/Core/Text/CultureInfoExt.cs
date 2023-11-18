namespace ActualChat;

public static class Cultures
{
    private static CultureInfo? _us;

    public static CultureInfo US => _us ??= CultureInfo.GetCultureInfo("en-US");
}
