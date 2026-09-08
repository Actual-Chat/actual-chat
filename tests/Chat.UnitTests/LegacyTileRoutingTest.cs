using ActualLab.Rpc;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// The pair of <c>[LegacyName]</c> attributes that sends a peer too old to read a new entry kind
/// to the filtering tile method. Mistyping one of them fails silently — the old peer keeps
/// getting the unfiltered tile and dies on it.
/// </summary>
public class LegacyTileRoutingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Version Boundary = Version.Parse(ApiConstants.LastVersionWithoutUnionTolerance);
    private static readonly Version JustAfterBoundary = new (2, 19);

    [Fact]
    public void AnOldPeerShouldReachTheFilteringMethod()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetLegacyTile), Boundary);

        // assert
        legacyName?.Name.Should().Be(nameof(IChats.GetTile),
            "a peer at or below the boundary calls the wire name GetTile and must land on the filtering method");
    }

    [Fact]
    public void AnOldPeerShouldNotReachTheUnfilteredMethod()
    {
        // act
        var legacyName = LegacyNameOf(nameof(IChats.GetTile), Boundary);

        // assert
        legacyName?.Name.Should().NotBe(nameof(IChats.GetTile),
            "the unfiltered method must be renamed out of an old peer's reach at the boundary");
    }

    [Fact]
    public void ANewPeerShouldReachTheUnfilteredMethod()
    {
        // act
        var unfiltered = LegacyNameOf(nameof(IChats.GetTile), JustAfterBoundary);
        var filtering = LegacyNameOf(nameof(IChats.GetLegacyTile), JustAfterBoundary);

        // assert
        unfiltered.Should().BeNull("past the boundary GetTile keeps its own name");
        filtering.Should().BeNull("and the filtering method stops answering to it");
    }

    [Fact]
    public void BothSidesOfThePairShouldShareOneBoundary()
    {
        // act
        var versions = new[] { nameof(IChats.GetTile), nameof(IChats.GetLegacyTile) }
            .SelectMany(x => MethodOf(x).GetCustomAttributes<LegacyNameAttribute>())
            .Select(x => x.MaxVersion)
            .Distinct()
            .ToList();

        // assert
        versions.Should().Equal([ApiConstants.LastVersionWithoutUnionTolerance],
            "a split boundary routes one of the two methods to the wrong peers");
    }

    [Fact]
    public void ThePairShouldStayInterchangeable()
    {
        // act
        var parameters = new[] { nameof(IChats.GetTile), nameof(IChats.GetLegacyTile) }
            .Select(x => MethodOf(x).GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        // assert
        parameters[0].Should().Equal(parameters[1],
            "an old peer's call is decoded against whichever method it lands on");
        MethodOf(nameof(IChats.GetTile)).ReturnType
            .Should().Be(MethodOf(nameof(IChats.GetLegacyTile)).ReturnType);
    }

    [Fact]
    public void CallEntryShouldBeFilteredForAPreTolerancePeer()
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var entry = new CallEntry(ChatEntryId.New(chatId, 1)) {
            CallerId = AuthorId.New(chatId, 1),
            Outcome = CallOutcome.NoAnswer,
        };

        // act
        var knownToOldPeer = ChatEntry.IsKnownTo(entry, new Version(2, 18));
        var knownToNewPeer = ChatEntry.IsKnownTo(entry, new Version(2, 19));

        // assert
        knownToOldPeer.Should().BeFalse("a peer below the release that declared this union tag can't read it");
        knownToNewPeer.Should().BeTrue("a peer at the declared release can read the tag");
    }

    // Private methods

    private static LegacyName? LegacyNameOf(string methodName, Version version)
        => new LegacyNames(MethodOf(methodName))[version];

    private static MethodInfo MethodOf(string name)
        => typeof(IChats).GetMethod(name)!;
}
