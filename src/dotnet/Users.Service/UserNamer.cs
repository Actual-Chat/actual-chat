using System.ComponentModel.DataAnnotations;
using Cysharp.Text;

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
            if (!IsValidCharacter(c))
                return new ValidationException("Name may contain only letters, digits, '-', '_' and spaces.");
        }
        return null;
    }

    public virtual string NormalizeName(string name)
    {
        if (ValidateName(name) == null)
            return name;
        // Normalizing name
        using var sb = ZString.CreateStringBuilder();
        foreach (var c in name) {
            if (IsValidCharacter(c))
                sb.Append(c);
            else if (sb.Length == 0 || char.IsLetterOrDigit(sb.AsSpan()[^1]))
                sb.Append('_');
        }
        name = sb.ToString();
        if (name.Length < 4 || !char.IsLetter(name[0]))
            name = "user-" + name;
        return name;
    }

    private static bool IsValidCharacter(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ';
}
