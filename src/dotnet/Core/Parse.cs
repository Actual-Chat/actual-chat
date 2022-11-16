namespace ActualChat;

// This type is used as an extra parameter of constructors to indicate no validation is required

public static class Parse
{
    public static readonly SkipParseTag None = default;
    public static readonly ParseOrDefaultTag OrDefault = default;
}

public readonly struct SkipParseTag { }
public readonly struct ParseOrDefaultTag { }
