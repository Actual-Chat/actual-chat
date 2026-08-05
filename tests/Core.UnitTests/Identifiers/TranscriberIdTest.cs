namespace ActualChat.Core.UnitTests.Identifiers;

public class TranscriberIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<TranscriberId>(@out)
{
    public override string[] ValidIdentifiers => [
        "soniox-stream",
        "openai-offline",
        "deepgram-stream",
        "u:k7f2xq",
        "u:some-key:with-colons",
        "deepgram-stream~openai-offline",
    ];
    public override string[] InvalidIdentifiers => [
        "u:",
        "~openai-offline",
        "deepgram-stream~",
        "a~b~c",
    ];

    [Fact]
    public void PairExposesBothHalves()
    {
        // act
        var pair = TranscriberId.Parse("deepgram-stream~openai-offline");

        // assert
        pair.IsPair.Should().BeTrue();
        pair.PrimaryId.Should().Be(TranscriberId.DeepgramStream);
        pair.RetranscriberId.Should().Be(TranscriberId.OpenAIOffline);
        pair.Source.Should().Be(TranscriberSource.Builtin);
    }

    [Fact]
    public void NewPairRoundTrips()
    {
        // act
        var pair = TranscriberId.NewPair(TranscriberId.SonioxStream, TranscriberId.SonioxOffline);

        // assert
        pair.Value.Should().Be("soniox-stream~soniox-offline");
        pair.Should().Be(TranscriberId.Parse(pair.Value));
    }

    [Fact]
    public void NonPairIsItsOwnPrimary()
    {
        // act
        var id = TranscriberId.Parse("soniox-stream");

        // assert
        id.IsPair.Should().BeFalse();
        id.PrimaryId.Should().BeSameAs(id);
        id.RetranscriberId.Should().BeNull();
    }

    [Fact]
    public void UserPairKeepsUserSource()
    {
        // act
        var pair = TranscriberId.Parse("u:my-key~openai-offline");

        // assert
        pair.Source.Should().Be(TranscriberSource.User);
        pair.PrimaryId.Key.Value.Should().Be("my-key");
        pair.RetranscriberId!.Should().Be(TranscriberId.OpenAIOffline);
    }

    [Fact]
    public void SourceAndKeyAreParsed()
    {
        // act
        var builtin = TranscriberId.Parse("soniox-stream");
        var user = TranscriberId.Parse("u:k7f2xq");

        // assert
        builtin.Source.Should().Be(TranscriberSource.Builtin);
        builtin.Key.Value.Should().Be("soniox-stream");
        user.Source.Should().Be(TranscriberSource.User);
        user.Key.Value.Should().Be("k7f2xq");
    }

    [Fact]
    public void UserKeyMayContainColons()
    {
        // act
        var id = TranscriberId.Parse("u:some-key:with-colons");

        // assert
        id.Source.Should().Be(TranscriberSource.User);
        id.Key.Value.Should().Be("some-key:with-colons");
    }

    [Fact]
    public void FactoriesMatchWellKnownIds()
    {
        // assert
        TranscriberId.NewBuiltin("google-stream").Should().Be(TranscriberId.GoogleStream);
        TranscriberId.NewUser("k7f2xq").Value.Should().Be("u:k7f2xq");
        TranscriberId.OpenAIOffline.Value.Should().Be("openai-offline");
    }

    [Fact]
    public void EmptyParsesToNone()
    {
        // act
        var isParsed = TranscriberId.TryParse("", out var id);

        // assert
        isParsed.Should().BeTrue();
        id.Should().BeSameAs(TranscriberId.None);
        TranscriberId.None.IsNone.Should().BeTrue();
        TranscriberId.GoogleStream.IsNone.Should().BeFalse();
    }
}
