using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.Users;

/// <summary>
/// Resolves the <see cref="IStringLocalizer"/> for composing text a given user will read -
/// notification bodies, email chrome. Server-side: there is no circuit to take a language from.
/// </summary>
public sealed class UserLocalizers(IServiceProvider services)
{
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();

    public async ValueTask<IStringLocalizer> Get(UserId userId, CancellationToken cancellationToken)
    {
        // Not Primary: that is a spoken language. Guests get defaults, so English.
        var settings = await ServerKvasBackend.ForUser(userId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var language = settings.UILanguage ?? settings.DetectedUILanguage ?? Languages.Main;
        return LanguageStringLocalizer.Get(language);
    }
}
