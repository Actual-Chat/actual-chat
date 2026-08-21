using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PeerChatPropertiesTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task EitherPeerCanToggleSummarize()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        await bobTester.CreatePeerContact(bob, alice);

        var peerChatId = (ChatId)PeerChatId.New(alice.Id, bob.Id);
        var chats = bobTester.AppServices.GetRequiredService<IChats>();
        CancellationToken cancellationToken = default;
        await aliceTester.Commander.Call(
            new Chats_UpsertEntry {
                Session = aliceTester.Session,
                ChatId = peerChatId,
                LocalId = null,
                Text = "Hello!",
            },
            cancellationToken);

        // act
        var afterAlice = await aliceTester.Commander.Call(
            new Chats_Change {
                Session = aliceTester.Session,
                ChatId = peerChatId,
                ExpectedVersion = null,
                Change = Change.Update(new ChatDiff { IsSummarized = true }),
            },
            cancellationToken);
        var afterBob = await bobTester.Commander.Call(
            new Chats_Change {
                Session = bobTester.Session,
                ChatId = peerChatId,
                ExpectedVersion = null,
                Change = Change.Update(new ChatDiff { IsSummarized = false }),
            },
            cancellationToken);

        // assert
        afterAlice.IsSummarized.Should().BeTrue();
        afterBob.IsSummarized.Should().BeFalse();
        await TestExt.When(async () => {
            var chat = await chats.Get(bobTester.Session, peerChatId, cancellationToken).Require();
            chat.IsSummarized.Should().BeFalse();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PeerChatRejectsOtherProperties()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        await bobTester.CreatePeerContact(bob, alice);

        var peerChatId = (ChatId)PeerChatId.New(alice.Id, bob.Id);
        CancellationToken cancellationToken = default;
        await aliceTester.Commander.Call(
            new Chats_UpsertEntry {
                Session = aliceTester.Session,
                ChatId = peerChatId,
                LocalId = null,
                Text = "Hello!",
            },
            cancellationToken);

        // act
        var rename = () => aliceTester.Commander.Call(
            new Chats_Change {
                Session = aliceTester.Session,
                ChatId = peerChatId,
                ExpectedVersion = null,
                Change = Change.Update(new ChatDiff { Title = "renamed" }),
            },
            cancellationToken);
        var archive = () => aliceTester.Commander.Call(
            new Chats_Change {
                Session = aliceTester.Session,
                ChatId = peerChatId,
                ExpectedVersion = null,
                Change = Change.Update(new ChatDiff { IsArchived = true }),
            },
            cancellationToken);
        var remove = () => aliceTester.Commander.Call(
            new Chats_Change {
                Session = aliceTester.Session,
                ChatId = peerChatId,
                ExpectedVersion = null,
                Change = Change.Remove(new ChatDiff()),
            },
            cancellationToken);

        // assert
        await rename.Should().ThrowAsync<Exception>();
        await archive.Should().ThrowAsync<Exception>();
        await remove.Should().ThrowAsync<Exception>();
    }
}
