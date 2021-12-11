using System.Text.Json.Serialization;
using Cysharp.Text;

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
    public double Duration => TextToTimeMap.TargetPoints[^1];

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

        var commonLength = Math.Min(text.Length, updatedText.Length);
        var commonPrefixLength = 0;
        for (; commonPrefixLength < commonLength; commonPrefixLength++) {
            if (text[commonPrefixLength] != updatedText[commonPrefixLength])
                break;
        }
        var updatePointIndex = updatedMap.SourcePoints.IndexOfLowerOrEqual(commonPrefixLength);
        if (updatePointIndex <= 0)
            return new TranscriptUpdate(updated); // Everything differs
        var updateTextStart = (int) updatedMap.SourcePoints[updatePointIndex];
        return new TranscriptUpdate(new Transcript(
            updatedText[updateTextStart..],
            updatedMap[updatePointIndex..]));
    }

    public Transcript WithSuffix(string suffix, double suffixEndTime)
    {
        var suffixTextToTimeMap = new LinearMap(
            new []{ (double) Text.Length, Text.Length + suffix.Length },
            new []{ Duration, suffixEndTime }
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

    // This record relies on referential equality
    public bool Equals(Transcript? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
