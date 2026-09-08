using ActualLab.Rpc;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// The three-way <c>[LegacyName]</c> split of wire name "GetNews" across GetFullNews (v2.12-
/// peers), GetLegacyNews (v2.13..v2.18 peers) and GetNews itself (v2.19+ peers). A wrong band
/// assignment doesn't fail the build - RpcMethodResolver's lowest-MaxVersion tie-break can mask it
/// - so this pins the resolution by test rather than by manual review.
/// </summary>
public class LegacyNewsRoutingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Version LowBoundary = Version.Parse("2.12.9999");
    private static readonly Version HighBoundary = Version.Parse(ApiConstants.LastVersionWithoutUnionTolerance);
    private static readonly Version APreLowBoundaryPeer = new (2, 10);
    private static readonly Version AMidBandPeer = new (2, 15);
    private static readonly Version APostHighBoundaryPeer = new (2, 19);

    [Fact]
    public void APreV212PeerShouldReachGetFullNews()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetFullNews), APreLowBoundaryPeer);

        // assert
        legacyName?.Name.Should().Be(nameof(IChats.GetNews),
            "a v2.10 peer calls the wire name GetNews and must land on GetFullNews");
    }

    [Fact]
    public void APreV212PeerShouldNotReachGetLegacyNews()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetLegacyNews), APreLowBoundaryPeer);

        // assert
        legacyName?.Name.Should().NotBe(nameof(IChats.GetNews),
            "GetLegacyNews must step aside for the older band instead of competing for the wire name");
    }

    [Fact]
    public void AMidBandPeerShouldReachGetLegacyNews()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetLegacyNews), AMidBandPeer);

        // assert
        legacyName?.Name.Should().Be(nameof(IChats.GetNews),
            "a v2.15 peer is past the v2.12 boundary but at or below the union-tolerance one, " +
            "so it must land on GetLegacyNews");
    }

    [Fact]
    public void AMidBandPeerShouldNotReachGetFullNews()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetFullNews), AMidBandPeer);

        // assert
        legacyName.Should().BeNull("GetFullNews only answers to peers at or below its own v2.12 boundary");
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
    public void EachBoundaryShouldBeClaimedByExactlyOneMethod()
    {
        // act
        var methodNames = new[] { nameof(IChats.GetNews), nameof(IChats.GetFullNews), nameof(IChats.GetLegacyNews) };
        var claimantsAtLowBoundary = methodNames.Count(x => LegacyNameOf(x, LowBoundary)?.Name == nameof(IChats.GetNews));
        var claimantsAtHighBoundary = methodNames.Count(x => LegacyNameOf(x, HighBoundary)?.Name == nameof(IChats.GetNews));

        // assert
        claimantsAtLowBoundary.Should().Be(1, "exactly one method may answer to GetNews at the v2.12 boundary");
        claimantsAtHighBoundary.Should().Be(1, "exactly one method may answer to GetNews at the v2.18 boundary");
    }

    // Private methods

    private static LegacyName? LegacyNameOf(string methodName, Version version)
        => new LegacyNames(MethodOf(methodName))[version];

    private static MethodInfo MethodOf(string name)
        => typeof(IChats).GetMethod(name)!;
}
