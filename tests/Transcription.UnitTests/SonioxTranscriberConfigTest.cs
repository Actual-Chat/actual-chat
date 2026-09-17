using ActualChat.Module;
using ActualChat.Transcription.Module;

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
    public void StreamingConfigShouldCarryTheEndpointSettings()
    {
        // arrange
        var settings = new TranscriptionSettings {
            SonioxMaxEndpointDelayMs = 800,
            SonioxEndpointLatencyAdjustmentLevel = 2,
            SonioxEndpointSensitivity = 0.4,
        };

        // act
        var config = new SonioxTranscriber(NewServices(settings)).NewConfig("key", new TranscriptionOptions());

        // assert
        config["max_endpoint_delay_ms"].Should().Be(800);
        config["endpoint_latency_adjustment_level"].Should().Be(2);
        config["endpoint_sensitivity"].Should().Be(0.4);
    }

    [Fact]
    public void StreamingConfigShouldOmitALatencyAdjustmentLevelOfZero()
    {
        // act - the defaults
        var config = new SonioxTranscriber(NewServices()).NewConfig("key", new TranscriptionOptions());

        // assert
        config["max_endpoint_delay_ms"].Should().Be(2000);
        config["endpoint_sensitivity"].Should().Be(0.0);
        config.Should().NotContainKey("endpoint_latency_adjustment_level",
            "level 0 is Soniox's default, and it's sent only when it changes something");
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

    private IServiceProvider NewServices(TranscriptionSettings? settings = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddSingleton(settings ?? new TranscriptionSettings())
            .AddSingleton<SonioxClient>()
            .AddSingleton(new SonioxCleaner.Options())
            .AddSingleton<SonioxCleaner>()
            .AddTestLogging(Out);
        services.AddHttpClient(SonioxClient.HttpClientName);
        return services.BuildServiceProvider();
    }
}
