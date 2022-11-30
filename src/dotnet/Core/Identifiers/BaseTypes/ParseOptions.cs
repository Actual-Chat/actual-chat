namespace ActualChat;

// This type is used as an extra parameter of constructors to indicate no validation is required

public static class ParseOptions
{
    public static readonly SkipParseOption Skip = default;
    public static readonly ParseOrNoneOption OrNone = default;
}

public readonly struct SkipParseOption { }
public readonly struct ParseOrNoneOption { }
