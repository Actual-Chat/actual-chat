using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class VideoCodecNegotiationTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private const string Forced = LiveVideoBackend.ChatState.ForcedCodecMarker;
    private static readonly ApiArray<string> Everything = new(["av1", "hevc", "vp9", "h264"]);

    [Fact]
    public async Task ShouldUseFloorCodecUntilAViewerRegisters()
    {
        var (chatId, backend, _, _) = await Setup();

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().Equal(LiveVideoBackend.ChatState.FloorCodec);
    }

    [Fact]
    public async Task ShouldIntersectCapabilitiesWhenNobodyOverrides()
    {
        var (chatId, backend, bob, alice) = await Setup();

        await RegisterViewer(backend, chatId, bob, Everything);
        await RegisterViewer(backend, chatId, alice, new ApiArray<string>(["vp9", "h264"]));

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().NotContain("av1");
        codecs.Should().NotContain("hevc");
        codecs.Should().Contain("vp9");
        codecs.Should().Contain("h264");
    }

    // The whole point of the marker: an admin's list replaces the negotiation
    // instead of joining it, so no floor is added and nothing is intersected.
    [Fact]
    public async Task ShouldLetOneAdminPinTheCallToASingleCodec()
    {
        var (chatId, backend, bob, alice) = await Setup();

        await RegisterViewer(backend, chatId, bob, Everything);
        await backend.RegisterMember(
            chatId, alice, new ApiArray<string>([Forced, "h264"]), true, CancellationToken.None);

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().Equal("h264");
    }

    // Two admins each pinning their own codec are both asking for theirs to be
    // usable; intersecting would leave nothing, so the picks are unioned.
    [Fact]
    public async Task ShouldUnionThePicksOfSeveralAdmins()
    {
        var (chatId, backend, bob, alice) = await Setup();

        await backend.RegisterMember(
            chatId, bob, new ApiArray<string>([Forced, "h264"]), true, CancellationToken.None);
        await backend.RegisterMember(
            chatId, alice, new ApiArray<string>([Forced, "av1"]), true, CancellationToken.None);

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().BeEquivalentTo(["av1", "h264"]);
        codecs.Should().NotContain("vp9");
    }

    // The marker is an admin power. From anyone else it carries no authority:
    // the rest of the list is still read as an honest capability report.
    [Fact]
    public async Task ShouldIgnoreTheMarkerFromANonAdmin()
    {
        var (chatId, backend, bob, alice) = await Setup();

        await RegisterViewer(backend, chatId, bob, Everything);
        await RegisterViewer(backend, chatId, alice, new ApiArray<string>([Forced, "h264"]));

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().Contain("vp9"); // floor still added
        codecs.Should().NotContain(Forced);
        codecs.Should().NotContain("av1"); // alice did not advertise it
    }

    // Every member re-registers on a heartbeat; with several clients that is an
    // invalidation every couple of seconds, each one an RPC result and a JS interop
    // call on every recording client, for a list that did not change.
    [Fact]
    public async Task HeartbeatShouldNotInvalidateAnUnchangedCodecList()
    {
        var (chatId, backend, bob, alice) = await Setup();
        await RegisterViewer(backend, chatId, bob, Everything);
        await RegisterViewer(backend, chatId, alice, Everything);
        var codecs = await Computed.Capture(() => backend.GetSupportedCodecs(chatId, CancellationToken.None));
        var memberCount = await Computed.Capture(
            () => backend.GetVideoStreamMemberCount(chatId, CancellationToken.None));

        await RegisterViewer(backend, chatId, bob, Everything);
        await RegisterViewer(backend, chatId, alice, Everything);

        codecs.IsConsistent().Should().BeTrue("re-registering the same codecs changes nothing");
        memberCount.IsConsistent().Should().BeTrue("re-registering an existing member changes nothing");
    }

    [Fact]
    public async Task LeavingForcedFloorShouldUpgradeImmediately()
    {
        // arrange
        var (chatId, backend, bob, alice) = await Setup();
        await RegisterViewer(backend, chatId, bob, Everything);
        await backend.UnregisterMember(chatId, bob, CancellationToken.None);
        var floor = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        floor.Should().Equal(LiveVideoBackend.ChatState.FloorCodec);
        await backend.RegisterMember(chatId, bob, new ApiArray<string>([Forced, "vp9"]), true, CancellationToken.None);
        await RegisterViewer(backend, chatId, alice, Everything);

        // act
        await backend.RegisterMember(chatId, bob, Everything, true, CancellationToken.None);
        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);

        // assert
        codecs.Should().BeEquivalentTo(Everything, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ChangedCapabilitiesShouldStillInvalidateTheCodecList()
    {
        var (chatId, backend, bob, alice) = await Setup();
        await RegisterViewer(backend, chatId, bob, Everything);
        await RegisterViewer(backend, chatId, alice, Everything);
        var codecs = await Computed.Capture(() => backend.GetSupportedCodecs(chatId, CancellationToken.None));

        await RegisterViewer(backend, chatId, alice, new ApiArray<string>(["vp9", "h264"]));

        codecs.IsConsistent().Should().BeFalse("alice can no longer decode av1/hevc");
        var updated = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        updated.Should().NotContain("av1");
    }

    [Fact]
    public async Task PeerChatShouldAllowHardwareCodecsBeforeAStreamExists()
    {
        // arrange
        var (_, backend, bob, alice) = await Setup();
        var chatId = PeerChatId.New(UserId.New(), UserId.New());

        // act
        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
        await backend.RegisterMember(chatId, alice, Everything, false, CancellationToken.None);
        await backend.UnregisterMember(chatId, alice, CancellationToken.None);
        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);

        // assert
        codecs.Should().Equal(Everything,
            "capability-aware reception protects late joiners without requiring a software VP9 bootstrap");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadyAudienceShouldUpgradeAndDowngrade(bool isPeer)
    {
        // arrange
        var (groupId, backend, bob, alice) = await Setup();
        ChatId chatId = isPeer ? PeerChatId.New(UserId.New(), UserId.New()) : groupId;
        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
        (await backend.GetSupportedCodecs(chatId, CancellationToken.None)).Should().Equal(Everything);

        // act
        await RegisterViewer(backend, chatId, alice, Everything);
        (await backend.GetSupportedCodecs(chatId, CancellationToken.None)).Should().Equal(Everything);
        await RegisterViewer(backend, chatId, alice, new ApiArray<string>(["vp9", "h264"]));
        (await backend.GetSupportedCodecs(chatId, CancellationToken.None)).Should().Equal("vp9", "h264");
        await RegisterViewer(backend, chatId, alice, Everything);

        // assert
        (await backend.GetSupportedCodecs(chatId, CancellationToken.None)).Should().Equal("vp9", "h264");
        await TestWait.When(async ct =>
            (await backend.GetSupportedCodecs(chatId, ct)).Should().Equal(Everything), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task ObsoleteReceiverReleaseShouldNotRemoveReplacement()
    {
        // arrange
        var (chatId, backend, bob, _) = await Setup();
        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
        await RegisterViewer(backend, chatId, "old", Everything);
        await RegisterViewer(backend, chatId, "new", new ApiArray<string>(["vp9", "h264"]));

        // act
        await backend.UnregisterMember(chatId, "old", CancellationToken.None);
        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);

        // assert
        codecs.Should().Equal("vp9", "h264");
    }

    [Fact]
    public async Task ReceiverLeasesShouldNotDoubleCountSessions()
    {
        // arrange
        var (chatId, backend, bob, alice) = await Setup();
        var nodeRef = AppHost.Services.MeshWatcher().ThisNode.Ref;
        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
        await backend.RegisterReceiver(chatId, "first", alice, Everything, nodeRef, CancellationToken.None);
        await backend.RegisterReceiver(chatId, "second", alice, Everything, nodeRef, CancellationToken.None);

        // act
        var count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);

        // assert
        count.Should().Be(2, "a camera and a screen subscription from one session are still one member");
    }

    [Fact]
    public async Task PeerChatShouldHonorAdminOverride()
    {
        // arrange
        var (_, backend, bob, _) = await Setup();
        var chatId = PeerChatId.New(UserId.New(), UserId.New());

        // act
        await backend.RegisterMember(chatId, bob, new ApiArray<string>([Forced, "hevc"]), true, CancellationToken.None);
        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);

        // assert
        codecs.Should().Equal("hevc");
    }

    private Task RegisterViewer(
        ILiveVideoBackend backend, ChatId chatId, string receiverId, ApiArray<string> codecs)
        => backend.RegisterReceiver(
            chatId, receiverId, receiverId, codecs,
            AppHost.Services.MeshWatcher().ThisNode.Ref, CancellationToken.None);

    private async Task<(ChatId ChatId, ILiveVideoBackend Backend, string Bob, string Alice)> Setup()
    {
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        return (chatId, backend, bob.Session.Id, alice.Session.Id);
    }
}
