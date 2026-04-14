using ActualChat.Contacts;
using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class ChatMentionSearchTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindByAvatarName()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var (chat, inviteId) = await Tester.CreateAndGetChat(false, "mention-test-avatar");
        await using var __ = await Tester.BackupAuth();
        var alice = await Tester.SignInAsNew("Magnolia");
        await Tester.JoinChat(chat.Id, inviteId);
        await Tester.SignIn(bob);

        var searchProvider = GetMentionSearchProvider(chat.Id);

        // act
        var results = await searchProvider.Find("Magn", 10, CancellationToken.None);

        // assert
        results.Should().ContainSingle()
            .Which.SearchMatch.Text.Should().Be("Magnolia");
    }

    [Fact]
    public async Task ShouldFindByContactDisplayName()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var (chat, inviteId) = await Tester.CreateAndGetChat(false, "mention-test-contact");
        await using var __ = await Tester.BackupAuth();
        var alice = await Tester.SignInAsNew("Magnolia");
        await Tester.JoinChat(chat.Id, inviteId);
        await Tester.SignIn(bob);

        // Bob renames Alice's contact
        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        var contact = new Contact(contactId) { PeerContactName = "MyBestie" };
        await Tester.Commander.Call(new Contacts_Change(Tester.Session, contactId, null, Change.Create(contact)));

        var searchProvider = GetMentionSearchProvider(chat.Id);

        // act - search by the contact display name Bob gave Alice
        var results = await searchProvider.Find("MyBest", 10, CancellationToken.None);

        // assert - top result should be the renamed contact
        results.Should().NotBeEmpty();
        results.First().SearchMatch.Text.Should().Be("MyBestie");
    }

    [Fact]
    public async Task ShouldFindByAllFields()
    {
        // arrange - Alice has:
        //   avatar name (Account.Avatar.Name): "Magnolia"
        //   contact display name (PeerRename by Bob): "MyBestie"
        var bob = await Tester.SignInAsUniqueBob();
        var (chat, inviteId) = await Tester.CreateAndGetChat(false, "mention-test-all-fields");
        await using var __ = await Tester.BackupAuth();
        var alice = await Tester.SignInAsNew("Magnolia");
        await Tester.JoinChat(chat.Id, inviteId);
        await Tester.SignIn(bob);

        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        var contact = new Contact(contactId) { PeerContactName = "MyBestie" };
        await Tester.Commander.Call(new Contacts_Change(Tester.Session, contactId, null, Change.Create(contact)));

        var searchProvider = GetMentionSearchProvider(chat.Id);

        // act & assert - should find by account avatar name even when contact has been renamed
        var byAvatar = await searchProvider.Find("Magnol", 10, CancellationToken.None);
        byAvatar.Should().Contain(x => x.SearchMatch.Text == "Magnolia",
            "account avatar name should remain searchable even when contact display name is set");

        // act & assert - should find by contact display name
        var byContact = await searchProvider.Find("MyBest", 10, CancellationToken.None);
        byContact.Should().NotBeEmpty("contact display name should be searchable");
        byContact.First().SearchMatch.Text.Should().Be("MyBestie");
    }

    [Fact]
    public async Task ShouldReturnAllMembersOnEmptyFilter()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var (chat, inviteId) = await Tester.CreateAndGetChat(false, "mention-test-all");
        await using var __ = await Tester.BackupAuth();
        await Tester.SignInAsNew("Magnolia");
        await Tester.JoinChat(chat.Id, inviteId);
        await Tester.SignIn(bob);

        var searchProvider = GetMentionSearchProvider(chat.Id);

        // act
        var results = await searchProvider.Find("", 10, CancellationToken.None);

        // assert - should include both Bob and the other user
        results.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    private ISearchProvider<MentionSearchResult> GetMentionSearchProvider(ChatId chatId)
    {
        var hub = Tester.ScopedAppServices.AppUIHub();
        var markupHub = hub.ChatMarkupHubFactory[chatId];
        return markupHub.MentionSearchProvider;
    }
}
