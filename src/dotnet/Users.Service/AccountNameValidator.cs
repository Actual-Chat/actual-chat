using System.Text.RegularExpressions;

namespace ActualChat.Users;

public sealed partial class AccountNameValidator
{
    private const int MinNameLength = 4;

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"[\p{Cc}\p{Cf}\p{Co}\p{Cs}\u200B-\u200D\uFEFF\u00AD]", RegexOptions.Compiled)]
    private static partial Regex InvisibleCharsRegex();

#pragma warning disable CA1822
    public string? Validate(in ReadOnlySpan<char> name)
#pragma warning restore CA1822
    {
        if (name.Length == 0)
            return "Name is empty.";
        if (name.Length < MinNameLength)
            return "Name is too short.";
        if (name[0] == ' ')
            return "Name starts with a space.";
        if (name[^1] == ' ')
            return "Name ends with a space.";

        foreach (var c in name)
            if (IsInvisibleOrControlChar(c))
                return "Name contains invisible or control characters.";

        for (var i = 1; i < name.Length; i++)
            if (name[i] == ' ' && name[i - 1] == ' ')
                return "Name contains consecutive spaces.";

        return null;
    }

    public string Normalize(string name)
    {
        // If name is valid, return it
        if (Validate(name) == null)
            return name;

        // If name is valid after cleanup, return it
        name = InvisibleCharsRegex().Replace(name, "");
        name = MultipleSpacesRegex().Replace(name, " ");
        name = name.Trim();
        if (Validate(name) == null)
            return name;

        // If name is too short or doesn't start with a letter, prepend generated name
        var generatedName = RandomNameGenerator.Default.Generate();
        if (name.Length == 0)
            return generatedName;

        return $"{generatedName} {name}";
    }

    private static bool IsInvisibleOrControlChar(char c)
    {
        // Control characters (newlines, tabs, etc.), format characters (RTL override, etc.),
        // private use, and surrogates
        if (char.IsControl(c) || char.GetUnicodeCategory(c) is
            UnicodeCategory.Format or
            UnicodeCategory.PrivateUse or
            UnicodeCategory.Surrogate)
            return true;

        // Zero-width characters and other invisible formatting
        return c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u00AD';
    }
}
