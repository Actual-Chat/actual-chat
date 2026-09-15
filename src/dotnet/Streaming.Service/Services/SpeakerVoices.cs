using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

// The stock voice a speaker is dubbed with: UserLanguageSettings.DubVoice when the catalog lists it (the
// setting is client-written, so it's never handed to the provider unchecked), otherwise the first voice
// DubVoiceAccents suggests for the speaker's languages - that is what "Default" means in the picker.
// Null means the synthesizer's default - for guests, anonymous authors, an empty catalog, and any
// lookup failure: a voice is never worth failing a dub over
public sealed class SpeakerVoices(IServiceProvider services)
{
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ITranslationsBackend TranslationsBackend { get; } = services.GetRequiredService<ITranslationsBackend>();
    private ILogger Log { get; } = services.LogFor<SpeakerVoices>();

    public async Task<string?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        try {
            var settings = await GetSettings(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (settings == null)
                return null;

            var voices = await TranslationsBackend.ListDubVoices(cancellationToken).ConfigureAwait(false);
            var chosenVoiceId = settings.DubVoice;
            if (!chosenVoiceId.IsNullOrEmpty() && !voices.Any(x => x.Id == chosenVoiceId))
                Log.LogInformation("Author {AuthorId} picked voice '{VoiceId}', which the catalog doesn't list",
                    authorId, chosenVoiceId);
            return DubVoiceAccents.ResolveVoice(chosenVoiceId, voices, settings.ListSpoken());
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogInformation(e, "Failed to read the voice of author {AuthorId}, using the default", authorId);
            return null;
        }
    }

    // Private methods

    private async Task<UserLanguageSettings?> GetSettings(
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author == null || author.UserId.IsGuestOrNull() || author.IsAnonymous)
            return null;

        return await ServerKvasBackend.ForUser(author.UserId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
    }
}
