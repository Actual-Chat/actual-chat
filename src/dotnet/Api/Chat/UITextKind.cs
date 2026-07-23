namespace ActualChat.Chat;

// Kind of UI text passed to ITranslations.GetTranslatedUIText. The server maps it to a
// translation prompt hint, so the set of hints stays server-authored and the compute-cache
// key cardinality stays bounded (no free-form client-supplied hints).
public enum UITextKind
{
    // TODO: since now we have only Error, then remove unused
    ErrorMessage = 0,
    Label = 1,
    Title = 2,
}
