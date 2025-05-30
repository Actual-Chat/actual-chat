using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

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
        await Tester.SignInAsUniqueBobAdmin();
        var appSettings = await Tester.AccountSettings.GetUserAppSettings(CancellationToken.None);
        await Tester.AccountSettings.SetUserAppSettings(appSettings with {
            IsIncompleteUIEnabled = true,
            AreExperimentalFeaturesEnabled = true,
        }, CancellationToken.None);
    }

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData]
    [InlineData("en")]
    [InlineData("en", "ru")]
    public async Task ShouldBeVisibleByDefaultWhenAnyForeignEntry(string sPrimary = "", string sSecondary = "")
    {
        // arrange
        await LanguageUI.WhenReady;
        var primary = Language.ParseNullable(sPrimary);
        var secondary = Language.ParseNullable(sSecondary);
        if (primary is not null || secondary is not null)
            LanguageUI.UpdateSettings(x => x with { Primary = primary!, Secondary = secondary });
        var (chatId, _) = await Tester.CreateChat(true);

        // act
        await CreateEntries(chatId, ("Bonjour", Languages.French));

        // assert
        await AssertIsGlobeVisible(chatId, true);
        await AssertIsSubHeaderVisible(chatId, true);
    }

    [Fact]
    public async Task ShouldNotBeVisibleByDefaultWhenNoForeignEntry()
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(true);

        // act
        await CreateEntries(chatId, ("Does not need translation", Languages.English));

        // assert
        await AssertIsGlobeVisible(chatId, false);
        await AssertIsSubHeaderVisible(chatId, false);
    }

    [Fact(Skip = "Fails locally, need to investigate why")]
    public async Task ShouldNotBeVisibleWhenLanguageMatchesSpoken()
    {
        // arrange
        await LanguageUI.WhenReady;
        LanguageUI.UpdateSettings(x => x with { Primary = Languages.English, Secondary = Languages.French });
        var (chatId, _) = await Tester.CreateChat(true);

        // act
        await CreateEntries(chatId, ("Bonjour", Languages.French));

        // assert
        await AssertIsGlobeVisible(chatId, false);
        await AssertIsSubHeaderVisible(chatId, false);
    }

    [Fact]
    public async Task ShouldNotBeVisibleIfForeignEntryIsNotVisible()
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(true);

        // act
        var entries = await CreateEntries(chatId, ("Does not need translation", Languages.English), ("Bonjour", Languages.French));
        var englishEntry = entries[0];
        SetVisibleItems(englishEntry);

        // assert
        await AssertIsGlobeVisible(chatId, false);
        await AssertIsSubHeaderVisible(chatId, false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GlobeShouldBeVisibleWhenLanguageMatchesSpokenButIsAlreadyOnOrOff(bool isOn)
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(true);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English);
        await TranslationUI.SetIsOn(chatId, isOn);

        // act
        await CreateEntries(chatId, ("Does not need translation", Languages.English));

        // assert
        await AssertIsGlobeVisible(chatId, true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsOnShouldNotAffectSubheaderVisibility(bool initialIsOn)
    {
        // arrange
        var (chatId, _) = await Tester.CreateChat(true);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English);
        await TranslationUI.SetIsSubHeaderVisible(chatId, true);

        // act
        await TranslationUI.SetIsOn(chatId, initialIsOn);

        // assert
        await AssertIsSubHeaderVisible(chatId, true);

        // act
        await TranslationUI.SetIsOn(chatId, !initialIsOn);

        // assert
        await AssertIsSubHeaderVisible(chatId, true);
    }

    private async Task<List<ChatEntry>> CreateEntries(ChatId chatId, params (string Text, Language ExpectedLanguage)[] toCreate)
    {
        await using var _1 = await Tester.BackupAuth();
        await Tester.SignInAsUniqueAlice();
        await Tester.JoinChat(chatId, Symbol.Empty, false);
        var entries = new List<ChatEntry>(toCreate.Length);
        var languageDetectionTasks = new List<Task>();
        foreach (var (text, expectedLanguage) in toCreate) {
            var entry = await Tester.CreateTextEntry(chatId, text);
            languageDetectionTasks.Add(WhenLanguageDetected(entry.Id, expectedLanguage));
            entries.Add(entry);
        }
        await languageDetectionTasks.Collect(1);
        SetVisibleItems(entries);
        return entries;
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

    private Task AssertIsSubHeaderVisible(ChatId chatId, bool expected)
        => ComputedTest.When(async ct => {
            var isVisible = await TranslationUI.IsSubHeaderVisible(chatId, ct);
            isVisible.Should().Be(expected);
        });

    private Task AssertIsGlobeVisible(ChatId chatId, bool expected)
        => ComputedTest.When(async ct => {
            var isVisible = await TranslationUI.IsGlobeVisible(chatId, ct);
            isVisible.Should().Be(expected);
        });
}
