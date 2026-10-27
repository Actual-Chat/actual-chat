using ActualChat.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class PluralLocalizerExtTest
{
    // One row per shipped language per count that its rule can tell apart:
    // 1 / 2 / 5 separate one, few and many; 21 is where the East Slavic rule returns to "one"
    // and where Polish deliberately does not - "21 uczestników", never "21 uczestnik".
    private static readonly (string Subtag, int Count, string Expected)[] MemberCountCases = [
        ("en", 1, "1 member"), ("en", 2, "2 members"), ("en", 5, "5 members"), ("en", 21, "21 members"),
        ("af", 1, "1 lid"), ("af", 2, "2 lede"), ("af", 5, "5 lede"), ("af", 21, "21 lede"),
        ("az", 1, "1 üzv"), ("az", 2, "2 üzv"), ("az", 5, "5 üzv"), ("az", 21, "21 üzv"),
        ("be", 1, "1 удзельнік"), ("be", 2, "2 удзельнікі"), ("be", 5, "5 удзельнікаў"), ("be", 21, "21 удзельнік"),
        ("bg", 1, "1 участник"), ("bg", 2, "2 участници"), ("bg", 5, "5 участници"), ("bg", 21, "21 участници"),
        ("bn", 1, "1 জন সদস্য"), ("bn", 2, "2 জন সদস্য"), ("bn", 5, "5 জন সদস্য"), ("bn", 21, "21 জন সদস্য"),
        ("bs", 1, "1 član"), ("bs", 2, "2 člana"), ("bs", 5, "5 članova"), ("bs", 21, "21 član"),
        ("ca", 1, "1 membre"), ("ca", 2, "2 membres"), ("ca", 5, "5 membres"), ("ca", 21, "21 membres"),
        ("cnr", 1, "1 član"), ("cnr", 2, "2 člana"), ("cnr", 5, "5 članova"), ("cnr", 21, "21 član"),
        ("cs", 1, "1 člen"), ("cs", 2, "2 členové"), ("cs", 5, "5 členů"), ("cs", 21, "21 členů"),
        ("cy", 1, "1 aelod"), ("cy", 2, "2 aelod"), ("cy", 5, "5 aelod"), ("cy", 21, "21 aelod"),
        ("da", 1, "1 medlem"), ("da", 2, "2 medlemmer"), ("da", 5, "5 medlemmer"), ("da", 21, "21 medlemmer"),
        ("de", 1, "1 Mitglied"), ("de", 2, "2 Mitglieder"), ("de", 5, "5 Mitglieder"), ("de", 21, "21 Mitglieder"),
        ("el", 1, "1 μέλος"), ("el", 2, "2 μέλη"), ("el", 5, "5 μέλη"), ("el", 21, "21 μέλη"),
        ("es", 1, "1 miembro"), ("es", 2, "2 miembros"), ("es", 5, "5 miembros"), ("es", 21, "21 miembros"),
        ("et", 1, "1 liige"), ("et", 2, "2 liiget"), ("et", 5, "5 liiget"), ("et", 21, "21 liiget"),
        ("eu", 1, "1 kide"), ("eu", 2, "2 kide"), ("eu", 5, "5 kide"), ("eu", 21, "21 kide"),
        ("fi", 1, "1 jäsen"), ("fi", 2, "2 jäsentä"), ("fi", 5, "5 jäsentä"), ("fi", 21, "21 jäsentä"),
        ("fil", 1, "1 miyembro"), ("fil", 2, "2 miyembro"), ("fil", 5, "5 miyembro"), ("fil", 21, "21 miyembro"),
        ("fr", 1, "1 membre"), ("fr", 2, "2 membres"), ("fr", 5, "5 membres"), ("fr", 21, "21 membres"),
        ("gl", 1, "1 membro"), ("gl", 2, "2 membros"), ("gl", 5, "5 membros"), ("gl", 21, "21 membros"),
        ("gu", 1, "1 સભ્ય"), ("gu", 2, "2 સભ્યો"), ("gu", 5, "5 સભ્યો"), ("gu", 21, "21 સભ્યો"),
        ("hi", 1, "1 सदस्य"), ("hi", 2, "2 सदस्य"), ("hi", 5, "5 सदस्य"), ("hi", 21, "21 सदस्य"),
        ("hr", 1, "1 član"), ("hr", 2, "2 člana"), ("hr", 5, "5 članova"), ("hr", 21, "21 član"),
        ("hu", 1, "1 tag"), ("hu", 2, "2 tag"), ("hu", 5, "5 tag"), ("hu", 21, "21 tag"),
        ("id", 1, "1 anggota"), ("id", 2, "2 anggota"), ("id", 5, "5 anggota"), ("id", 21, "21 anggota"),
        ("it", 1, "1 membro"), ("it", 2, "2 membri"), ("it", 5, "5 membri"), ("it", 21, "21 membri"),
        ("ja", 1, "メンバー 1 人"), ("ja", 2, "メンバー 2 人"), ("ja", 5, "メンバー 5 人"), ("ja", 21, "メンバー 21 人"),
        ("kk", 1, "1 қатысушы"), ("kk", 2, "2 қатысушы"), ("kk", 5, "5 қатысушы"), ("kk", 21, "21 қатысушы"),
        ("kn", 1, "1 ಸದಸ್ಯ"), ("kn", 2, "2 ಸದಸ್ಯರು"), ("kn", 5, "5 ಸದಸ್ಯರು"), ("kn", 21, "21 ಸದಸ್ಯರು"),
        ("ko", 1, "멤버 1명"), ("ko", 2, "멤버 2명"), ("ko", 5, "멤버 5명"), ("ko", 21, "멤버 21명"),
        ("lt", 1, "1 narys"), ("lt", 2, "2 nariai"), ("lt", 5, "5 nariai"), ("lt", 21, "21 narys"),
        ("lv", 1, "1 dalībnieks"), ("lv", 2, "2 dalībnieki"), ("lv", 5, "5 dalībnieki"), ("lv", 21, "21 dalībnieks"),
        ("max", 1, "1 உறுப்பினர்கள்"), ("max", 2, "2 உறுப்பினர்கள்"),
        ("max", 5, "5 உறுப்பினர்கள்"), ("max", 21, "21 உறுப்பினர்கள்"),
        ("mk", 1, "1 член"), ("mk", 2, "2 членови"), ("mk", 5, "5 членови"), ("mk", 21, "21 член"),
        ("ml", 1, "1 അംഗം"), ("ml", 2, "2 അംഗങ്ങൾ"), ("ml", 5, "5 അംഗങ്ങൾ"), ("ml", 21, "21 അംഗങ്ങൾ"),
        ("mr", 1, "1 सदस्य"), ("mr", 2, "2 सदस्य"), ("mr", 5, "5 सदस्य"), ("mr", 21, "21 सदस्य"),
        ("ms", 1, "1 ahli"), ("ms", 2, "2 ahli"), ("ms", 5, "5 ahli"), ("ms", 21, "21 ahli"),
        ("nl", 1, "1 lid"), ("nl", 2, "2 leden"), ("nl", 5, "5 leden"), ("nl", 21, "21 leden"),
        ("no", 1, "1 medlem"), ("no", 2, "2 medlemmer"), ("no", 5, "5 medlemmer"), ("no", 21, "21 medlemmer"),
        ("pa", 1, "1 ਮੈਂਬਰ"), ("pa", 2, "2 ਮੈਂਬਰ"), ("pa", 5, "5 ਮੈਂਬਰ"), ("pa", 21, "21 ਮੈਂਬਰ"),
        ("pl", 1, "1 uczestnik"), ("pl", 2, "2 uczestnicy"), ("pl", 5, "5 uczestników"),
        ("pl", 21, "21 uczestników"),
        ("pt", 1, "1 membro"), ("pt", 2, "2 membros"), ("pt", 5, "5 membros"), ("pt", 21, "21 membros"),
        ("ro", 1, "1 membru"), ("ro", 2, "2 membri"), ("ro", 5, "5 membri"), ("ro", 21, "21 de membri"),
        ("ru", 1, "1 участник"), ("ru", 2, "2 участника"), ("ru", 5, "5 участников"), ("ru", 21, "21 участник"),
        ("sk", 1, "1 člen"), ("sk", 2, "2 členovia"), ("sk", 5, "5 členov"), ("sk", 21, "21 členov"),
        ("sl", 1, "1 član"), ("sl", 2, "2 člana"), ("sl", 5, "5 članov"), ("sl", 21, "21 članov"),
        ("sq", 1, "1 anëtar"), ("sq", 2, "2 anëtarë"), ("sq", 5, "5 anëtarë"), ("sq", 21, "21 anëtarë"),
        ("sr", 1, "1 члан"), ("sr", 2, "2 члана"), ("sr", 5, "5 чланова"), ("sr", 21, "21 члан"),
        ("sv", 1, "1 medlem"), ("sv", 2, "2 medlemmar"), ("sv", 5, "5 medlemmar"), ("sv", 21, "21 medlemmar"),
        ("sw", 1, "mwanachama 1"), ("sw", 2, "wanachama 2"), ("sw", 5, "wanachama 5"), ("sw", 21, "wanachama 21"),
        ("ta", 1, "1 உறுப்பினர்"), ("ta", 2, "2 உறுப்பினர்கள்"),
        ("ta", 5, "5 உறுப்பினர்கள்"), ("ta", 21, "21 உறுப்பினர்கள்"),
        ("te", 1, "సభ్యులు: 1"), ("te", 2, "సభ్యులు: 2"), ("te", 5, "సభ్యులు: 5"), ("te", 21, "సభ్యులు: 21"),
        ("th", 1, "สมาชิก 1 คน"), ("th", 2, "สมาชิก 2 คน"), ("th", 5, "สมาชิก 5 คน"), ("th", 21, "สมาชิก 21 คน"),
        ("tr", 1, "1 üye"), ("tr", 2, "2 üye"), ("tr", 5, "5 üye"), ("tr", 21, "21 üye"),
        ("uk", 1, "1 учасник"), ("uk", 2, "2 учасники"), ("uk", 5, "5 учасників"), ("uk", 21, "21 учасник"),
        ("uz", 1, "1 ta aʼzo"), ("uz", 2, "2 ta aʼzo"), ("uz", 5, "5 ta aʼzo"), ("uz", 21, "21 ta aʼzo"),
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
        var l = NewLocalizer(GetCatalogLanguage(subtag));

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
        var l = NewLocalizer(GetCatalogLanguage(subtag));

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
        var l = NewLocalizer(GetCatalogLanguage(subtag));

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

    private static Language GetCatalogLanguage(string subtag)
        => Languages.AllUIAndTestOnly.Single(l => l.IsoCode == subtag);
}
