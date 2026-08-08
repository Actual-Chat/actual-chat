using System.Text;
using System.Text.RegularExpressions;

namespace ActualChat.UI.Blazor.Resources;

// TODO(FC): review once more manually without claude
/// <summary>
/// Reverse index over <c>Messages.en.json</c>: maps an English message produced at runtime
/// (a validator's output, a server error) back to its catalog key, either exactly or by
/// matching a <c>{n}</c>-template and extracting the arguments.
/// </summary>
public sealed partial class MessageIndex
{
    public const string ValidationPrefix = "Validation_";
    public const string ErrorPrefix = "Error_";
    public const string FieldPrefix = "Field_";
    public static readonly string[] KnownPrefixes = [ValidationPrefix, ErrorPrefix, FieldPrefix];
    public static readonly MessageIndex Default = new(
        StringCatalog.Load(StringCatalog.MessagesPrefix, Languages.English.IsoCode) ?? []);

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderRe { get; }

    private readonly Dictionary<string, string> _keyByMessage = new();
    private readonly Dictionary<string, string> _keyByFieldName = new();
    private readonly List<MessageTemplate> _templates = [];

    public MessageIndex(IReadOnlyDictionary<string, string> messages)
    {
        var keyByTemplate = new Dictionary<string, string>();
        foreach (var (key, message) in messages) {
            if (key.StartsWith(FieldPrefix)) {
                Add(_keyByFieldName, message, key);
                continue;
            }
            if (!KnownPrefixes.Any(key.StartsWith))
                throw StandardError.Constraint(
                    $"'{key}' must start with one of: {KnownPrefixes.ToDelimitedString()}.");

            if (!PlaceholderRe.IsMatch(message)) {
                Add(_keyByMessage, message, key);
                continue;
            }

            Add(keyByTemplate, message, key);
            _templates.Add(MessageTemplate.New(key, message));
        }
        _templates.Sort((x, y) => y.LiteralLength.CompareTo(x.LiteralLength));
    }

    public MessageMatch? Match(string message)
    {
        if (_keyByMessage.TryGetValue(message, out var key))
            return new MessageMatch(key, []);

        foreach (var template in _templates)
            if (template.TryMatch(message, out var match))
                return match;

        return null;
    }

    public string? GetFieldKey(string fieldName)
        => _keyByFieldName.GetValueOrDefault(fieldName);

    // Private methods

    private static void Add(Dictionary<string, string> index, string message, string key)
    {
        if (!index.TryAdd(message, key))
            throw StandardError.Constraint(
                $"'{key}' and '{index[message]}' share the English value \"{message}\".");
    }

    // Nested types

    private sealed record MessageTemplate(
        string Key,
        Regex Re,
        int[] ArgIndexes,
        int ArgCount,
        int LiteralLength,
        bool HasFieldArg)
    {
        public static MessageTemplate New(string key, string template)
        {
            var pattern = new StringBuilder("^");
            var argIndexes = new List<int>();
            var literalLength = 0;
            var position = 0;
            foreach (Match placeholder in PlaceholderRe.Matches(template)) {
                var literal = template[position..placeholder.Index];
                if (literal.Length == 0 && argIndexes.Count != 0)
                    throw StandardError.Constraint(
                        $"'{key}' has adjacent placeholders, which makes the match ambiguous.");

                pattern.Append(Regex.Escape(literal)).Append("(.+?)");
                argIndexes.Add(int.Parse(placeholder.Groups[1].Value));
                literalLength += literal.Length;
                position = placeholder.Index + placeholder.Length;
            }
            var tail = template[position..];
            literalLength += tail.Length;
            if (literalLength == 0)
                throw StandardError.Constraint($"'{key}' has no literal text to match on.");

            pattern.Append(Regex.Escape(tail)).Append('$');
            return new MessageTemplate(
                key,
                new Regex(pattern.ToString(), RegexOptions.Singleline),
                argIndexes.ToArray(),
                argIndexes.Max() + 1,
                literalLength,
                key.StartsWith(ValidationPrefix));
        }

        public bool TryMatch(string message, [NotNullWhen(true)] out MessageMatch? match)
        {
            match = null;
            var m = Re.Match(message);
            if (!m.Success)
                return false;

            var args = new string[ArgCount];
            for (var i = 0; i < ArgIndexes.Length; i++)
                args[ArgIndexes[i]] = m.Groups[i + 1].Value;
            match = new MessageMatch(Key, args, HasFieldArg);
            return true;
        }
    }
}

public sealed record MessageMatch(string Key, string[] Args, bool HasFieldArg = false);
