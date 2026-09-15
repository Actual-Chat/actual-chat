using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

// The stock voice a speaker chose to be dubbed with (UserLanguageSettings.DubVoice); null means the
// synthesizer's default - for guests, anonymous authors and speakers who never picked one
public sealed class SpeakerVoices(IServiceProvider services)
{
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();

    public async Task<string?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
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
