namespace ActualChat.Transcription;

// Soniox splits context into `general` key-values and a free-form `text` block; the rendered
// summary maps onto a "topic" entry and the rendered prefix onto `text`.

public static class SonioxContext
{
    public static SonioxContextPayload? Build(
        TranscriptionContext context,
        TranscriptionContextPolicy? policy,
        TimeSpan? audioDuration = null)
    {
        if (policy == null || context.IsNone)
            return null;

        var text = context.Render(policy.GetMaxChars(audioDuration));
        if (text.IsNone)
            return null;

        return new SonioxContextPayload {
            General = text.Summary.IsNullOrEmpty()
                ? null
                : [new SonioxContextGeneralItem { Key = "topic", Value = text.Summary }],
            Text = text.Text.NullIfEmpty(),
        };
    }
}
