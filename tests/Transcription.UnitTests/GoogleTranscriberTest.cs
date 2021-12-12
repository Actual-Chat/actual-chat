using ActualChat.Transcription.Internal;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf.WellKnownTypes;

namespace ActualChat.Transcription.UnitTests;

public class GoogleTranscriberTest : TestBase
{
    private ILogger<GoogleTranscriber> Log { get; }

    public GoogleTranscriberTest(ITestOutputHelper @out, ILogger<GoogleTranscriber> log) : base(@out)
        => Log = log;

    [Fact]
    public async Task DuplicateFinalResponsesTest()
    {
        var options = new TranscriptionOptions() {
            Language = "ru-RU",
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var process = new GoogleTranscriberProcess(options, null!, Log);
        await process.ProcessResponses(GenerateResponses(), CancellationToken.None);

        var updates = await process.GetUpdates().ToListAsync();
        updates.Max(update => update.UpdatedPart!.TimeRange.End).Should().Be(3.47d);
        updates.Min(update => update.UpdatedPart!.TextToTimeMap.TargetRange.Min).Should().Be(0d);

        var transcript = new Transcript();
        foreach (var update in updates) {
            transcript = transcript.WithUpdate(update);
            Out.WriteLine($"+ {update}");
            Out.WriteLine($"= {transcript}");
        }

        transcript.Text.Should().Be("проверка связи");
        transcript.TextToTimeMap.SourcePoints.Should()
            .Equal(new[] { 0d, 9, 14 }, (l, r) => Math.Abs(l - r) < 0.001);
        transcript.TextToTimeMap.TargetPoints.Should()
            .Equal(new[] { 0d, 1.3, 3.47 }, (l, r) => Math.Abs(l - r) < 0.0001);

        Log.LogInformation("Transcript: {Transcript}", transcript);

#pragma warning disable CS1998
        async IAsyncEnumerable<StreamingRecognizeResponse> GenerateResponses()
#pragma warning restore CS1998
        {
            yield return new StreamingRecognizeResponse {
                Results = {
                    new[] {
                        new StreamingRecognitionResult {
                            Alternatives = { new[] { new SpeechRecognitionAlternative { Transcript = "проверь" } } },
                            Stability = 0.01f,
                            ResultEndTime = new Duration { Seconds = 1, Nanos = 100_000_000 },
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
                            ResultEndTime = new Duration { Seconds = 1, Nanos = 220_000_000 },
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
                            ResultEndTime = new Duration { Seconds = 1, Nanos = 820_000_000 },
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
                            ResultEndTime = new Duration { Seconds = 2, Nanos = 840_000_000 },
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
                            ResultEndTime = new Duration { Seconds = 3, Nanos = 440_000_000 },
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
                                                    StartTime = new Duration { Seconds = 0, Nanos = 200_000_000 },
                                                    EndTime = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    EndTime = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerTag = 1,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndTime = new Duration { Seconds = 3, Nanos = 470_000_000 },
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
                                                    StartTime = new Duration { Seconds = 0, Nanos = 200_000_000 },
                                                    EndTime = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    EndTime = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new Duration { Seconds = 0, Nanos = 200_000_000 },
                                                    EndTime = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerTag = 1,
                                                },
                                                new WordInfo {
                                                    StartTime = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    EndTime = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerTag = 1,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndTime = new Duration { Seconds = 3, Nanos = 470_000_000 },
                            LanguageCode = "ru-ru",
                            IsFinal = true,
                        },
                    },
                },
            };
        }
    }

    [Fact]
    public async Task TextToTimeMapTest()
    {
        var options = new TranscriptionOptions() {
            Language = "ru-RU",
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var process = new GoogleTranscriberProcess(options, null!, Log);
        await process.ProcessResponses(GoogleTranscriptReader.ReadFromFile("transcript.json"), CancellationToken.None);

        var results = await process.GetUpdates().ToListAsync();
        var transcript = results[0].UpdatedPart!;
        foreach (var transcriptUpdate in results.Skip(1)) {
            Out.WriteLine(transcriptUpdate.ToString());
            transcript = transcript.WithUpdate(transcriptUpdate);
        }

        Out.WriteLine(transcript.ToString());
        transcript.TimeRange.End.Should().BeLessThan(23d);
    }
}
