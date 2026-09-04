namespace ActualChat.Chat.UI.Blazor.UnitTests;

/// <summary>
/// Locates the repository checkout a test runs from, for tests that read source files.
/// </summary>
public static class TestRepository
{
    public static DirectoryInfo Root => field ??= FindRoot();
    private static DirectoryInfo FindRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ActualChat.sln")))
            root = root.Parent;

        return root ?? throw new InvalidOperationException(
            "The test must run from inside the repository to read its sources.");
    }
}
