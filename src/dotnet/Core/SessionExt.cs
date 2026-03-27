using ActualLab.Generators;

namespace ActualChat;

public static class SessionExt
{
    private static readonly Generator<string> ApiKeyGenerator
        = new RandomStringGenerator(32, Alphabet.AlphaNumericDash.Symbols);

    public static Session? NewValidOrNull(string? sessionId)
    {
        if (sessionId.IsNullOrEmpty())
            return null;

        try {
            var session = new Session(sessionId);
            return session.IsValid() ? session : null;
        }
        catch {
            return null;
        }
    }

    public static Session NewApiKey()
        => new (CoreConstants.Session.ApiKeyPrefix + ApiKeyGenerator.Next());

    extension(Session session)
    {
        public SessionKind Kind
            => session.Id.StartsWith(CoreConstants.Session.ApiKeyPrefix)
                ? SessionKind.ApiKey
                : SessionKind.Session;

        public string IdPrefix
            => session.Id.Length >= CoreConstants.Session.IdPrefixLength
                ? session.Id[..CoreConstants.Session.IdPrefixLength]
                : session.Id;

        public bool HasIdPrefix(string idPrefix)
            => session.Id.StartsWith(idPrefix, StringComparison.Ordinal);
    }
}
