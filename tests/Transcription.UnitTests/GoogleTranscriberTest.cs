using Google.Cloud.Speech.V1P1Beta1;

namespace ActualChat.Transcription.UnitTests;

public class GoogleTranscriberTest
{
    private readonly ILogger<GoogleTranscriber> _logger;

    public GoogleTranscriberTest(ILogger<GoogleTranscriber> logger)
        => _logger = logger;

    [Fact]
    public async Task DuplicateFinalResponsesTest()
    {
        var transcriber = new GoogleTranscriber(_logger);
        var channel = Channel.CreateUnbounded<TranscriptUpdate>();
        await transcriber.ReadTranscript(GenerateResponses(), channel, CancellationToken.None);

        var results = await channel.Reader.ReadAllAsync().ToListAsync();
        results.Max(tu => tu.UpdatedPart.Duration).Should().Be(3.47d);
        results.Max(tu => tu.UpdatedPart.TextToTimeMap.TargetRange.Min).Should().Be(0d);

        var transcript = results[0].UpdatedPart!;
        foreach (var transcriptUpdate in results.Skip(1))
            transcript = transcript.WithUpdate(transcriptUpdate);

        transcript.Text.Should().Be("проверка связи");
        transcript.TextToTimeMap.SourcePoints.Should()
            .Equal(new[] { 0d, 9, 14 }, (l, r) => Math.Abs(l - r) < 0.001);
        transcript.TextToTimeMap.TargetPoints.Should()
            .Equal(new[] { 0d, 1.3, 3.47 }, (l, r) => Math.Abs(l - r) < 0.0001);

        _logger.LogInformation("Transcript: {Transcript}", transcript);

        async IAsyncEnumerable<StreamingRecognizeResponse> GenerateResponses()
        {
            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = { new[] { new SpeechRecognitionAlternative { Transcript = "проверь" } } },
                            Stability = 0.01f,
                            ResultEndTime = new () { Seconds = 1, Nanos = 100_000_000 },
                            LanguageCode = "ru-ru",
                        },
                    },
                },
            };

            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = { new[] { new SpeechRecognitionAlternative { Transcript = "проверка" } } },
                            Stability = 0.01f,
                            ResultEndTime = new () { Seconds = 1, Nanos = 220_000_000 },
                            LanguageCode = "ru-ru",
                        },
                    },
                },
            };

            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = { new[] { new SpeechRecognitionAlternative { Transcript = "проверка" } } },
                            Stability = 0.09f,
                            ResultEndTime = new () { Seconds = 1, Nanos = 820_000_000 },
                            LanguageCode = "ru-ru",
                        },
                    },
                },
            };

            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = { new[] { new SpeechRecognitionAlternative { Transcript = "проверка" } } },
                            Stability = 0.09f,
                            ResultEndTime = new () { Seconds = 2, Nanos = 840_000_000 },
                            LanguageCode = "ru-ru",
                        },
                    },
                },
            };

            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives =
                                { new[] { new SpeechRecognitionAlternative { Transcript = "проверка связи" } } },
                            Stability = 0.09f,
                            ResultEndTime = new () { Seconds = 3, Nanos = 440_000_000 },
                            LanguageCode = "ru-ru",
                        },
                    },
                },
            };

            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = {
                                new[] {
                                    new SpeechRecognitionAlternative {
                                        Transcript = "проверка связи", Confidence = 0.9527583f,
                                        Words = {
                                            new[] {
                                                new WordInfo {
                                                    StartTime = new () { Seconds = 0, Nanos = 200_000_000 },
                                                    EndTime = new () { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new () { Seconds = 1, Nanos = 300_000_000 },
                                                    EndTime = new () { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerTag = 1,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndTime = new () { Seconds = 3, Nanos = 470_000_000 },
                            LanguageCode = "ru-ru",
                            IsFinal = true,
                        },
                    },
                },
            };

            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = {
                                new[] {
                                    new SpeechRecognitionAlternative {
                                        Transcript = "проверка связи", Confidence = 0.9527583f,
                                        Words = {
                                            new[] {
                                                new WordInfo {
                                                    StartTime = new () { Seconds = 0, Nanos = 200_000_000 },
                                                    EndTime = new () { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new () { Seconds = 1, Nanos = 300_000_000 },
                                                    EndTime = new () { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new () { Seconds = 0, Nanos = 200_000_000 },
                                                    EndTime = new () { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new () { Seconds = 1, Nanos = 300_000_000 },
                                                    EndTime = new () { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerTag = 1,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndTime = new () { Seconds = 3, Nanos = 470_000_000 },
                            LanguageCode = "ru-ru",
                            IsFinal = true,
                        },
                    },
                },
            };
        }
    }
}
