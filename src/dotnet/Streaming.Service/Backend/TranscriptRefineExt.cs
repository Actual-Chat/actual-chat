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

    public static TextScript GetDominantScript(this string text)
    {
        int latin = 0, cyrillic = 0, greek = 0, other = 0;
        foreach (var ch in text) {
            if (!char.IsLetter(ch))
                continue;
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= 'À' and <= 'ɏ')
                latin++;
            else if (ch is >= 'Ѐ' and <= 'ӿ')
                cyrillic++;
            else if (ch is >= 'Ͱ' and <= 'Ͽ')
                greek++;
            else
                other++;
        }
        var max = Math.Max(Math.Max(latin, cyrillic), Math.Max(greek, other));
        if (max == 0)
            return TextScript.None;
        if (max == cyrillic)
            return TextScript.Cyrillic;
        if (max == latin)
            return TextScript.Latin;
        if (max == greek)
            return TextScript.Greek;
        return TextScript.Other;
    }
}

internal enum TextScript { None, Latin, Cyrillic, Greek, Other }
