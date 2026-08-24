namespace ActualChat.Core.UnitTests.Identifiers;

public sealed class LanguageSupportTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void AllUIShouldMatchTheUIFlag()
    {
        // AllUI is declared rather than derived, because its order is the order the
        // App-language picker shows - so it has to be checked against the flag it mirrors.

        // act
        var flagged = Languages.All.Where(x => x.Support.HasFlag(LanguageSupport.UI)).ToList();

        // assert
        Languages.AllUI.Should().BeEquivalentTo(flagged,
            "every language flagged LanguageSupport.UI must appear in Languages.AllUI and vice-versa");
    }

    [Fact]
    public void AllUIAndTestOnlyShouldHaveNoDuplicateIsoCodes()
    {
        // A catalog is named after the IsoCode, so two catalog entries sharing one - en-GB
        // and en-IN both resolve to "en" - would load the same file twice and make the
        // picker offer a choice that changes nothing.

        // act
        var duplicates = Languages.AllUIAndTestOnly
            .GroupBy(x => x.IsoCode)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {g.Select(x => x.Value).ToDelimitedString()}")
            .ToList();

        // assert
        duplicates.Should().BeEmpty("each UI language must own a distinct catalog:\n{0}",
            duplicates.ToDelimitedString("\n"));
    }

    [Fact]
    public void MaxShouldBeAHiddenUrlOnlyPseudoLanguage()
    {
        // assert
        Languages.Max.Support.Should().Be(LanguageSupport.None);
        Languages.All.Should().NotContain(Languages.Max);
        Languages.AllUI.Should().NotContain(Languages.Max);
        Languages.AllTranscription.Should().NotContain(Languages.Max);
        Languages.AllUIAndTestOnly.Should().Contain(Languages.Max);
        Language.TryParse(Languages.Max.Value).Should().BeNull();
    }

    [Fact]
    public void MontenegrinShouldBeUIOnly()
    {
        // The case the split exists for: "cnr" is not a code any transcriber knows, so
        // offering it as a spoken language would hand the user a broken transcription.

        // assert
        Languages.Montenegrin.Support.Should().Be(LanguageSupport.UI);
        Languages.AllTranscription.Should().NotContain(Languages.Montenegrin);
        Languages.AllUI.Should().Contain(Languages.Montenegrin);
    }

    [Fact]
    public void EveryLanguageShouldSupportSomething()
    {
        // act
        var unusable = Languages.All.Where(x => x.Support == LanguageSupport.None).ToList();

        // assert
        unusable.Should().BeEmpty("a language nothing can use should be removed, not declared");
    }
}
