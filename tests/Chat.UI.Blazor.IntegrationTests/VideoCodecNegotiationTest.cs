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
    public async Task ShouldIntersectCapabilitiesWhenNobodyOverrides()
    {
        var (chatId, backend, bob, alice) = await Setup();

        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
        await backend.RegisterMember(chatId, alice, new ApiArray<string>(["vp9", "h264"]), false, CancellationToken.None);

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

        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
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

        await backend.RegisterMember(chatId, bob, Everything, false, CancellationToken.None);
        await backend.RegisterMember(
            chatId, alice, new ApiArray<string>([Forced, "h264"]), false, CancellationToken.None);

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().Contain("vp9"); // floor still added
        codecs.Should().NotContain(Forced);
        codecs.Should().NotContain("av1"); // alice did not advertise it
    }

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
