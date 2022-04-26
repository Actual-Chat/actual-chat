namespace ActualChat.Testing;

public static class EnvExt
{
    public static bool IsRunningInContainer()
        => bool.TryParse(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                out var isRunningContainer)
            && isRunningContainer;
}
