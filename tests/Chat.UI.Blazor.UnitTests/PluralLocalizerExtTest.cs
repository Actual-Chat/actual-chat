using ActualChat.UI.Blazor.Resources;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class PluralLocalizerExtTest
{
    // One row per shipped language per count that its rule can tell apart:
    // 1 / 2 / 5 separate one, few and many; 21 is where the East Slavic rule returns to "one"
    // and where Polish deliberately does not - "21 uczestników", never "21 uczestnik".
    private static readonly (string Subtag, int Count, string Expected)[] MemberCountCases = [
        ("en", 1, "1 member"), ("en", 2, "2 members"), ("en", 5, "5 members"), ("en", 21, "21 members"),
        ("cs", 1, "1 člen"), ("cs", 2, "2 členové"), ("cs", 5, "5 členů"), ("cs", 21, "21 členů"),
        ("de", 1, "1 Mitglied"), ("de", 2, "2 Mitglieder"), ("de", 5, "5 Mitglieder"), ("de", 21, "21 Mitglieder"),
        ("es", 1, "1 miembro"), ("es", 2, "2 miembros"), ("es", 5, "5 miembros"), ("es", 21, "21 miembros"),
        ("fr", 1, "1 membre"), ("fr", 2, "2 membres"), ("fr", 5, "5 membres"), ("fr", 21, "21 membres"),
        ("hi", 1, "1 सदस्य"), ("hi", 2, "2 सदस्य"), ("hi", 5, "5 सदस्य"), ("hi", 21, "21 सदस्य"),
        ("id", 1, "1 anggota"), ("id", 2, "2 anggota"), ("id", 5, "5 anggota"), ("id", 21, "21 anggota"),
        ("it", 1, "1 membro"), ("it", 2, "2 membri"), ("it", 5, "5 membri"), ("it", 21, "21 membri"),
        ("ja", 1, "メンバー 1 人"), ("ja", 2, "メンバー 2 人"), ("ja", 5, "メンバー 5 人"), ("ja", 21, "メンバー 21 人"),
        ("ko", 1, "멤버 1명"), ("ko", 2, "멤버 2명"), ("ko", 5, "멤버 5명"), ("ko", 21, "멤버 21명"),
        ("pl", 1, "1 uczestnik"), ("pl", 2, "2 uczestnicy"), ("pl", 5, "5 uczestników"),
        ("pl", 21, "21 uczestników"),
        ("pt", 1, "1 membro"), ("pt", 2, "2 membros"), ("pt", 5, "5 membros"), ("pt", 21, "21 membros"),
        ("ru", 1, "1 участник"), ("ru", 2, "2 участника"), ("ru", 5, "5 участников"), ("ru", 21, "21 участник"),
        ("tr", 1, "1 üye"), ("tr", 2, "2 üye"), ("tr", 5, "5 üye"), ("tr", 21, "21 üye"),
        ("uk", 1, "1 учасник"), ("uk", 2, "2 учасники"), ("uk", 5, "5 учасників"), ("uk", 21, "21 учасник"),
        ("vi", 1, "1 thành viên"), ("vi", 2, "2 thành viên"), ("vi", 5, "5 thành viên"), ("vi", 21, "21 thành viên"),
        ("zh", 1, "1 名成员"), ("zh", 2, "2 名成员"), ("zh", 5, "5 名成员"), ("zh", 21, "21 名成员"),
    ];

    public static TheoryData<string, int, string> MemberCounts { get; } = NewTheoryData(MemberCountCases);

    // The languages that don't inflect the noun: zh, vi and tr still list two forms,
    // ja and ko list one. 0 / 1 / 2 are the counts their rule can tell apart.
    private static readonly (string Subtag, int Count, string Expected)[] DeleteTitleCases = [
        ("zh", 0, "删除这些消息?"), ("zh", 1, "删除该消息?"), ("zh", 2, "删除这些消息?"),
        ("vi", 0, "Xóa các tin nhắn?"), ("vi", 1, "Xóa tin nhắn?"), ("vi", 2, "Xóa các tin nhắn?"),
        ("tr", 0, "Mesajlar silinsin mi?"), ("tr", 1, "Mesaj silinsin mi?"), ("tr", 2, "Mesajlar silinsin mi?"),
        ("ja", 0, "メッセージを削除しますか?"), ("ja", 1, "メッセージを削除しますか?"), ("ja", 2, "メッセージを削除しますか?"),
        ("ko", 0, "메시지를 삭제할까요?"), ("ko", 1, "메시지를 삭제할까요?"), ("ko", 2, "메시지를 삭제할까요?"),
    ];

    public static TheoryData<string, int, string> DeleteTitles { get; } = NewTheoryData(DeleteTitleCases);

    [Theory]
    [MemberData(nameof(MemberCounts))]
    public void EveryLanguageShouldAgreeWithTheNumeral(string subtag, int count, string expected)
    {
        // arrange
        var l = NewLocalizer(Language.Parse(subtag));

        // act
        var text = l.Chat_Members(count, count);

        // assert
        text.Should().Be(expected);
    }

    [Fact]
    public void EveryShippedLanguageShouldBeCovered()
    {
        // A new language ships with a plural rule it never gets checked against unless
        // this test forces its counted forms into the table above.

        // arrange
        var covered = MemberCountCases.Select(c => c.Subtag).ToHashSet();

        // act
        var uncovered = StringCatalogs.ShippedSubtags(StringCatalogs.Kind.Strings)
            .Where(s => !covered.Contains(s))
            .ToList();

        // assert
        uncovered.Should().BeEmpty("every shipped language must have counted forms in MemberCountCases");
    }

    [Theory]
    [InlineData(11, "11 участников", "11 учасників")]
    [InlineData(14, "14 участников", "14 учасників")]
    [InlineData(22, "22 участника", "22 учасники")]
    [InlineData(25, "25 участников", "25 учасників")]
    [InlineData(101, "101 участник", "101 учасник")]
    [InlineData(112, "112 участников", "112 учасників")]
    public void SlavicRuleShouldHandleTeensAndHundreds(int count, string ru, string uk)
    {
        // 11-14 take the "many" form even though they end in 1-4 - the trap the mod 100 check exists for.

        // arrange
        var lRu = NewLocalizer(Languages.Russian);
        var lUk = NewLocalizer(Languages.Ukrainian);

        // act
        var texts = (Ru: lRu.Chat_Members(count, count), Uk: lUk.Chat_Members(count, count));

        // assert
        texts.Should().Be((ru, uk));
    }

    [Theory]
    [MemberData(nameof(DeleteTitles))]
    public void LanguagesWithoutNounPluralsShouldStillPickBothForms(string subtag, int count, string expected)
    {
        // zh, vi and tr leave the noun alone but still split one from other with a demonstrative
        // or a plural marker; Chat_Members is single-form in all five, so nothing else covers that.

        // arrange
        var l = NewLocalizer(Language.Parse(subtag));

        // act
        var text = l.Selection_DeleteTitle(count);

        // assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData("en", "0 members")]
    [InlineData("fr", "0 membre")]
    [InlineData("pt", "0 membro")]
    [InlineData("hi", "0 सदस्य")]
    [InlineData("ru", "0 участников")]
    public void ZeroShouldFollowTheLanguageRule(string subtag, string expected)
    {
        // French, Hindi and Portuguese count zero as singular; English and Russian don't.

        // arrange
        var l = NewLocalizer(Language.Parse(subtag));

        // act
        var text = l.Chat_Members(0, 0);

        // assert
        text.Should().Be(expected);
    }

    [Fact]
    public void EnglishFallbackShouldLandOnTheOtherForm()
    {
        // A key missing from the Russian catalog resolves to the English value, which
        // has no "many" form - the clamp has to land on the last one it does have.

        // arrange
        var strings = StringCatalogs.Load(StringCatalogs.Kind.Strings, Languages.English)!;
        var l = new TestStringLocalizer(strings, Languages.Russian);

        // act
        var text = l.Chat_Members(5, 5);

        // assert
        text.Should().Be("5 members");
    }

    // Private methods

    private static TheoryData<string, int, string> NewTheoryData((string Subtag, int Count, string Expected)[] cases)
    {
        var result = new TheoryData<string, int, string>();
        foreach (var (subtag, count, expected) in cases)
            result.Add(subtag, count, expected);
        return result;
    }

    private static TestStringLocalizer NewLocalizer(Language language)
        => new(StringCatalogs.Load(StringCatalogs.Kind.Strings, language)!, language);
}
