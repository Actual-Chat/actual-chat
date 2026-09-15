using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

// The stock voice a speaker chose to be dubbed with (UserLanguageSettings.DubVoice); null means the
// synthesizer's default - for guests, anonymous authors, speakers who never picked one, an id the
// catalog no longer lists (the setting is client-written, so it's never handed to the provider
// unchecked), and any lookup failure: a voice is never worth failing a dub over
public sealed class SpeakerVoices(IServiceProvider services)
{
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ITranslationsBackend TranslationsBackend { get; } = services.GetRequiredService<ITranslationsBackend>();
    private ILogger Log { get; } = services.LogFor<SpeakerVoices>();

    public async Task<string?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        try {
            var voiceId = await GetChosen(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (voiceId == null)
                return null;

            var voices = await TranslationsBackend.ListDubVoices(cancellationToken).ConfigureAwait(false);
            if (voices.Any(x => x.Id == voiceId))
                return voiceId;

            Log.LogInformation("Author {AuthorId} picked voice '{VoiceId}', which the catalog doesn't list",
                authorId, voiceId);
            return null;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogInformation(e, "Failed to read the voice of author {AuthorId}, using the default", authorId);
            return null;
        }
    }

    // Private methods

    private async Task<string?> GetChosen(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author == null || author.UserId.IsGuestOrNull() || author.IsAnonymous)
            return null;

        var settings = await ServerKvasBackend.ForUser(author.UserId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return settings.DubVoice.NullIfEmpty();
    }
}
