namespace ActualChat.Chat;

/// <summary>
/// Cheap presence-based language detector that classifies characters by Unicode block.
/// Used as a stand-in for AI language detection in tests / dev environments.
/// </summary>
public static class AlphabetLanguageDetector
{
    public static IReadOnlyList<Language> Detect(string text)
    {
        if (text.IsNullOrEmpty())
            return [];

        var found = new HashSet<Language>();
        foreach (var ch in text) {
            var lang = Classify(ch);
            if (lang is { } v)
                found.Add(v);
        }
        return found.Count == 0 ? [] : [..found];
    }

    // A translation that shares no script with its target can't be one: the model answered in
    // another language instead of NO_TRANSLATION_NEEDED. Same-script pairs, e.g. English to
    // Spanish, can't be told apart here and stay with the prompt.
    public static bool IsScriptMismatch(string source, string translation, Language target)
    {
        if (GetScript(target) is not { } script)
            return false;
        if (Detect(source).Count == 0)
            return false;

        var scripts = Detect(translation);
        return scripts.Count > 0 && !scripts.Contains(script);
    }

    // Private methods

    // The script a translation into the language must contain, or null when it can't be told:
    // a script Classify doesn't know, or the CJK ideographs Chinese and Japanese share.
    private static Language? GetScript(Language language)
    {
        var scripts = Detect(language.NativeName);
        return scripts is [var script] && script != Languages.Chinese && script != Languages.Japanese
            ? script
            : null;
    }

    private static Language? Classify(char c)
    {
        // Latin (basic + extended)
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return Languages.English;
        if (c is >= '\u00C0' and <= '\u024F')
            return Languages.English;
        // Cyrillic
        if (c is >= '\u0400' and <= '\u04FF')
            return Languages.Russian;
        // Hangul (Korean)
        if (c is >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF')
            return Languages.Korean;
        // Hiragana / Katakana (Japanese)
        if (c is >= '\u3040' and <= '\u309F' or >= '\u30A0' and <= '\u30FF')
            return Languages.Japanese;
        // CJK Unified Ideographs (Chinese; falls through after kana check)
        if (c is >= '\u4E00' and <= '\u9FFF')
            return Languages.Chinese;
        // Devanagari (Hindi)
        if (c is >= '\u0900' and <= '\u097F')
            return Languages.Hindi;
        // Thai
        if (c is >= '\u0E00' and <= '\u0E7F')
            return Languages.Thai;
        return null;
    }
}
