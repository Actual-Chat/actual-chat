using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Resources;

public static class MessageLocalizer
{
    extension(IStringLocalizer l)
    {
        // Our own validation attributes report a Validation_* key rather than an English
        // sentence, so there is nothing to reverse-match - the key resolves directly.
        // Returns null for anything that isn't such a key.
        public string? ForValidationKey(string key, string fieldLabel = "")
        {
            if (!key.StartsWith(MessageIndex.ValidationPrefix, StringComparison.Ordinal))
                return null;

            var localized = l[key];
            if (localized.ResourceNotFound)
                return null;

            return MessageIndex.Format(localized.Value, l.FieldArgs(fieldLabel));
        }

        // Reverse-matches a message produced at runtime - a BCL validator's output, a server
        // error - back to its key, then resolves that key. The message must be the English one
        // MessageIndex was built from; anything else returns null.
        public string? ForRuntimeMessage(string message, string fieldLabel = "")
        {
            var match = MessageIndex.Default.Match(message);
            if (match == null)
                return null;

            var localized = l[match.Key];
            if (localized.ResourceNotFound)
                return null;

            // Without a label the field keeps the name the framework gave it - the member name.
            var args = match.Args;
            if (!fieldLabel.IsNullOrEmpty() && args.ContainsKey(MessageIndex.FieldArg))
                args = new Dictionary<string, string>(args, StringComparer.Ordinal) {
                    [MessageIndex.FieldArg] = fieldLabel,
                };

            return MessageIndex.Format(localized.Value, args);
        }

        // Private methods

        private Dictionary<string, string> FieldArgs(string fieldLabel)
            => fieldLabel.IsNullOrEmpty()
                ? []
                : new Dictionary<string, string>(StringComparer.Ordinal) {
                    [MessageIndex.FieldArg] = fieldLabel,
                };
    }
}
