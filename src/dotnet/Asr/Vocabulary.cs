using System.Text;
using System.Text.RegularExpressions;

namespace ActualChat.Asr;

/// <summary>Loads vocab.txt and maps token IDs to text.</summary>
public sealed partial class Vocabulary
{
    private static readonly Regex SpaceCleanupRegex = CreateSpaceCleanupRegex();

    private readonly Dictionary<int, string> _tokens;

    public int VocabSize { get; }
    public int BlankIndex { get; }

    public Vocabulary(string vocabPath)
    {
        _tokens = [];
        foreach (var line in File.ReadLines(vocabPath, Encoding.UTF8)) {
            if (line.IsNullOrWhiteSpace())
                continue;

            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace < 0)
                continue;

            var token = line[..lastSpace].Replace("\u2581", " ");
            var id = int.Parse(line[(lastSpace + 1)..]);
            _tokens[id] = token;
        }

        VocabSize = _tokens.Count;
        BlankIndex = _tokens.FirstOrDefault(kv => string.Equals(kv.Value, "<blk>")).Key;
        if (BlankIndex == 0 && !_tokens.ContainsKey(0))
            BlankIndex = VocabSize; // Convention: blank = vocab_size
    }

    public string GetToken(int id)
        => _tokens.GetValueOrDefault(id, "");

    /// <summary>Decodes a sequence of token IDs into readable text.</summary>
    public string DecodeTokens(IReadOnlyList<int> tokenIds)
    {
        if (tokenIds.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var id in tokenIds) {
            if (_tokens.TryGetValue(id, out var token))
                sb.Append(token);
        }

        var raw = sb.ToString();
        // Clean up spacing: remove leading space, remove space before non-word-boundary chars
        return SpaceCleanupRegex.Replace(raw, m => m.Groups[1].Success ? " " : "").Trim();
    }

    /// <summary>Decodes tokens and returns individual word results with timestamps.</summary>
    public IReadOnlyList<WordResult> DecodeTokensWithTimestamps(
        IReadOnlyList<int> tokenIds,
        IReadOnlyList<int> timestamps,
        float timeScale)
    {
        if (tokenIds.Count == 0)
            return [];

        var words = new List<WordResult>();
        var currentWord = new StringBuilder();
        var wordStart = -1f;
        var wordEnd = -1f;

        for (int i = 0; i < tokenIds.Count; i++) {
            if (!_tokens.TryGetValue(tokenIds[i], out var token))
                continue;

            var time = timestamps[i] * timeScale;

            // Token starts with space -> new word boundary
            if (token.Length > 0 && token[0] == ' ' && currentWord.Length > 0) {
                words.Add(new WordResult(currentWord.ToString(), wordStart, wordEnd));
                currentWord.Clear();
                wordStart = -1f;
            }

            currentWord.Append(token);
            if (wordStart < 0)
                wordStart = time;
            wordEnd = time;
        }

        if (currentWord.Length > 0)
            words.Add(new WordResult(currentWord.ToString(), wordStart, wordEnd));

        return words;
    }

    // Matches: leading whitespace | whitespace before non-word-boundary | captures (whitespace) before word-boundary
    [GeneratedRegex(@"\A\s|\s\B|(\s)\b")]
    private static partial Regex CreateSpaceCleanupRegex();
}
