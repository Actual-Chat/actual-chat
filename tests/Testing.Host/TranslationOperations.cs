using ActualChat.Chat;
using ActualChat.Hashing;

namespace ActualChat.Testing.Host;

public static class TranslationOperations
{
    public static Task<Translation?> GetTranslation(
        this IWebTester tester,
        TextEntryId entryId,
        Language language,
        CancellationToken cancellationToken = default)
        => tester.GetTranslation(TranslationId.New(entryId, language), cancellationToken);

    public static Task<Translation?> GetTranslation(
        this IWebTester tester,
        TranslationId id,
        CancellationToken cancellationToken = default)
        => tester.Translations.Get(tester.Session, id, cancellationToken);

    public static Task<ChatEntryLanguage?> GetEntryLanguage(
        this IWebTester tester,
        ChatEntryId id,
        CancellationToken cancellationToken = default)
        => tester.Translations.GetLanguage(tester.Session, (TextEntryId)id, cancellationToken);

    public static Task<ChatEntryLanguage?> CreateEntryLanguage(
        this IWebTester tester,
        TextEntryId id,
        Language language,
        HashString entryContentHash,
        CancellationToken cancellationToken = default)
        => tester.Commander.Call(new ChatEntryLanguagesBackend_Change(id, null, Change.Create(new ChatEntryLanguage(id) {
            Languages = [language],
            EntryContentHash = entryContentHash,
        })), cancellationToken);

    public static Task<ChatEntryLanguage> UpdateEntryLanguage(
        this IWebTester tester,
        ChatEntryLanguage entryLanguage,
        CancellationToken cancellationToken = default)
        => tester.Commander.Call(
            new ChatEntryLanguagesBackend_Change(entryLanguage.Id, null, Change.Update(entryLanguage)),
            cancellationToken).Require();
}
