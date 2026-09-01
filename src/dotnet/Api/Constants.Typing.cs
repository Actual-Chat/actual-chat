namespace ActualChat;

public static partial class Constants
{
    public static class Typing
    {
        // An author is shown as typing for this long after their last change. The client tracks its
        // own lease and renews it with whatever is left of this window, so the streak ends exactly
        // MaxTtl after the last change; the server clamps anything longer.
        public const double MaxTtlSeconds = 5;
        public static readonly TimeSpan MaxTtl = TimeSpan.FromSeconds(MaxTtlSeconds);
    }
}
