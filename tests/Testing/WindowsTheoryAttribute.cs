namespace ActualChat.Testing;

/// <summary>
/// A Theory that is skipped everywhere but Windows.
/// </summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute(string reason)
        => Skip = OperatingSystem.IsWindows() ? null : reason;
}
