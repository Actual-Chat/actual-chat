using ActualLab.Rpc;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// The <c>[LegacyName]</c> split of wire name "GetNews" between GetLegacyNews (v2.19- peers) and
/// GetNews itself (v2.20+ peers). A wrong band assignment doesn't fail the build -
/// RpcMethodResolver's lowest-MaxVersion tie-break can mask it - so this pins the resolution by
/// test rather than by manual review.
/// </summary>
public class LegacyNewsRoutingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Version HighBoundary = Version.Parse(ApiConstants.LastVersionWithoutUnionTolerance);
    private static readonly Version AMidBandPeer = new (2, 15);
    // Derived, not written out: a hardcoded copy of the boundary is exactly what went stale once,
    // when 2.19 shipped without the tolerant formatter and the band still called it tolerant.
    private static readonly Version APostHighBoundaryPeer = new (HighBoundary.Major, HighBoundary.Minor + 1);

    [Fact]
    public void AMidBandPeerShouldReachGetLegacyNews()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetLegacyNews), AMidBandPeer);

        // assert
        legacyName?.Name.Should().Be(nameof(IChats.GetNews),
            "a v2.15 peer is at or below the union-tolerance boundary, so it must land on GetLegacyNews");
    }

    [Fact]
    public void APostV218PeerShouldReachTheCurrentGetNews()
    {
        // act
        var ownRedirect = LegacyNameOf(nameof(IChats.GetNews), APostHighBoundaryPeer);
        var legacyRedirect = LegacyNameOf(nameof(IChats.GetLegacyNews), APostHighBoundaryPeer);

        // assert
        ownRedirect.Should().BeNull("past both boundaries GetNews keeps its own name");
        legacyRedirect.Should().BeNull("and GetLegacyNews stops answering to it");
    }

    [Fact]
    public void TheBoundaryShouldBeClaimedByExactlyOneMethod()
    {
        // act
        var methodNames = new[] { nameof(IChats.GetNews), nameof(IChats.GetLegacyNews) };
        var claimantsAtBoundary = methodNames.Count(x => LegacyNameOf(x, HighBoundary)?.Name == nameof(IChats.GetNews));

        // assert
        claimantsAtBoundary.Should().Be(1, "exactly one method may answer to GetNews at the v2.19 boundary");
    }

    // Private methods

    private static LegacyName? LegacyNameOf(string methodName, Version version)
        => new LegacyNames(MethodOf(methodName))[version];

    private static MethodInfo MethodOf(string name)
        => typeof(IChats).GetMethod(name)!;
}
