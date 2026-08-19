using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Resources;

/// <summary>
/// Resolves a catalog key whose value lists plural forms separated by '|', in the order
/// <see cref="PluralRules"/> indexes them. A language that needs fewer forms lists fewer:
/// the index is clamped, so the last form is also the one the English fallback lands on.
/// </summary>
public static class PluralLocalizerExt
{
    private const char FormSeparator = '|';

    public static string Plural(this IStringLocalizer l, string key, long count)
        => GetForm(l, key, count);

    public static string Plural(this IStringLocalizer l, string key, long count, object arg)
        => string.Format(GetForm(l, key, count), arg);

    // Private methods

    private static string GetForm(IStringLocalizer l, string key, long count)
    {
        var localized = l[key];
        if (localized.ResourceNotFound)
            return localized.Value;

        var forms = localized.Value.Split(FormSeparator);
        var index = Math.Min(((IHasUILanguage)l).UILanguage.GetPluralFormIndex(count), forms.Length - 1);
        return forms[index];
    }
}
