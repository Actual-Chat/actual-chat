namespace ActualChat.Testing;

/// <summary>
/// A Fact that is skipped everywhere but Windows.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
        => Skip = OperatingSystem.IsWindows() ? null : reason;
}
