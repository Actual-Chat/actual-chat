namespace ActualChat.Chat;

// A parse is synchronous and runs on one thread, so the budget is a thread-static counter: Pidgin's
// parse state has no slot for it. The nested parses a document makes (paragraph and quote bodies,
// table cells) draw from the budget of the parse that started them, since they are part of it.

/// <summary>
/// Bounds how many alternatives one <see cref="MarkupParser"/> parse may try;
/// <see cref="SafeOneOfParser{T}"/> spends a step per invocation.
/// </summary>
internal static class ParseBudget
{
    [ThreadStatic]
    private static int _remaining;

    public static int Remaining => _remaining;

    public static void Reset(int steps)
        => _remaining = steps;

    public static void Spend()
    {
        if (--_remaining < 0)
            throw new ParseBudgetExceededException();
    }
}

internal sealed class ParseBudgetExceededException : Exception
{
    private const string DefaultMessage = "Markup parse budget exceeded.";

    public ParseBudgetExceededException() : base(DefaultMessage) { }
    public ParseBudgetExceededException(string? message) : base(message ?? DefaultMessage) { }
    public ParseBudgetExceededException(string? message, Exception? innerException)
        : base(message ?? DefaultMessage, innerException) { }
}
