namespace ActualChat.Testing;

public static class EnvExt
{
    public static bool IsRunningInContainer()
    {
        // When running in Claude's Docker (AC_OS="Linux in Docker"), use regular localhost config
        // because --network host makes localhost = host
        if (Environment.GetEnvironmentVariable("AC_OS") == "Linux in Docker")
            return false;

        return bool.TryParse(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                out var isRunningContainer)
            && isRunningContainer;
    }

    // "Agent mode" enables console logging for tests, making logs visible in terminal.
    // Detected by presence of AC_OS environment variable (set by Claude Launcher).
    public static bool IsAgentMode()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AC_OS"));
}
