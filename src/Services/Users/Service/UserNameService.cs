using System;
using System.ComponentModel.DataAnnotations;

namespace ActualChat.Users
{
    public class UserNameService : IUserNameService
    {
        public ValidationException? ValidateName(in ReadOnlySpan<char> name)
        {
            if (name.Length == 0)
                return new ValidationException("Name is empty.");
            if (name.Length < 4)
                return  new ValidationException("Name is too short.");
            if (!char.IsLetter(name[0]))
                return  new ValidationException("Name must start with a letter.");
            foreach (var c in name[1..]) {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    return new ValidationException("Name may contain only letters, digits, '-' and '_'.");
            }
            return null;
        }

        public ReadOnlySpan<char> ParseName(ref ReadOnlySpan<char> text)
        {
            var name = text;
            for (var i = 0; i < text.Length; i++) {
                var c = text[i];
                if (i == 0) {
                    if (char.IsLetter(c))
                        continue;
                    return ReadOnlySpan<char>.Empty;
                }
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    continue;
                name = text[..i];
            }
            if (ValidateName(name) == null) {
                text = text[name.Length..];
                return name;
            }
            return ReadOnlySpan<char>.Empty;
        }
    }
}
