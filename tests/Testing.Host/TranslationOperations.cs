using ActualChat.Hashing;

namespace ActualChat.Testing.Host;

public static class TranslationOperations
{
    extension(IWebTester tester)
    {
        public Task<Translation?> GetTranslation(
            ChatEntryId entryId,
            Language language,
            bool translateIfMissing,
            CancellationToken cancellationToken = default)
            => tester.GetTranslation(TranslationId.New(entryId, language), translateIfMissing, cancellationToken);

        public Task<Translation?> GetTranslation(
            TranslationId id,
            bool translateIfMissing,
            CancellationToken cancellationToken = default)
            => tester.Translations.Get(tester.Session, id, translateIfMissing, cancellationToken);

        public Task<ChatEntryLanguage?> GetEntryLanguage(
            ChatEntryId id,
            CancellationToken cancellationToken = default)
            => tester.Translations.GetLanguage(tester.Session, (ChatEntryId)id, cancellationToken);

        public Task<ChatEntryLanguage?> CreateEntryLanguage(
            ChatEntryId id,
            Language? language,
            HashString entryContentHash,
            CancellationToken cancellationToken = default)
            => tester.Commander.Call(new ChatEntryLanguagesBackend_Change(id, null, Change.Create(new ChatEntryLanguage(id) {
                Languages = language is not null ? [language] : [],
                EntryContentHash = entryContentHash,
            })), cancellationToken);

        public Task<ChatEntryLanguage> UpdateEntryLanguage(
            ChatEntryLanguage entryLanguage,
            CancellationToken cancellationToken = default)
            => tester.Commander.Call(
                new ChatEntryLanguagesBackend_Change(entryLanguage.Id, null, Change.Update(entryLanguage)),
                cancellationToken).Require();
    }
}
