using ActualLab.IO;

namespace ActualChat.Testing;

// The name is intentionally different from TestExt, which is used in some tests
public static class UnitTestExt
{
    public static ITest Require(this ITest? test)
        => test ?? throw StandardError.Internal("Failed to extract test info.");

    public static string GetInstanceName(this ITest test)
    {
        // DisplayName:
        // - Build server: DisplayName is generated based on class full name and method name.
        // - Rider: DisplayName uses only method name.
        var displayName = test.DisplayName;
        // We drop the namespace to have a more readable instance name
        // (with test method name) after the length is truncated.
        var ns = test.TestCase.TestMethod.TestClass.Class.ToRuntimeType().Namespace;
        if (displayName.OrdinalStartsWith(ns))
            displayName = displayName[(ns.Length + 1)..];
        // Postgres identifier size limit: 63 characters
        return FilePath.GetHashedName(test.TestCase.UniqueID, displayName, maxLength: 32);
    }
}
