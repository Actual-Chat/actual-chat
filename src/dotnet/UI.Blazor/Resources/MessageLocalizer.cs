using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Resources;

public static class MessageLocalizer
{
    extension(IStringLocalizer l)
    {
        // Our own validation attributes report a catalog key rather than an English sentence,
        // so there is nothing to reverse-match - the key resolves directly.
        // TODO: maybe relate the name to validation? be clear
        public string? TryLocalizeKey(string key, string fieldLabel = "")
        {
            if (!key.StartsWith(MessageIndex.ValidationPrefix, StringComparison.Ordinal))
                return null;

            var localized = l[key];
            if (localized.ResourceNotFound)
                return null;

            return MessageIndex.Format(localized.Value, l.FieldArgs(fieldLabel));
        }

        // Reverse-matches English produced at runtime (a BCL validator, a server error) back to
        // its key, then resolves that key.
        // TODO: name - very vague! is it about message? or what? be clear
        public string? TryLocalizeEnglish(string message, string fieldLabel = "")
        {
            var match = MessageIndex.Default.Match(message);
            if (match == null)
                return null;

            var localized = l[match.Key];
            if (localized.ResourceNotFound)
                return null;

            var args = match.Args;
            if (args.TryGetValue(MessageIndex.FieldArg, out var displayName))
                args = new Dictionary<string, string>(args, StringComparer.Ordinal) {
                    [MessageIndex.FieldArg] = l.FieldName(displayName, fieldLabel),
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

        private string FieldName(string displayName, string fieldLabel)
        {
            if (!fieldLabel.IsNullOrEmpty())
                return fieldLabel;

            var key = MessageIndex.Default.GetFieldKey(displayName);
            if (key == null)
                return displayName;

            var localized = l[key];
            return localized.ResourceNotFound ? displayName : localized.Value;
        }
    }
}
