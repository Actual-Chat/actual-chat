namespace ActualChat.Chat;

public static class TranslationDubExt
{
    public static bool HasValidDub(this Translation translation)
        => translation.DubMediaId != null
            && translation.DubContentHash == ChatEntryHashExt.GetContentHashString(translation.Content);
}
