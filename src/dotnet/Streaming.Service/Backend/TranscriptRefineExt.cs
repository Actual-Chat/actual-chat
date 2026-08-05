namespace ActualChat.Streaming;

internal static class TranscriptRefineExt
{
    public static bool ShouldUseOriginalTranscript(this string realtimeText, string refinedText)
    {
        // The refine engine (OpenAI) can hallucinate text in a different language/script than what was
        // actually said (e.g. Polish/Greek for Russian speech) on short or low-quality audio. The
        // real-time transcript reliably reflects the spoken language, so reject a refined text whose
        // dominant script differs from it; same-script refinements (legitimate wording fixes) pass through.
        if (refinedText.IsNullOrEmpty())
            return true;
        if (refinedText.GetDominantScript() != realtimeText.GetDominantScript())
            return true;
        if (refinedText.Length >= realtimeText.Length)
            return false;
        if (realtimeText.Length > 50)
            return refinedText.Length < 0.9 * realtimeText.Length;
        if (realtimeText.Length > 25)
            return refinedText.Length < 0.8 * realtimeText.Length;

        return true;
    }

    public static int CountWords(this string text)
    {
        // Chinese, Japanese and Thai write without spaces, so there are no word boundaries to count
        // and the character total stands in for one. Korean does space its words, hence not listed.
        var charsPerWord = text.GetDominantScript() switch {
            TextScript.Cjk => 1.5,
            TextScript.Thai => 3.0,
            _ => 0.0,
        };
        if (charsPerWord > 0)
            return (int)(text.Count(char.IsLetterOrDigit) / charsPerWord);

        var words = 0;
        var isInWord = false;
        foreach (var ch in text) {
            if (char.IsLetterOrDigit(ch)) {
                if (!isInWord)
                    words++;
                isInWord = true;
            }
            else if (char.IsWhiteSpace(ch))
                isInWord = false;
        }

        return words;
    }

    public static TextScript GetDominantScript(this string text)
    {
        Span<int> counts = stackalloc int[(int)TextScript.Other + 1];
        foreach (var ch in text) {
            if (char.IsLetter(ch))
                counts[(int)Classify(ch)]++;
        }
        var dominant = TextScript.None;
        var max = 0;
        for (var i = 1; i < counts.Length; i++) {
            if (counts[i] > max) {
                max = counts[i];
                dominant = (TextScript)i;
            }
        }

        return dominant;
    }

    private static TextScript Classify(char ch)
        => ch switch {
            >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                or >= 'À' and <= 'ɏ' // Latin-1 Supplement + Latin Extended-A/B
                or >= 'Ḁ' and <= 'ỿ' // Latin Extended Additional (Vietnamese)
                => TextScript.Latin,
            >= 'Ѐ' and <= 'ԯ' => TextScript.Cyrillic, // Cyrillic + Cyrillic Supplement
            >= 'Ͱ' and <= 'Ͽ' => TextScript.Greek, // Greek and Coptic
            >= '一' and <= '鿿' or >= '㐀' and <= '䶿' // CJK ideographs (Chinese, Japanese kanji)
                or >= '぀' and <= 'ヿ' // Hiragana + Katakana (Japanese)
                => TextScript.Cjk,
            >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏'
                => TextScript.Hangul, // Korean
            >= '฀' and <= '๿' => TextScript.Thai,
            >= 'ऀ' and <= 'ॿ' => TextScript.Devanagari, // Hindi, Marathi
            >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' => TextScript.Arabic, // Urdu
            >= '஀' and <= '௿' => TextScript.Tamil,
            _ => TextScript.Other,
        };
}

internal enum TextScript { None, Latin, Cyrillic, Greek, Cjk, Hangul, Thai, Devanagari, Arabic, Tamil, Other }
