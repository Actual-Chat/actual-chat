using ActualChat.Kvas;
using CommunityToolkit.HighPerformance.Buffers;

namespace ActualChat.Users;

/// <summary>
/// Server-side implementation of <see cref="IUserSettings"/>.
/// Translates between <see cref="StoredSettings"/> and raw bytes stored in <see cref="IServerKvasBackend"/>.
/// </summary>
public class UserSettings(IServiceProvider services) : IUserSettings
{
    private static readonly Dictionary<string, Type> KeyToType = new() {
        [nameof(UserAppSettings)] = typeof(UserAppSettings),
        [nameof(UserEmailsSettings)] = typeof(UserEmailsSettings),
        [nameof(UserLanguageSettings)] = typeof(UserLanguageSettings),
        [nameof(UserListeningSettings)] = typeof(UserListeningSettings),
        [nameof(UserNavbarSettings)] = typeof(UserNavbarSettings),
        [nameof(UserReactionSettings)] = typeof(UserReactionSettings),
        [nameof(UserAvatarSettings)] = typeof(UserAvatarSettings),
        [nameof(UserTranscriptionEngineSettings)] = typeof(UserTranscriptionEngineSettings),
        [nameof(UserOnboardingSettings)] = typeof(UserOnboardingSettings),
        [nameof(UserBubbleSettings)] = typeof(UserBubbleSettings),
        [nameof(UserChatRecordingDetectedLanguage)] = typeof(UserChatRecordingDetectedLanguage),
        [nameof(UserTranscodingTestSettings)] = typeof(UserTranscodingTestSettings),
        [nameof(FakeDeviceContactOptions)] = typeof(FakeDeviceContactOptions),
        [nameof(UserReplaySettings)] = typeof(UserReplaySettings),
        [nameof(UserPttSettings)] = typeof(UserPttSettings),
        [nameof(UserNotificationsPanelSettings)] = typeof(UserNotificationsPanelSettings),
        [nameof(RecentMentions)] = typeof(RecentMentions),
        [nameof(RecentGifs)] = typeof(RecentGifs),
    };

    private static KvasSerializer Serializer => Kvas.KvasExt.Serializer;

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IServerKvasBackend KvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<UserSettings>();

    // [ComputeMethod]
    public virtual async Task<StoredSettings?> Get(Session session, string key, CancellationToken cancellationToken = default)
    {
        if (KvasKeys.IsHidden(key))
            return null;

        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var data = await KvasBackend.Get(prefix, key, cancellationToken).ConfigureAwait(false);
        return Deserialize(data, key);
    }

    // [CommandHandler]
    public virtual async Task OnSet(UserSettings_Set command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, key, value) = command;
        KvasKeys.RequireNotHidden(key);
        // Null for a singleton key = an unknown union tag deserialized to null; deleting the row
        // would silently wipe the settings. Parameterized "@" keys keep null-as-delete.
        if (value is null && KeyToType.ContainsKey(key))
            throw StandardError.Constraint($"A null value is not allowed for the '{key}' settings key.");

        value?.ValidateKey(key);

        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var data = Serialize(value);
        var setManyCommand = new ServerKvasBackend_SetMany(prefix, (key, data));
        await Commander.Call(setManyCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask<string> GetPrefix(Session session, CancellationToken cancellationToken)
        => await GetUserPrefix(session, cancellationToken).ConfigureAwait(false)
            ?? await GetGuestPrefix(session, cancellationToken).ConfigureAwait(false)
            ?? "";

    private async ValueTask<string?> GetUserPrefix(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return !account.IsGuest
            ? UserScopedKvasBackend.GetUserPrefix(account.Id)
            : null;
    }

    private async ValueTask<string?> GetGuestPrefix(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var guestId = sessionInfo.GetGuestId();
        return guestId is null ? null : UserScopedKvasBackend.GetUserPrefix(guestId);
    }

    private StoredSettings? Deserialize(byte[]? data, string key)
    {
        if (data is null)
            return null;

        try {
            // Use concrete type for deserialization to stay compatible with IServerKvas/IKvas data format
            var type = ResolveType(key);
            return (StoredSettings?)Serializer.Read(data, type, out _);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to deserialize settings for key '{Key}' ({Length} bytes), treating as missing",
                key, data.Length);
            return null;
        }
    }

    private static byte[]? Serialize(StoredSettings? value)
    {
        if (value is null)
            return null;

        // Serialize using the concrete type to stay compatible with IServerKvas/IKvas readers
        using var buffer = new ArrayPoolBufferWriter<byte>(ArrayPools.SharedBytePool, 1024);
        Serializer.Write(buffer, value, value.GetType());
        return buffer.WrittenMemory.ToArray();
    }

    private static Type ResolveType(string key)
    {
        if (key.Length == 0)
            return typeof(StoredSettings);

        // Parameterized keys start with @
        if (key[0] == '@') {
            if (key.StartsWith(ChatUserSettings.KeyPrefix))
                return typeof(ChatUserSettings);
            if (key.StartsWith(AddChatMembersBannerUserSettings.KeyPrefix))
                return typeof(AddChatMembersBannerUserSettings);
            if (key.StartsWith(PttJoinBannerUserSettings.KeyPrefix))
                return typeof(PttJoinBannerUserSettings);

            return typeof(StoredSettings);
        }

        // Simple keys: the key is the type name
        return KeyToType.GetValueOrDefault(key) ?? typeof(StoredSettings);
    }
}
