namespace ActualChat;

/// <summary>
/// Core application constants and configuration values.
/// </summary>
public static partial class CoreConstants
{
    public const string AppName = "Voxt";
    public static readonly string Copyright = $"© 2022–{Moment.Now.ToDateTimeOffset().Year} Actual Chat, Inc. All rights reserved.";

    public static class Hosts
    {
        public const string Prod = "voxt.ai"; // NOTE: keep lowercase
        public const string Dev = $"dev.{Prod}";
        public const string Local = $"local.{Prod}";
        // Suffix for wildcard matching (used for worktree subdomains like wt1.local.voxt.ai)
        public const string LocalSuffix = $".{Local}";
    }

    public static class AsyncMemoizer
    {
        public static readonly int TargetQueueSize = 16;
    }

    public static class MessageProcessor
    {
        public static readonly int QueueSize = 128;
        public static readonly TimeSpan ProcessCallTimeout = TimeSpan.FromSeconds(2);
    }
}
