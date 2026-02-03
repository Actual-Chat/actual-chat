namespace ActualChat.Testing;

#pragma warning disable CA1019 // Define accessors for attribute arguments

/// <summary>
/// A Theory that is skipped when running on the build server.
/// Use this for tests that require direct DB manipulation or other operations
/// that can cause issues in the CI environment.
/// </summary>
public sealed class LocalTheoryAttribute : TheoryAttribute
{
    public LocalTheoryAttribute(string reason)
        => Skip = TestRunnerInfo.IsBuildAgent() ? reason : null;
}
