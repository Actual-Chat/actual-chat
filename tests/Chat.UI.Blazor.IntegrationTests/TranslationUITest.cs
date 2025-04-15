using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(TranslationUICollection))]
public class TranslationUITest(TranslationAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TranslationAppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    [field: AllowNull, MaybeNull]
    private TranslationUI TranslationUI => field ??= Tester.ScopedAppServices.GetRequiredService<TranslationUI>();
    [field: AllowNull, MaybeNull]
    private LanguageUI LanguageUI => field ??= Tester.ScopedAppServices.GetRequiredService<LanguageUI>();
    [field: AllowNull, MaybeNull]
    private ChatUI ChatUI => field ??= Tester.ScopedAppServices.GetRequiredService<ChatUI>();

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Tester.SignInAsUniqueBob();
    }

    [Fact]
    public async Task ShouldSuggestTranslationInitially()
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Bonjour");
        SetVisibleItems(entry);
        await WhenLanguageDetected(entry.Id, Languages.French);

        // assert
        await AssertMustSuggest(chatId, true);
    }

    [Theory]
    [InlineData]
    [InlineData("en")]
    [InlineData("en", "ru")]
    public async Task ShouldSuggestTranslationIfLanguageDoesNotMatchSpoken(
        string primaryLanguage = "",
        string secondaryLanguage = "")
    {
        // arrange
        var primary = Language.ParseOrNull(primaryLanguage);
        var secondary = Language.ParseOrNull(secondaryLanguage);
        var languageSettings = await LanguageUI.Settings.Use(CancellationToken.None);
        LanguageUI.UpdateSettings(languageSettings with {
            Primary = primary!,
            Secondary = secondary,
        });
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Bonjour");
        SetVisibleItems(entry);
        await WhenLanguageDetected(entry.Id, Languages.French);

        // assert
        await AssertMustSuggest(chatId, true);
    }

    [Fact]
    public async Task ShouldNotSuggestTranslationIfLanguageMatchesSpoken()
    {
        // arrange
        var languageSettings = await LanguageUI.Settings.Use(CancellationToken.None);
        LanguageUI.UpdateSettings(languageSettings with {
            Primary = Languages.English,
            Secondary = Languages.French,
        });
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Bonjour");
        SetVisibleItems(entry);
        await WhenLanguageDetected(entry.Id, Languages.French);

        // assert
        await AssertMustSuggest(chatId, false);
    }

    [Fact]
    public async Task ShouldNotSuggestTranslationIfEntryIsNotVisible()
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var frenchEntry = await Tester.CreateTextEntry(chatId, "Bonjour");
        var englishEntries = await Tester.CreateTextEntries(chatId, "Does not need translation", 3);
        SetVisibleItems(englishEntries);
        await WhenLanguageDetected(frenchEntry.Id, Languages.French);

        // assert
        await AssertMustSuggest(chatId, false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldNotSuggestTranslationIfAlreadyIsOnOrOff(bool isOn)
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(false);
        await TranslationUI.SetIsOn(chatId, isOn);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Bonjour");
        SetVisibleItems(entry);
        await WhenLanguageDetected(entry.Id, Languages.French);

        // assert
        await AssertMustSuggest(chatId, false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldNotSuggestTranslationAfterIsOnIsChanged(bool isOn)
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Bonjour");
        SetVisibleItems(entry);
        await WhenLanguageDetected(entry.Id, Languages.French);

        // assert
        await AssertMustSuggest(chatId, true);

        // act
        await TranslationUI.SetIsOn(chatId, isOn);

        // assert
        await AssertMustSuggest(chatId, false);
    }

    private Task<ChatEntryLanguage> WhenLanguageDetected(ChatEntryId entryId, Language expectedLanguage)
        => ComputedTest.When(async ct => {
            var language = await Tester.GetEntryLanguage(entryId, ct).Require();
            language.Languages.Should().BeEquivalentTo([expectedLanguage]);
            return language;
        }, TimeSpan.FromSeconds(20).Debuggable());

    private void SetVisibleItems(params IReadOnlyCollection<ChatEntry> entries)
    {
        var chatId = entries.Select(x => x.ChatId).Distinct().Single();
        var lids = entries.Select(x => x.LocalId).ToHashSet();
        ChatUI.ItemVisibility.Value = new ChatViewItemVisibility(chatId, lids, true);
    }

    private Task AssertMustSuggest(ChatId chatId, bool expected)
        => ComputedTest.When(async ct => {
            var mustSuggest = await TranslationUI.MustSuggest(chatId, ct);
            mustSuggest.Should().Be(expected);
        });
}
