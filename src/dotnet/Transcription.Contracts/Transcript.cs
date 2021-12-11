using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Cysharp.Text;
using Stl.Internal;

namespace ActualChat.Transcription;

[DataContract]
public sealed record Transcript
{
    public const double TextToTimeMapTextPrecision = 0.5d;
    public const double TextToTimeMapTimePrecision = 0.1d;
    public static LinearMap EmptyMap { get; } = new(new[] { 0d, 0 }, new[] { 0d, 0d });

    [DataMember(Order = 0)]
    public string Text { get; init; } = "";
    [DataMember(Order = 1)]
    public LinearMap TextToTimeMap { get; init; } = EmptyMap;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<double> TextRange => TextToTimeMap.SourceRange;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<double> TimeRange => TextToTimeMap.TargetRange;

    public Transcript() { }
    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public Transcript(string text, LinearMap textToTimeMap)
    {
        Text = text;
        TextToTimeMap = textToTimeMap;
    }

    public TranscriptUpdate GetUpdateTo(Transcript updated)
    {
        var map = TextToTimeMap;
        var text = Text;
        var updatedMap = updated.TextToTimeMap;
        var updatedText = updated.Text;
        if (Math.Abs(map.SourceRange.Min - updatedMap.SourceRange.Min) > 0.1)
            return new TranscriptUpdate(updated); // Start differs

        // The logic below is fairly complex, but overall:
        // - We find the max. common part of the map and the text
        // - Take shorter(commonMap, commonText) as our common prefix
        // - Try to augment commonMap with one extra point - (commonTextEnd, itsTime)
        // - Compute the updated map as (commonText, commonMap) + suffix from (updatedText, updatedMap)

        var commonMapPrefixLength = Math.Min(
            map.SourcePoints.CommonPrefixLength(updatedMap.SourcePoints),
            map.TargetPoints.CommonPrefixLength(updatedMap.TargetPoints));
        if (commonMapPrefixLength == 0)
            return new TranscriptUpdate(updated);
        var commonTextPrefixLengthBasedOnMap = (int) map.SourcePoints[commonMapPrefixLength - 1];

        var commonTextPrefixLength = text.AsSpan().CommonPrefixLength(updatedText.AsSpan());
        var lastCommonMapPointIndex = map.SourcePoints.IndexOfLowerOrEqual(commonTextPrefixLength - 0.5);
        if (lastCommonMapPointIndex < 0)
            return new TranscriptUpdate(updated);
        var commonMapPrefixLengthBasedOnText = lastCommonMapPointIndex + 1;

        var textPrefix = text[..Math.Min(commonTextPrefixLength, commonTextPrefixLengthBasedOnMap)];
        var mapPrefix = map[..Math.Min(commonMapPrefixLength, commonMapPrefixLengthBasedOnText)];
        if (mapPrefix.IsEmpty)
            return new TranscriptUpdate(updated);
        var mapSuffix = updatedMap[mapPrefix.Length..];
        updatedMap = mapPrefix
            .TryAppend(textPrefix.Length, map.Map(textPrefix.Length)!.Value, TextToTimeMapTextPrecision)
            .AppendOrUpdateTail(mapSuffix, TextToTimeMapTextPrecision);

        var transcript = new Transcript(updatedText, updatedMap);
        transcript.Validate(); // TODO(AY): Remove this call once we see everything is fine
        return new TranscriptUpdate(transcript);
    }

    public Transcript WithSuffix(string suffix, double suffixEndTime)
    {
        var suffixTextToTimeMap = new LinearMap(
            new [] { (double) Text.Length, Text.Length + suffix.Length },
            new [] { TimeRange.End, suffixEndTime }
        ).TrySimplifyToPoint();
        return WithSuffix(suffix, suffixTextToTimeMap);
    }

    public Transcript WithSuffix(string suffix, LinearMap suffixTextToTimeMap)
    {
        var updatedText = ZString.Concat(Text, suffix);
        var updatedMap = TextToTimeMap.AppendOrUpdateTail(suffixTextToTimeMap, TextToTimeMapTextPrecision);
        return new Transcript(updatedText, updatedMap);
    }

    public Transcript WithUpdate(TranscriptUpdate update)
    {
        var updatedPart = update.UpdatedPart;
        if (updatedPart == null)
            return this;

        var updatedPartMap = updatedPart.TextToTimeMap;
        var cutIndex = (int) updatedPartMap.SourceRange.Min;
        var retainedText = Text;
        if (cutIndex > Text.Length)
            retainedText += new string(' ', cutIndex - retainedText.Length);
        else
            retainedText = retainedText[..cutIndex];
        return new Transcript() {
            Text = retainedText + updatedPart.Text,
            TextToTimeMap = TextToTimeMap.AppendOrUpdateTail(updatedPartMap, TextToTimeMapTextPrecision),
        };
    }

    public async Task<Transcript> WithUpdates(
        IAsyncEnumerable<TranscriptUpdate> transcriptStream,
        CancellationToken cancellationToken)
    {
        var transcript = this;
        await foreach (var update in transcriptStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            transcript = transcript.WithUpdate(update);
        return transcript;
    }

    public void Validate()
    {
        if (Text.Length == 0) {
            if (!TextToTimeMap.IsEmpty)
                throw Errors.InternalError("TextToTimeMap must be empty.");
        }
        else {
            TextToTimeMap.Validate();
            if (TextToTimeMap.SourcePoints[0] != 0)
                throw Errors.InternalError("TextToTimeMap start must be 0.");
            if (Text.Length != (int) TextToTimeMap.SourcePoints[^1])
                throw Errors.InternalError("TextToTimeMap end must match Text's end.");
        }
    }

    // This record relies on referential equality
    public bool Equals(Transcript? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
