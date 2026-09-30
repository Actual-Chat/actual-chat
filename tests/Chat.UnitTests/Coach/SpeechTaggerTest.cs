using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechTaggerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Text = "So, um, I went, you know, to the store and it was awesome, like really awesome.";

    [Fact]
    public void ParseResponseShouldLocateEveryItem()
    {
        // arrange
        const string json = """
            {"items":[
              {"class":"filledPause","word":"um","occurrence":1,"synonyms":[]},
              {"class":"filler","word":"you know","occurrence":1,"synonyms":[]},
              {"class":"weak","word":"awesome","occurrence":2,"synonyms":["excellent","remarkable"]},
              {"class":"filler","word":"like","occurrence":1,"synonyms":[]}
            ]}
            """;

        // act
        var spans = SpeechTagger.ParseResponse(Text, json);

        // assert
        spans.Should().HaveCount(4);
        spans[0].Should().Be(new SpeechSpan(SpeechSpanKind.FilledPause, "um", 4, 2, ApiArray<string>.Empty));
        spans[3].Kind.Should().Be(SpeechSpanKind.Weak);
        spans[3].Start.Should().Be(Text.LastIndexOf("awesome"));
        spans[3].Synonyms.Should().Equal("excellent", "remarkable");
    }

    [Fact]
    public void UnlocatableItemsShouldBeDropped()
    {
        // arrange
        const string json = """
            {"items":[
              {"class":"filler","word":"basically","occurrence":1},
              {"class":"weak","word":"awesome","occurrence":3},
              {"class":"banana","word":"um","occurrence":1},
              {"class":"filledPause","word":"um","occurrence":1}
            ]}
            """;

        // act
        var spans = SpeechTagger.ParseResponse(Text, json);

        // assert
        spans.Should().ContainSingle().Which.Word.Should().Be("um");
    }

    [Fact]
    public void ParseResponseShouldTolerateCodeFences()
        => SpeechTagger.ParseResponse(Text, "```json\n{\"items\":[]}\n```").Should().BeEmpty();

    [Fact]
    public void ParseResponseShouldCapSynonymsAtThree()
    {
        // arrange
        const string json = """{"items":[{"class":"weak","word":"awesome","occurrence":1,"synonyms":["a","b","c","d"]}]}""";

        // act
        var spans = SpeechTagger.ParseResponse(Text, json);

        // assert
        spans.Should().ContainSingle().Which.Synonyms.Should().HaveCount(3);
    }

    [Fact]
    public void ParseResponseShouldThrowOnInvalidJson()
    {
        // act
        var act = () => SpeechTagger.ParseResponse(Text, "not json");

        // assert
        act.Should().Throw<Exception>("a schema failure must count as a failed call, not as an empty result");
    }

    [Fact]
    public void ParseResponseShouldMatchSubstringsForNoSpaceScripts()
    {
        // arrange
        const string text = "あの私はあの店に行きました";
        const string json = """{"items":[{"class":"filledPause","word":"あの","occurrence":2,"synonyms":[]}]}""";

        // act
        var spans = SpeechTagger.ParseResponse(text, json, isWordSplittable: false);

        // assert
        spans.Should().ContainSingle().Which.Start.Should().Be(4);
    }

    [Fact]
    public async Task StubShouldNotPretendToTag()
    {
        // act
        var result = await new SpeechTaggerStub().Tag(new SpeechTagRequest("um, hello", null), default);

        // assert
        result.Should().BeNull("a stub result would mark rows as tagged with zero fillers");
    }

    [Fact]
    public void BuildUserMessageShouldSendTheTextAloneWithoutContext()
    {
        // act
        var message = SpeechTagger.BuildUserMessage(new SpeechTagRequest("Um, hello.", null));

        // assert
        message.Should().Be("Um, hello.");
    }

    [Fact]
    public void BuildUserMessageShouldMarkTheContextAsReadOnly()
    {
        // arrange
        var request = new SpeechTagRequest("Well, it works.", null, "I tried it yesterday.");

        // act
        var message = SpeechTagger.BuildUserMessage(request);

        // assert
        message.Should().Contain("I tried it yesterday.").And.Contain("Well, it works.");
        message.IndexOf("I tried it yesterday.", StringComparison.Ordinal)
            .Should().BeLessThan(message.IndexOf("Well, it works.", StringComparison.Ordinal));
        message.Should().StartWith(SpeechTagger.ContextHeader);
        message.Should().Contain(SpeechTagger.TextHeader);
    }

    [Fact]
    public void ParseResponseShouldIgnoreAWordThatIsOnlyInTheContext()
    {
        // arrange: the chunk holds no "like"; the model returned one from the context sentence
        const string chunk = "It was fine.";
        const string json = """{"items":[{"class":"filler","word":"like","occurrence":1,"synonyms":[]}]}""";

        // act
        var spans = SpeechTagger.ParseResponse(chunk, json);

        // assert
        spans.Should().BeEmpty();
    }
}
