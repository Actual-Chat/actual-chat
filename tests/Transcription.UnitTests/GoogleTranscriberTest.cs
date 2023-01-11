using ActualChat.Transcription.Google;
using Google.Cloud.Speech.V2;
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
        var process = new GoogleTranscriberProcess(null!, null!, null!, null!, Log);
        await process.ProcessResponses(GenerateResponses(), CancellationToken.None);

        var transcripts = await process.GetTranscripts().ToListAsync();
        transcripts.Min(t => t.TimeRange.Start).Should().Be(0f);
        transcripts.Max(t => t.TimeRange.End).Should().Be(3.47f);
        var transcript = transcripts.ApplyDiffs().Last();

        transcript.Text.Should().Be("проверка связи");
        var points = transcript.TextToTimeMap.Points.ToArray();
        points.Select(p => p.X).Should()
            .Equal(new[] { 0f, 8, 9, 14 }, (l, r) => Math.Abs(l - r) < 0.001);
        points.Select(p => p.Y).Should()
            .Equal(new[] { 0.2f, 1.3, 1.3, 3.47 }, (l, r) => Math.Abs(l - r) < 0.0001);

        Log.LogInformation("Transcript={Transcript}", transcript);

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
                            ResultEndOffset = new Duration { Seconds = 1, Nanos = 100_000_000 },
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
                            ResultEndOffset = new Duration { Seconds = 1, Nanos = 220_000_000 },
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
                            ResultEndOffset = new Duration { Seconds = 1, Nanos = 820_000_000 },
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
                            ResultEndOffset = new Duration { Seconds = 2, Nanos = 840_000_000 },
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
                            ResultEndOffset = new Duration { Seconds = 3, Nanos = 440_000_000 },
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
                                                    StartOffset = new Duration { Seconds = 0, Nanos = 200_000_000 },
                                                    EndOffset = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    EndOffset = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerLabel = "1",
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndOffset = new Duration { Seconds = 3, Nanos = 470_000_000 },
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
                                                    StartOffset = new Duration { Seconds = 0, Nanos = 200_000_000 },
                                                    EndOffset = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    EndOffset = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 0, Nanos = 200_000_000 },
                                                    EndOffset = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 1, Nanos = 300_000_000 },
                                                    EndOffset = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerLabel = "1",
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndOffset = new Duration { Seconds = 3, Nanos = 470_000_000 },
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
        var process = new GoogleTranscriberProcess(null!, null!, null!, null!, Log);
        await process.ProcessResponses(GoogleTranscriptReader.ReadFromFile("transcript.json"), CancellationToken.None);

        var transcripts = await process.GetTranscripts().ToListAsync();
        var transcript = transcripts.ApplyDiffs().Last();
        Out.WriteLine(transcript.ToString());
        transcript.TimeRange.End.Should().BeLessThan(23f);
    }
}
