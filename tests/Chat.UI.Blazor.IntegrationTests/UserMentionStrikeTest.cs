using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class UserMentionStrikeTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task MemberUserMentionIsNotStruckThrough()
    {
        // arrange: a public chat owned by Bob; Alice joins (member), Carol stays a non-member.
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(true, "user-mention-strike-test");

        AccountFull alice, carol;
        await using (await Tester.BackupAuth()) {
            alice = await Tester.SignInAsNew("Alice");
            await Tester.JoinChat(chat.Id, Symbol.Empty);
            carol = await Tester.SignInAsNew("Carol"); // account exists, never joins this chat
        }

        var services = Tester.ScopedAppServices;
        var session = Tester.Session;

        // Root cause: a regular message's markup is rendered un-enriched, so the IsChatMember
        // flag a user-mention carries is false even when the mentioned user IS a member -
        // which is what made every user mention render struck through.
        var parser = services.GetRequiredService<IMarkupParser>();
        var markup = parser.Parse(new UserMention(MentionRef.NewUser(alice.Id), "Alice").Format());
        var mention = FindUserMention(markup);
        mention.Should().NotBeNull();
        mention!.IsChatMember.Should().BeFalse();

        // Fix: the strike decision is resolved per-user, the way UserMentionView now resolves it -
        // independent of markup enrichment and of the SeeMembers permission.
        var authors = services.GetRequiredService<IAuthors>();
        (await authors.IsUserChatMember(session, chat.Id, alice.Id, CancellationToken.None))
            .Should().BeTrue("Alice is a member and must not be struck through");
        (await authors.IsUserChatMember(session, chat.Id, carol.Id, CancellationToken.None))
            .Should().BeFalse("Carol is not a member of this chat");
    }

    private static UserMention? FindUserMention(Markup markup)
        => markup switch {
            UserMention um => um,
            MarkupSeq seq => seq.Items.Select(FindUserMention).FirstOrDefault(x => x is not null),
            ParagraphMarkup p => FindUserMention(p.Content),
            _ => null,
        };
}
