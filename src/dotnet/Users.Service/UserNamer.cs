using System.ComponentModel.DataAnnotations;

namespace ActualChat.Users;

public class UserNamer
{
    public virtual ValidationException? ValidateName(in ReadOnlySpan<char> name)
    {
        if (name.Length == 0)
            return new ValidationException("Name is empty.");
        if (name.Length < 4)
            return new ValidationException("Name is too short.");
        if (!char.IsLetter(name[0]))
            return new ValidationException("Name must start with a letter.");
        foreach (var c in name[1..]) {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                return new ValidationException("Name may contain only letters, digits, '-' and '_'.");
        }
        return null;
    }

    public virtual ReadOnlySpan<char> ParseName(ReadOnlySpan<char> text)
    {
        if (text.Length < 4)
            return ReadOnlySpan<char>.Empty;
        if (!char.IsLetter(text[0]))
            return ReadOnlySpan<char>.Empty;
        var i = 1;
        for (; i < text.Length; i++) {
            var c = text[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                return ReadOnlySpan<char>.Empty;
        }
        var name = text[..i];
        return ValidateName(name) == null ? name : ReadOnlySpan<char>.Empty;
    }
}
