using ActualChat.Chat;
using ActualChat.Transcription;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

// The voice a speaker is dubbed with: VoicePool's clone when the speaker opted in and one is
// acquirable, otherwise the stock voice - UserLanguageSettings.DubVoice when the catalog lists it,
// otherwise the first voice DubVoiceAccents suggests for the speaker's languages ("Default" in the
// picker). An unset voice ID means the synthesizer's default - guests, anonymous authors, an empty catalog, or any
// lookup failure: a voice is never worth failing a dub over
public sealed class SpeakerVoices(IServiceProvider services)
{
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ITranslationsBackend TranslationsBackend { get; } = services.GetRequiredService<ITranslationsBackend>();
    private VoicePool VoicePool { get; } = services.GetRequiredService<VoicePool>();
    private ILogger Log { get; } = services.LogFor<SpeakerVoices>();

    public async Task<SpeechSynthesisOptions> GetSynthesisOptions(
        ChatId chatId, AuthorId authorId, Language language, CancellationToken cancellationToken)
    {
        try {
            var (userId, settings) = await GetSettings(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (settings == null)
                return new SpeechSynthesisOptions(language);

            if (settings.IsOwnVoiceEnabled) {
                // A pool id is a Soniox UUID, not something the catalog would ever list - it skips
                // the ResolveVoice/catalog check below on purpose
                var cloneVoiceId = await VoicePool.Acquire(userId!, cancellationToken).ConfigureAwait(false);
                if (!cloneVoiceId.IsNullOrEmpty())
                    return new SpeechSynthesisOptions(language, cloneVoiceId) {
                        OnVoiceNotFound = voiceId => VoicePool.Recover(userId!, voiceId),
                    };
            }

            var voices = await TranslationsBackend.ListDubVoices(cancellationToken).ConfigureAwait(false);
            var chosenVoiceId = settings.DubVoice;
            if (!chosenVoiceId.IsNullOrEmpty() && !voices.Any(x => x.Id == chosenVoiceId))
                Log.LogInformation("Author {AuthorId} picked voice '{VoiceId}', which the catalog doesn't list",
                    authorId, chosenVoiceId);
            var voiceId = DubVoiceAccents.ResolveVoice(chosenVoiceId, voices, settings.ListSpoken());
            return new SpeechSynthesisOptions(language, voiceId);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogInformation(e, "Failed to read the voice of author {AuthorId}, using the default", authorId);
            return new SpeechSynthesisOptions(language);
        }
    }

    // Private methods

    private async Task<(UserId? UserId, UserLanguageSettings? Settings)> GetSettings(
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author == null || author.UserId.IsGuestOrNull() || author.IsAnonymous)
            return (null, null);

        var settings = await ServerKvasBackend.ForUser(author.UserId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return (author.UserId, settings);
    }
}
