namespace ActualChat.Asr;

public sealed record TranscriptionResult(string Text, IReadOnlyList<WordResult> Words)
{
    /// <summary>Groups words into sentences by splitting on sentence-ending punctuation.</summary>
    public IReadOnlyList<SentenceResult> GetSentences()
    {
        if (Words.Count == 0)
            return [];

        var sentences = new List<SentenceResult>();
        var currentWords = new List<WordResult>();

        foreach (var word in Words) {
            currentWords.Add(word);
            var trimmed = word.Text.TrimEnd();
            if (trimmed.Length > 0 && ".!?".Contains(trimmed[^1])) {
                var text = string.Join("", currentWords.Select(w => w.Text));
                sentences.Add(new SentenceResult(text, currentWords[0].StartTime, currentWords[^1].EndTime));
                currentWords = [];
            }
        }

        if (currentWords.Count > 0) {
            var text = string.Join("", currentWords.Select(w => w.Text));
            sentences.Add(new SentenceResult(text, currentWords[0].StartTime, currentWords[^1].EndTime));
        }

        return sentences;
    }
}

public sealed record WordResult(string Text, float StartTime, float EndTime);

public sealed record SentenceResult(string Text, float StartTime, float EndTime);

public sealed record PartialTranscription(string FixedText, string ActiveText, float Timestamp, bool IsFinal);
