namespace ActualChat.UI.Blazor.App.Services;

public static class LanguagePickerExt
{
    public static Language[] WithSelected(this Language[] options, Language? selected)
        // A picker keeps showing a selection it doesn't offer: set on a newer device, it can be
        // a LanguageSupport.Preview language, or one this build doesn't know at all
        => selected is null || options.Contains(selected) ? options : [..options, selected];
}
