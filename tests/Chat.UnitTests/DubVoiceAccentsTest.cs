namespace ActualChat.Chat.UnitTests;

public sealed class DubVoiceAccentsTest
{
    private static readonly DubVoice[] Catalog = [
        Voice("Ivan", "male", "slavic", "narration"),
        Voice("Olga", "female", "slavic", "conversational"),
        Voice("Boris", "male", "slavic", "conversational"),
        Voice("Anna", "female", "slavic", "narration"),
        Voice("Pavel", "male", "slavic", "conversational"),
        Voice("Adrian", "male", "american", "conversational"),
        Voice("Emma", "female", "american", "conversational"),
        Voice("Noah", "male", "american", "narration"),
        Voice("Mia", "female", "american", "narration"),
        Voice("Liam", "male", "american", "conversational"),
        Voice("Ava", "female", "american", "conversational"),
        Voice("Diego", "male", "latin_american", "conversational"),
        Voice("Lucia", "female", "latin_american", "conversational"),
        Voice("Carlos", "male", "spanish", "conversational"),
        Voice("Daniel", "male", "british", "conversational"),
    ];

    [Theory]
    [InlineData("ru-RU", "slavic")]
    [InlineData("uk-UA", "slavic")]
    [InlineData("cnr-ME", "slavic")]
    [InlineData("es-ES", "spanish")]
    [InlineData("es-MX", "latin_american")]
    [InlineData("es-US", "latin_american")]
    [InlineData("pt-PT", "portuguese")]
    [InlineData("pt-BR", "brazilian")]
    [InlineData("hi-IN", "indian")]
    [InlineData("en-IN", "indian")]
    [InlineData("en-US", "american")]
    [InlineData("en-GB", "british")]
    [InlineData("zh-TW", "chinese")]
    [InlineData("fr-CA", "french")]
    [InlineData("fil-PH", "southeast_asian")]
    [InlineData("tr-TR", "american")]
    [InlineData("kk-KZ", "american")]
    public void MapsLanguageToAccent(string languageTag, string expectedAccent)
        => DubVoiceAccents.ForLanguage(Language.Parse(languageTag)).Should().Be(expectedAccent);

    [Fact]
    public void RussianPrimaryGetsSlavicVoicesOfBothGendersConversationalFirst()
    {
        // act
        var suggested = DubVoiceAccents.Suggest(Catalog, [Languages.Russian]);

        // assert
        suggested.Select(v => v.Id).Should().Equal("Boris", "Olga", "Pavel", "Anna", "Ivan");
        suggested.Should().OnlyContain(v => v.Accent == "slavic");
    }

    [Fact]
    public void MexicanSpanishGetsLatinAmericanVoicesPaddedWithAmerican()
    {
        // act
        var suggested = DubVoiceAccents.Suggest(Catalog, [Languages.SpanishMX]);

        // assert: two latin_american voices are fewer than MinSuggested, so american ones follow
        suggested.Select(v => v.Id).Should().Equal("Diego", "Lucia", "Adrian", "Emma", "Liam", "Ava", "Noah", "Mia");
        suggested.Should().NotContain(v => v.Accent == "spanish");
    }

    [Fact]
    public void UnknownLanguageGetsAmericanVoices()
    {
        // act
        var suggested = DubVoiceAccents.Suggest(Catalog, [Languages.Turkish]);

        // assert
        suggested.Should().OnlyContain(v => v.Accent == "american");
        suggested.Select(v => v.Id).Should().Equal("Adrian", "Emma", "Liam", "Ava", "Noah", "Mia");
    }

    [Fact]
    public void SecondaryLanguageFollowsThePrimaryOne()
    {
        // act
        var suggested = DubVoiceAccents.Suggest(Catalog, [Languages.Spanish, Languages.EnglishUK]);

        // assert: one spanish + one british voice are fewer than MinSuggested, so american ones pad it,
        // and the two males already listed count against the male cap
        suggested.Select(v => v.Id).Should().Equal("Carlos", "Daniel", "Adrian", "Emma", "Liam", "Ava", "Mia");
    }

    [Fact]
    public void CapsAtFourPerGenderAndEightInTotal()
    {
        // arrange: 6 slavic males + 6 slavic females + 2 american males
        var catalog = Enumerable.Range(0, 6).Select(i => Voice($"M{i}", "male", "slavic", "conversational"))
            .Concat(Enumerable.Range(0, 6).Select(i => Voice($"F{i}", "female", "slavic", "conversational")))
            .Concat(Enumerable.Range(0, 2).Select(i => Voice($"A{i}", "male", "american", "conversational")))
            .ToArray();

        // act
        var suggested = DubVoiceAccents.Suggest(catalog, [Languages.Russian, Languages.English]);

        // assert
        suggested.Should().HaveCount(DubVoiceAccents.MaxSuggested);
        suggested.Count(v => v.Gender == "male").Should().Be(DubVoiceAccents.MaxSuggestedPerGender);
        suggested.Count(v => v.Gender == "female").Should().Be(DubVoiceAccents.MaxSuggestedPerGender);
        suggested.Should().OnlyContain(v => v.Accent == "slavic");
    }

    [Fact]
    public void OneGenderOnlyFillsUpToItsCap()
    {
        // arrange: males only
        var catalog = Enumerable.Range(0, 6).Select(i => Voice($"M{i}", "male", "slavic", "conversational")).ToArray();

        // act
        var suggested = DubVoiceAccents.Suggest(catalog, [Languages.Russian]);

        // assert
        suggested.Select(v => v.Id).Should().Equal("M0", "M1", "M2", "M3");
    }

    [Fact]
    public void NeverListsAVoiceTwice()
    {
        // act: Russian and Ukrainian map to the same accent, English to the padding one
        var suggested = DubVoiceAccents.Suggest(Catalog, [Languages.Russian, Languages.Ukrainian, Languages.English]);

        // assert
        suggested.Select(v => v.Id).Should().OnlyHaveUniqueItems();
        suggested.Should().HaveCount(DubVoiceAccents.MaxSuggested);
    }

    [Fact]
    public void EmptyCatalogSuggestsNothing()
        => DubVoiceAccents.Suggest([], [Languages.Russian]).Should().BeEmpty();

    private static DubVoice Voice(string id, string gender, string accent, string useCase)
        => new(id) { Gender = gender, Accent = accent, UseCase = [useCase] };
}
