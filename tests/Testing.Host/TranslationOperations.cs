using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class TranslationOperations
{
    public static Task<Translation?> GetTranslation(
        this IWebTester tester,
        ChatEntryId entryId,
        Language language,
        CancellationToken cancellationToken = default)
        => tester.GetTranslation(new TranslationId(entryId, language, AssumeValid.Option), cancellationToken);

    public static Task<Translation?> GetTranslation(
        this IWebTester tester,
        TranslationId id,
        CancellationToken cancellationToken = default)
        => tester.Translations.Get(tester.Session, id, cancellationToken);

    public static Task<ChatEntryLanguage?> GetEntryLanguage(
        this IWebTester tester,
        ChatEntryId id,
        CancellationToken cancellationToken = default)
        => tester.Translations.GetLanguage(tester.Session, id, cancellationToken);
}
