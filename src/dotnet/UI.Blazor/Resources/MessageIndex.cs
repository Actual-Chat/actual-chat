using System.Text;
using System.Text.RegularExpressions;

namespace ActualChat.UI.Blazor.Resources;

// TODO(FC): review once more manually without claude
// TODO(FC): drop the validation half of this index - 11.0.0-preview.7 shipped the fix this was
// waiting for, and we are still on preview.6. The generated validator now resolves
// IStringLocalizerFactory per call from context.ServiceProvider (Validation/gen/Templates/
// ValidatableInfo.cs:83) instead of capturing it into the singleton ValidationOptions, so our
// scoped AppStringLocalizer reaches it; ValidationOptions.MessageKeyProvider then maps
// (attribute, member, type) to a forward key, leaving the BCL attributes unwrapped.
// Once the SDK is bumped, Validation_*_Format and this reverse matching go away and only
// Error_ server strings stay. See docs/plans/validation-localization-forward-keys.md §1.

/// <summary>
/// Reverse index over <c>Messages.en.json</c>: maps an English message produced at runtime
/// (a validator's output, a server error) back to its catalog key, either exactly or by
/// matching a <c>{name}</c>-template and extracting the arguments by name.
/// </summary>
public sealed partial class MessageIndex
{
    public const string ValidationPrefix = "Validation_";
    public const string ErrorPrefix = "Error_";
    // The placeholder carrying the field name - the only one a form label may replace.
    public const string FieldArg = "field";
    public static readonly string[] KnownPrefixes = [ValidationPrefix, ErrorPrefix];
    public static readonly MessageIndex Default = new(
        StringCatalogs.LoadMessages(Languages.English) ?? []);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRe { get; }

    private static readonly Dictionary<string, string> NoArgs = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _keyByMessage = new();
    private readonly List<MessageTemplate> _templates = [];

    public MessageIndex(IReadOnlyDictionary<string, string> messages)
    {
        var keyByTemplate = new Dictionary<string, string>();
        foreach (var (key, message) in messages) {
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
            return new MessageMatch(key, NoArgs);

        foreach (var template in _templates)
            if (template.TryMatch(message, out var match))
                return match;

        return null;
    }

    public static string Format(string template, IReadOnlyDictionary<string, string> args)
    {
        // A placeholder with no matching argument is left as is: a half-translated template
        // stays readable instead of throwing at render time.
        if (args.Count == 0)
            return template;

        return PlaceholderRe.Replace(template,
            m => args.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

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
        IReadOnlyList<string> ArgNames,
        int LiteralLength)
    {
        public static MessageTemplate New(string key, string template)
        {
            var pattern = new StringBuilder("^");
            var argNames = new List<string>();
            var literalLength = 0;
            var position = 0;
            foreach (Match placeholder in PlaceholderRe.Matches(template)) {
                var literal = template[position..placeholder.Index];
                if (literal.Length == 0 && argNames.Count != 0)
                    throw StandardError.Constraint(
                        $"'{key}' has adjacent placeholders, which makes the match ambiguous.");

                var name = placeholder.Groups[1].Value;
                if (argNames.Contains(name, StringComparer.Ordinal))
                    throw StandardError.Constraint($"'{key}' repeats the placeholder '{{{name}}}'.");

                pattern.Append(Regex.Escape(literal)).Append("(.+?)");
                argNames.Add(name);
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
                argNames,
                literalLength);
        }

        public bool TryMatch(string message, [NotNullWhen(true)] out MessageMatch? match)
        {
            match = null;
            var m = Re.Match(message);
            if (!m.Success)
                return false;

            var args = new Dictionary<string, string>(ArgNames.Count, StringComparer.Ordinal);
            for (var i = 0; i < ArgNames.Count; i++)
                args[ArgNames[i]] = m.Groups[i + 1].Value;

            match = new MessageMatch(Key, args);
            return true;
        }
    }
}

public sealed record MessageMatch(string Key, IReadOnlyDictionary<string, string> Args);
