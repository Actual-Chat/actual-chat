using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class LocalSearchUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindSystemChatsByLocalizedTitle()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.AppServices.UserSettingsUI(Tester.Session)
            .UserLanguageSettings()
            .Set(new UserLanguageSettings { UILanguage = Languages.Russian });
        var notesChat = await CreateSystemChat(Constants.Chat.System.Notes, null);
        var place = await Tester.CreatePlace(false, "local-search-place");
        var welcomeChat = await CreateSystemChat(Constants.Chat.System.Welcome, place.Id);
        notesChat.Title.Should().Be("Заметки", "Chats.Get names a system chat in the reader's language");

        // act, assert
        var found = await Find(SearchScope.Groups, null, "Заметки", 1);
        found[0].ContactId.Should().Be(ContactId.NewAny(bob.Id, notesChat.Id));
        found[0].Match.Text.Should().Be("Заметки");

        found = await Find(SearchScope.Groups, place.Id, "Добро", 1);
        found[0].ContactId.Should().Be(ContactId.NewAny(bob.Id, welcomeChat.Id));
        found[0].Match.Text.Should().Be("Добро пожаловать");

        found = await Find(SearchScope.Groups, null, "Notes", 0);
        found.Should().BeEmpty("the local candidates carry the localized title only, by design");
    }

    private async Task<Chat> CreateSystemChat(Constants.Chat.SystemChat systemChat, PlaceId? placeId)
    {
        var chat = await Tester.Commander.Call(new Chats_Change {
            Session = Tester.Session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = systemChat.DefaultTitle,
                    Kind = placeId is null ? ChatKind.Group : null,
                    IsPublic = placeId is not null,
                    PlaceId = placeId,
                    SystemTag = systemChat.Tag,
                },
            },
        }).Require();
        return await Tester.Chats.Get(Tester.Session, chat.Id, CancellationToken.None).Require();
    }

    // The contact behind a new chat lands through queued events, so the candidate list fills in after creation
    private Task<IReadOnlyList<FoundContact>> Find(
        SearchScope scope,
        PlaceId? placeId,
        string criteria,
        int expectedCount)
        => TestsExt.When(async () => {
            var localSearch = Tester.ScopedAppServices.GetRequiredService<LocalSearchUI>();
            var found = await localSearch.FindContacts(scope, placeId, criteria, 10, CancellationToken.None);
            found.Should().HaveCount(expectedCount);
            return found;
        }, TimeSpan.FromSeconds(20));
}
