using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Resources;

public static class MessageLocalizer
{
    extension(IStringLocalizer l)
    {
        public string? TryMessage(string message, string fieldLabel = "")
        {
            var match = MessageIndex.Default.Match(message);
            if (match == null)
                return null;

            var args = match.Args;
            if (match.HasFieldArg && args.Length != 0)
                args[0] = l.FieldName(args[0], fieldLabel);
            var localized = args.Length == 0 ? l[match.Key] : l[match.Key, args];
            return localized.ResourceNotFound ? null : localized.Value;
        }

        // Private methods

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
