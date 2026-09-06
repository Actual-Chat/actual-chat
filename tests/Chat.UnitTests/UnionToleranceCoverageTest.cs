using ActualChat.Serialization;
using ActualChat.Serialization.Internal;
using MessagePack;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// A new <c>[Union]</c> hierarchy that ships closed makes its first added member a compatibility
/// event for every peer. This is what notices, before a release does.
/// </summary>
public class UnionToleranceCoverageTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Dictionary<Type, string> Exempt = new() {
        [typeof(StoredSettings)] =
            "Tolerance alone doesn't fix its bug - an unreadable row is dropped on write-back, "
            + "which is a change to the settings write path. Its own task.",
        [typeof(Live.MuxedAudioStreamItem)] =
            "A live stream item has no placeholder that means less than a throw: the demuxer keys "
            + "off StreamIndex, so a null in its place is a different failure, not a lesser one. "
            + "Tolerating one needs a skip path through the demuxer, which is its own design.",
        [typeof(Media.MediaFrame)] =
            "Same as MuxedAudioStreamItem - a frame the pipeline can't decode isn't a frame it can "
            + "carry as null past the timestamping and A/V sync that read it.",
    };

    [Fact]
    public void EveryUnionRootShouldBeTolerantOrExempt()
    {
        // arrange
        ApiModuleInitializerLoad();
        var roots = ApiAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttributes<UnionAttribute>().Any())
            .OrderBy(t => t.FullName)
            .ToList();

        // act
        var untreated = roots
            .Where(t => !Exempt.ContainsKey(t) && !IsRegisteredAsTolerant(t))
            .Select(t => t.GetName())
            .ToList();

        // assert
        roots.Should().NotBeEmpty("the scan must actually find the union roots");
        Out.WriteLine($"Union roots: {roots.Count}, exempt: {Exempt.Count}");
        untreated.Should().BeEmpty(
            "every [Union] root either tolerates an unknown member or is listed in Exempt with a "
            + "reason:\n{0}",
            string.Join("\n", untreated));
    }

    [Fact]
    public void ExemptRootsShouldStillBeUnionRoots()
    {
        // act
        var stale = Exempt.Keys
            .Where(t => !t.GetCustomAttributes<UnionAttribute>().Any())
            .Select(t => t.GetName())
            .ToList();

        // assert
        stale.Should().BeEmpty("an exemption for a type that is no longer a union root is dead weight");
    }

    // Private methods

    private static void ApiModuleInitializerLoad()
        // Touching an Api type runs its module initializer, which is what registers the formatters.
        => _ = ChatEntry.Removed(ChatEntryId.New(ChatId.Parse("the-actual-one"), 1));

    private static IEnumerable<Assembly> ApiAssemblies()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("ActualChat.") == true);

    private static bool IsRegisteredAsTolerant(Type root)
        => AppMessagePackResolverSettings.Formatters.TryGetValue(root, out var formatter)
            && formatter.IsGenericType
            && formatter.GetGenericTypeDefinition() == typeof(ForwardCompatibleUnionFormatter<>)
            && typeof(IForwardCompatibleUnion<>).MakeGenericType(root).IsAssignableFrom(root);
}
