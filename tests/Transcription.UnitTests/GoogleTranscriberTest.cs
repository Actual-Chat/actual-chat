using ActualChat.Module;
using ActualChat.Transcription.Google;
using Google.Cloud.Speech.V2;
using Google.Protobuf.WellKnownTypes;

namespace ActualChat.Transcription.UnitTests;

public class GoogleTranscriberTest : TestBase
{
    private ILogger<GoogleTranscriber> Log { get; }
    private ServiceProvider Services { get; set; }

    // ReSharper disable once ContextualLoggerProblem
    public GoogleTranscriberTest(ITestOutputHelper @out, ILogger<GoogleTranscriber> log) : base(@out)
    {
        Log = log;
        Services = new ServiceCollection()
            .AddSingleton(new CoreServerSettings { GoogleProjectId = "n/a" })
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton<GoogleTranscriber>()
            .ConfigureLogging(Out)
            .BuildServiceProvider();
    }

    [Fact]
    public async Task DuplicateFinalResponsesTest()
    {
        var transcriber = Services.GetRequiredService<GoogleTranscriber>();
        await transcriber.WhenInitialized;
        var responses = GenerateResponses();
        var state = new GoogleTranscribeState(null!, null!, null!);
        var transcripts = await transcriber.ProcessResponses(state, responses).ToListAsync();

        var transcript = transcripts.Last();
        transcript.Text.Should().Be("Проверка связи проверка связи");
        transcript.TimeMap.IsValid().Should().BeTrue();

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
                            ResultEndOffset = new Duration { Seconds = 3, Nanos = 100_000_000 },
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
                            ResultEndOffset = new Duration { Seconds = 3, Nanos = 220_000_000 },
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
                            ResultEndOffset = new Duration { Seconds = 3, Nanos = 820_000_000 },
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
                                                    StartOffset = new Duration { Seconds = 2, Nanos = 200_000_000 },
                                                    EndOffset = new Duration { Seconds = 3, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 3, Nanos = 300_000_000 },
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
                                                    StartOffset = new Duration { Seconds = 2, Nanos = 200_000_000 },
                                                    EndOffset = new Duration { Seconds = 3, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 3, Nanos = 300_000_000 },
                                                    EndOffset = new Duration { Seconds = 2, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 2, Nanos = 200_000_000 },
                                                    EndOffset = new Duration { Seconds = 3, Nanos = 300_000_000 },
                                                    Word = "проверка", SpeakerLabel = "1",
                                                },
                                                new WordInfo {
                                                    StartOffset = new Duration { Seconds = 3, Nanos = 300_000_000 },
                                                    EndOffset = new Duration { Seconds = 4, Nanos = 900_000_000 },
                                                    Word = "связи", SpeakerLabel = "1",
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            ResultEndOffset = new Duration { Seconds = 5, Nanos = 470_000_000 },
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
        var transcriber = Services.GetRequiredService<GoogleTranscriber>();
        await transcriber.WhenInitialized;
        var state = new GoogleTranscribeState(null!, null!, null!);
        var responses = GoogleTranscriptReader.ReadFromFile("data/transcript.json");
        var transcripts = await transcriber.ProcessResponses(state, responses).ToListAsync();

        var transcript = transcripts.Last();
        Out.WriteLine(transcript.ToString());
        transcript.TimeRange.End.Should().BeLessThan(23f);
    }

    [Fact]
    public async Task LongTranscriptProducesCorrectDiff()
    {
        var transcriber = Services.GetRequiredService<GoogleTranscriber>();
        await transcriber.WhenInitialized;
        var state = new GoogleTranscribeState(null!, null!, null!);
        var responses = GoogleTranscriptReader.ReadFromFile("data/long-transcript.json");
        var transcripts = transcriber.ProcessResponses(state, responses);

        var memoizedTranscripts = transcripts.Memoize();
        var diffs = memoizedTranscripts.Replay().ToTranscriptDiffs();
        var memoizedDiffs = diffs.Memoize();
        await foreach (var diff in memoizedDiffs.Replay())
            Out.WriteLine(diff.ToString());

        var transcript = await memoizedTranscripts.Replay().LastAsync();
        var restoredTranscript = await memoizedDiffs.Replay().ToTranscripts().LastAsync();

        transcript.Text.Should().Be(restoredTranscript.Text);
        transcript.TimeMap.IsValid().Should().BeTrue();
    }
}
