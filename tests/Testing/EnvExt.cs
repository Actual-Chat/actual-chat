namespace ActualChat.Testing;

public static class EnvExt
{
    public static bool IsRunningInContainer()
        => !IsAgentMode() // We don't trigger docker container mode for agents
            && bool.TryParse(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), out var v)
            && v;

    // "Agent mode" enables console logging for tests, making logs visible in terminal.
    // Detected by presence of AC_OS environment variable (set by Claude Launcher).
    public static bool IsAgentMode()
        => !Environment.GetEnvironmentVariable("AC_OS").IsNullOrEmpty();
}
