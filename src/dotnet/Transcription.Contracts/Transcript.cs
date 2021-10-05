using System.Text.Json.Serialization;
using ActualChat.Mathematics;

namespace ActualChat.Transcription;

[DataContract]
public record Transcript
{
    public static LinearMap EmptyMap { get; } = new(new[] { 0d, 1 }, new[] { 0d, 0d });

    [DataMember(Order = 0)]
    public string Text { get; init; } = "";
    [DataMember(Order = 1)]
    public LinearMap TextToTimeMap { get; init; } = EmptyMap;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public double Duration => TextToTimeMap.TargetPoints[^1];

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
            TextToTimeMap = TextToTimeMap.AppendOrUpdateTail(updatedPartMap).Simplify(0.1),
        };
    }
}
