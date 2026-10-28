using ActualChat.Transcription;

namespace ActualChat.Streaming;

internal static class TranscriptRefineExt
{
    public static bool ShouldUseOriginalTranscript(
        this string realtimeText,
        string refinedText,
        TranscriberId refinedBy = default)
    {
        // Retranscription wins unless empty or much shorter. Soniox gets looser bars and no script check: a broken
        // realtime pass is often the longer text, while other engines (OpenAI) can hallucinate another script.
        if (refinedText.IsNullOrEmpty())
            return true;

        var isSoniox = refinedBy == TranscriberId.SonioxOffline;
        if (!isSoniox && refinedText.GetDominantScript() != realtimeText.GetDominantScript())
            return true;

        var isShort = realtimeText.Length < 50;
        var minLengthRatio = isSoniox
            ? isShort ? 0.6 : 0.8
            : isShort ? 0.8 : 0.9;
        return refinedText.Length < minLengthRatio * realtimeText.Length;
    }
}
