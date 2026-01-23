using System.ComponentModel.DataAnnotations;

namespace ActualChat.Users;

public sealed class UserNamer
{
    public ValidationException? ValidateName(in ReadOnlySpan<char> name)
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

    public string NormalizeName(string name)
    {
        if (ValidateName(name) == null)
            return name;

        // Name doesn't pass validation, generate a prefix
        var generatedName = RandomNameGenerator.Default.Generate();
        return $"{generatedName} {name}".Trim();
    }

    private static bool IsValidCharacter(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ';
}
