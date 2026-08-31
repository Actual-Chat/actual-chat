using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class VideoCodecNegotiationTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    // Members advertise best-first. H.264 sits below the floor on efficiency, so
    // a set with no ranking always resolved to VP9 and forcing H.264 could not
    // work — the whole point of the ordering.
    [Fact]
    public async Task ShouldRankForcedCodecAheadOfTheFloor()
    {
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var everything = new ApiArray<string>(["av1", "hevc", "vp9", "h264"]);

        // Both unconstrained: AV1 leads, exactly as before.
        await backend.RegisterMember(chatId, bob.Session.Id, everything, CancellationToken.None);
        await backend.RegisterMember(chatId, alice.Session.Id, everything, CancellationToken.None);
        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs[0].Should().Be("av1");

        // Alice forces H.264: she advertises it first, so it leads for everyone.
        await backend.RegisterMember(
            chatId, alice.Session.Id, new ApiArray<string>(["h264", "vp9"]), CancellationToken.None);
        codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs[0].Should().Be("h264");
        codecs.Should().Contain("vp9");
        codecs.Should().NotContain("av1");

        await backend.UnregisterMember(chatId, bob.Session.Id, CancellationToken.None);
        await backend.UnregisterMember(chatId, alice.Session.Id, CancellationToken.None);
    }

    // A member that cannot decode the forced codec keeps it out entirely; the
    // floor is what the call falls back to.
    [Fact]
    public async Task ShouldFallBackToTheFloorWhenAMemberCannotDecodeTheForcedCodec()
    {
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        await backend.RegisterMember(
            chatId, bob.Session.Id, new ApiArray<string>(["h264", "vp9"]), CancellationToken.None);
        await backend.RegisterMember(
            chatId, alice.Session.Id, new ApiArray<string>(["vp9"]), CancellationToken.None);

        var codecs = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().Equal("vp9");

        await backend.UnregisterMember(chatId, bob.Session.Id, CancellationToken.None);
        await backend.UnregisterMember(chatId, alice.Session.Id, CancellationToken.None);
    }
}
