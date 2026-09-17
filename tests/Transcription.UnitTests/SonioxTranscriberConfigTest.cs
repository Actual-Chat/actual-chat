using ActualChat.Module;

namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxTranscriberConfigTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void StreamingConfigShouldIdentifyTheLanguageEvenWhenTheChatHasOne()
    {
        // arrange
        var options = new TranscriptionOptions { Language = Languages.Russian };

        // act
        var config = new SonioxTranscriber(NewServices()).NewConfig("key", options);

        // assert - tokens carry a language only with identification on, and the dub decides on it
        config["enable_language_identification"].Should().Be(true);
        config["language_hints"].Should().BeEquivalentTo(new[] { "ru" }, "the chat language stays the hint");
    }

    [Fact]
    public void StreamingConfigShouldHintWithTheCandidatesInDetectMode()
    {
        // arrange
        var options = TranscriptionOptions.AutoDetectLanguage([Languages.English, Languages.Russian]);

        // act
        var config = new SonioxTranscriber(NewServices()).NewConfig("key", options);

        // assert
        config["enable_language_identification"].Should().Be(true);
        config["language_hints"].Should().BeEquivalentTo(new[] { "en", "ru" });
    }

    [Fact]
    public void OfflineRequestShouldIdentifyTheLanguageEvenWhenTheChatHasOne()
    {
        // arrange
        var options = new TranscriptionOptions { Language = Languages.Russian };

        // act
        var request = new SonioxOfflineTranscriber(NewServices()).NewRequest("f1", options, null);

        // assert
        request["enable_language_identification"].Should().Be(true);
        request["language_hints"].Should().BeEquivalentTo(new[] { "ru" });
    }

    // Private methods

    private IServiceProvider NewServices()
    {
        var services = new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddSingleton<SonioxClient>()
            .AddSingleton(new SonioxCleaner.Options())
            .AddSingleton<SonioxCleaner>()
            .AddTestLogging(Out);
        services.AddHttpClient(SonioxClient.HttpClientName);
        return services.BuildServiceProvider();
    }
}
