using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class SystemChatTitleLocalizationTest
{
    [Fact]
    public void EnglishCatalogShouldMatchTheDefaultTitles()
    {
        // A chat is stored with the Api's English title and shown under the catalog's, so the two
        // must agree - or an English reader would see one name in the list and another after a reload.

        // arrange
        var l = NewLocalizer(Languages.English);

        // act
        var mismatches = SystemChats()
            .Select(chat => (Localized: l.GetSystemChatTitle(chat), chat.Title))
            .Where(x => x.Localized != x.Title)
            .Select(x => $"'{x.Localized}' != '{x.Title}'")
            .ToList();

        // assert
        mismatches.Should().BeEmpty(
            "the English catalog must name every system chat exactly as the Api does:\n{0}",
            string.Join("\n", mismatches));
    }

    [Fact]
    public void EveryShippedLanguageShouldNameEverySystemChat()
    {
        // arrange
        var errors = new List<string>();

        // act
        foreach (var language in ShippedLanguages()) {
            var l = NewLocalizer(language);
            foreach (var chat in SystemChats()) {
                var title = l.GetSystemChatTitle(chat);
                if (title.IsNullOrEmpty())
                    errors.Add($"'{language.IsoCode}' names nothing for '{chat.Title}'");
                else if (title.Contains("SystemChat_") || title.Contains("Onboarding_"))
                    errors.Add($"'{language.IsoCode}' leaves a key unresolved: '{title}'");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "every system chat must have a name in every shipped language:\n{0}", string.Join("\n", errors));
    }

    [Fact]
    public void RenamedSystemChatShouldKeepItsName()
    {
        // arrange
        var l = NewLocalizer(Languages.Russian);
        var renamed = SystemChats().Select(chat => chat with { Title = "My " + chat.Title }).ToList();

        // act
        var titles = renamed.Select(chat => l.LocalizeTitle(chat).Title);

        // assert
        titles.Should().Equal(renamed.Select(chat => chat.Title), "a title the owner chose is not translated");
    }

    [Fact]
    public void OrdinaryChatShouldKeepItsName()
    {
        // arrange
        var l = NewLocalizer(Languages.Russian);
        var chat = new Chat(GroupChatId.New()) { Title = Constants.Chat.System.Notes.DefaultTitle };

        // act
        var localized = l.LocalizeTitle(chat);

        // assert
        localized.Should().BeSameAs(chat, "a chat merely called 'Notes' is not the Notes chat");
    }

    // Private methods

    private static IEnumerable<Chat> SystemChats()
    {
        yield return new Chat(Constants.Chat.AnnouncementsChatId) { Title = Constants.Chat.AnnouncementsChatTitle };
        foreach (var systemChat in Constants.Chat.System.All) {
            var chatId = systemChat == Constants.Chat.System.Welcome
                ? PlaceChatId.New(PlaceId.New())
                : (ChatId)GroupChatId.New();
            yield return new Chat(chatId) { Title = systemChat.DefaultTitle, SystemTag = systemChat.Tag };
        }
    }

    private static IStringLocalizer NewLocalizer(Language language)
        => new TestStringLocalizer(StringCatalogs.LoadStrings(language)!, language);

    private static IEnumerable<Language> ShippedLanguages()
        => StringCatalogs.ShippedSubtags(StringCatalogs.Kind.Strings)
            .Select(s => Languages.AllUIAndTestOnly.SingleOrDefault(l => l.IsoCode == s))
            .OfType<Language>();
}
