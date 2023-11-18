using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Google.Cloud.Speech.V2;
using Google.Protobuf.WellKnownTypes;

namespace ActualChat.Transcription.UnitTests;

public static class GoogleTranscriptReader
{
    private static readonly JsonSerializerOptions _serializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "<Pending>")]
    public static async IAsyncEnumerable<StreamingRecognizeResponse> ReadFromFile(string fileName)
    {
        await using var fileStream = File.OpenRead(fileName);
        var responses = await JsonSerializer.DeserializeAsync<Response[]>(
                fileStream,
                _serializerOptions)
            .ConfigureAwait(false);

        foreach (var response in responses!) {
            var googleResponse = new StreamingRecognizeResponse {
                Metadata = new RecognitionResponseMetadata {
                    TotalBilledDuration = response.TotalBilledTime.FromString(),
                },
                Results = { response.Results.Select(r =>
                    new StreamingRecognitionResult {
                        IsFinal = r.IsFinal,
                        Stability = r.Stability,
                        ResultEndOffset = r.ResultEndOffset.FromString(),
                        Alternatives = {
                            r.Alternatives.Select(a =>
                                new SpeechRecognitionAlternative {
                                    Confidence = a.Confidence,
                                    Transcript = a.Transcript,
                                    Words = {
                                        a.Words == null ? Array.Empty<WordInfo>() : a.Words.Select(w =>
                                            new WordInfo {
                                                Word = w.Word,
                                                StartOffset = w.StartOffset.FromString(),
                                                EndOffset = w.EndOffset.FromString(),
                                            }),
                                    },
                                }
                            ),
                        },
                    }) },
            };
            yield return googleResponse;
        }
    }

    public static Duration? FromString(this string? duration)
    {
        if (duration.IsNullOrEmpty())
            return null;

        var (sec, ms, _) = duration.TrimEnd('s').Split('.');
        return new Duration { Seconds = long.Parse(sec, CultureInfo.InvariantCulture), Nanos = int.Parse(ms ?? "0", CultureInfo.InvariantCulture) * 1_000_000 };
    }
}


#pragma warning disable CA2227
public class Response
{
    public ReadOnlyCollection<Result> Results { get; set; } = null!;
    public string? TotalBilledTime { get; set; }
}

public class Result
{
    public ReadOnlyCollection<Alternative> Alternatives { get; set; } = null!;
    public float Stability { get; set; }
    public bool IsFinal { get; set; }
    public string ResultEndOffset { get; set; } = null!;
}

public class Alternative
{
    public string Transcript { get; set; } = null!;
    public float Confidence { get; set; }
    public ReadOnlyCollection<WordN>? Words { get; set; }
}

public class WordN
{
    public string StartOffset { get; set; } = null!;
    public string EndOffset { get; set; } = null!;
    public string Word { get; set; } = null!;
    public string SpeakerLabel { get; set; } = null!;
}
#pragma warning restore CA2227
