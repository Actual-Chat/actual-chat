using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Services;
using MessagePack;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class SystemEntryLocalizationTest
{
    private const string AuthorName = "John";
    private static readonly AuthorId MentionedAuthorId = AuthorId.Parse("052w3sgrad:1");

    [Fact]
    public void EverySystemEntryKindShouldBeCovered()
    {
        // The tests below only see what Entries() builds, so a new [Union] kind would
        // otherwise ship unlocalized - SystemEntryMarkupBuilder renders it as empty markup.

        // arrange
        var kinds = typeof(SystemEntry).GetCustomAttributes<UnionAttribute>()
            .Select(a => a.SubType)
            .ToHashSet();

        // act
        var covered = AllEntries().Select(e => e.GetType()).ToHashSet();

        // assert
        covered.Should().BeEquivalentTo(kinds, "every SystemEntry kind must have a sample in Entries()");
    }

    [Fact]
    public void EnglishRenderingShouldMatchTheDefaultBuilder()
    {
        // Server-composed text (notifications, digests) is built by SystemEntryMarkupBuilder.Default,
        // so the English catalog must reproduce it exactly.

        // arrange
        var builder = NewBuilder(Languages.English);

        // act
        var mismatches = AllEntries()
            .Select(e => (
                Localized: builder.Build(e).ToReadableText(),
                English: SystemEntryMarkupBuilder.Default.Build(e).ToReadableText()))
            .Where(x => x.Localized != x.English)
            .Select(x => $"'{x.Localized}' != '{x.English}'")
            .ToList();

        // assert
        mismatches.Should().BeEmpty(
            "the English catalog must render exactly what the default builder renders:\n{0}",
            string.Join("\n", mismatches));
    }

    [Fact]
    public void EveryShippedLanguageShouldRenderEverySystemEntry()
    {
        // act
        var errors = new List<string>();
        foreach (var language in ShippedLanguages()) {
            var builder = NewBuilder(language);
            foreach (var entry in AllEntries()) {
                var text = builder.Build(entry).ToReadableText();
                if (text.Contains("SystemEntry_"))
                    errors.Add($"'{language.IsoCode}' leaves a key unresolved: '{text}'");
                else if (!text.Contains(AuthorName))
                    errors.Add($"'{language.IsoCode}' drops the author name: '{text}'");
                else if (text.Length <= AuthorName.Length + 1)
                    errors.Add($"'{language.IsoCode}' renders no sentence around the name: '{text}'");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "every system entry must render a complete sentence in every shipped language:\n{0}",
            string.Join("\n", errors));
    }

    [Fact]
    public void MentionShouldSurviveLocalization()
    {
        // The name is a separate markup node, so every language must still produce the
        // mention rather than folding the name into plain text.

        // arrange
        var expected = new HashSet<MentionRef> { MentionRef.NewAuthor(MentionedAuthorId) };

        // act
        var errors = new List<string>();
        foreach (var language in ShippedLanguages()) {
            var builder = NewBuilder(language);
            foreach (var entry in Entries(MentionedAuthorId)) {
                var mentions = MentionExtractor.Instance.GetMentionIds(builder.Build(entry));
                if (!mentions.SetEquals(expected))
                    errors.Add($"'{language.IsoCode}' renders {mentions.Count} mention(s) for {entry.GetType().Name}");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "a localized system entry must keep its author mention:\n{0}", string.Join("\n", errors));
    }

    [Fact]
    public void MissingAuthorNameShouldFallBackToSomeone()
    {
        // arrange
        var builder = NewBuilder(Languages.Russian);

        // act
        var text = builder.Build(new MembersChangedEntry()).ToReadableText();

        // assert
        text.Should().StartWith("Кто-то", "the fallback name must be localized too");
    }

    // Private methods

    private static IEnumerable<SystemEntry> AllEntries()
        => Entries(null).Concat(Entries(MentionedAuthorId));

    private static IEnumerable<SystemEntry> Entries(AuthorId? authorId)
    {
        yield return new MembersChangedEntry { TargetAuthorId = authorId, TargetAuthorName = AuthorName };
        yield return new MembersChangedEntry {
            TargetAuthorId = authorId, TargetAuthorName = AuthorName, HasLeft = true,
        };
        yield return new NotifyMembersEntry { TargetAuthorId = authorId, TargetAuthorName = AuthorName };
    }

    private static SystemEntryMarkupBuilder NewBuilder(Language language)
        => new LocalizedSystemEntryMarkupBuilder(new ServiceCollection()
            .AddSingleton<IStringLocalizer>(
                new TestStringLocalizer(StringCatalogs.LoadStrings(language)!, language))
            .BuildServiceProvider());

    private static IEnumerable<Language> ShippedLanguages()
        => StringCatalogs.ShippedSubtags(StringCatalogs.Kind.Strings)
            .Select(s => Languages.AllUIAndTestOnly.SingleOrDefault(l => l.IsoCode == s))
            .OfType<Language>();
}
