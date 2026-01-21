namespace ActualChat.Testing;

public static class EnvExt
{
    public static bool IsRunningInContainer()
        => bool.TryParse(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                out var isRunningContainer)
            && isRunningContainer;

    // "Claude mode" enables console logging for tests, making logs visible in terminal.
    // Set CLAUDE_MODE=1 or CLAUDE_MODE=true to enable.
    public static bool IsClaudeMode()
        => bool.TryParse(Environment.GetEnvironmentVariable("CLAUDE_MODE"), out var isClaudeMode)
            && isClaudeMode
            || Environment.GetEnvironmentVariable("CLAUDE_MODE") == "1";
}
