using System.Buffers.Text;
using System.Security.Cryptography;
using ActualChat.Hashing;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;
using ActualLab.Rpc.Infrastructure;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace ActualChat.Users.Passkeys;

public class PasskeyAuth : DbServiceBase<UsersDbContext>, IPasskeyAuth
{
    private const string CreateChallengeKeyPrefix = ".PasskeyChallenge:create:";
    private const string GetChallengeKeyPrefix = ".PasskeyChallenge:get:";
    private const string VerificationFailedMessage = "This passkey couldn't be verified. Please try again.";
    private const int UserHandleLength = 32;

    private UsersSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private IFido2 Fido2 { get; }
    private RateLimitPolicy RateLimitPolicy { get; }
    private RedisDb<UsersDbContext> RedisDb { get; }
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IPasskeysBackend PasskeysBackend => field ??= Services.GetRequiredService<IPasskeysBackend>();

    public PasskeyAuth(IServiceProvider services) : base(services)
    {
        Settings = services.GetRequiredService<UsersSettings>();
        HostInfo = services.HostInfo();
        Fido2 = services.GetRequiredService<IFido2>();
        RateLimitPolicy = services.GetRequiredService<RateLimitPolicy>();
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
    }

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(Settings.IsPasskeyAuthEnabled ?? !HostInfo.IsProductionInstance);

    // [ComputeMethod]
    public virtual Task<string> GetRpId(CancellationToken cancellationToken)
        => Task.FromResult(Settings.GetPasskeyRpId(HostInfo));

    // [ComputeMethod]
    public virtual async Task<ApiArray<Passkey>> ListOwn(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return ApiArray<Passkey>.Empty;

        var credentials = await PasskeysBackend.List(account.Id, cancellationToken).ConfigureAwait(false);
        return credentials.Select(x => x.ToPasskey()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<string> OnBeginRegistration(
        PasskeyAuth_BeginRegistration command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnBeginRegistration), session, cancellationToken).ConfigureAwait(false);
        if (command.Purpose != PasskeyPurpose.AddToAccount)
            throw StandardError.NotSupported("Creating an account with a passkey isn't available yet.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var existing = await PasskeysBackend.List(account.Id, cancellationToken).ConfigureAwait(false);
        var userHandle = existing.Count > 0
            ? existing[0].UserHandle
            : RandomNumberGenerator.GetBytes(UserHandleLength);
        var user = new Fido2User {
            Id = userHandle,
            Name = account.Email.IsNullOrEmpty() ? account.Name : account.Email,
            DisplayName = account.Name,
        };
        var options = Fido2.RequestNewCredential(new RequestNewCredentialParams {
            User = user,
            ExcludeCredentials = existing
                .Select(x => new PublicKeyCredentialDescriptor(Base64Url.DecodeFromChars(x.Id)))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = options.ToJson();
        var name = command.Name.IsNullOrWhiteSpace() ? null : command.Name.Trim();
        var pending = new PendingRegistration(account.Id, json, name);
        await StoreChallenge(CreateChallengeKeyPrefix, session, JsonSerializer.Serialize(pending), cancellationToken)
            .ConfigureAwait(false);
        return json;
    }

    // [CommandHandler]
    public virtual async Task<Passkey> OnCompleteRegistration(
        PasskeyAuth_CompleteRegistration command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnCompleteRegistration), session, cancellationToken).ConfigureAwait(false);
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);

        var pendingJson = await ConsumeChallenge(CreateChallengeKeyPrefix, session, cancellationToken)
            .ConfigureAwait(false);
        var pending = Deserialize<PendingRegistration>(pendingJson);
        if (pending.AccountId != account.Id)
            throw StandardError.Unauthorized("This passkey request was started for another account.");

        var options = CredentialCreateOptions.FromJson(pending.OptionsJson);
        var attestation = Deserialize<AuthenticatorAttestationRawResponse>(command.AttestationJson);
        RegisteredPublicKeyCredential registered;
        try {
            registered = await Fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
                AttestationResponse = attestation,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (p, ct) => {
                    var identity = UserIdentityExt.NewPasskeyIdentity(Base64Url.EncodeToString(p.CredentialId));
                    var ownerId = await AccountsBackend.GetIdByUserIdentity(identity, ct).ConfigureAwait(false);
                    return ownerId is null;
                },
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Fido2VerificationException e) {
            Log.LogWarning(e, "Passkey registration failed: {Code}", e.Code);
            throw StandardError.Unauthorized(VerificationFailedMessage);
        }

        var transports = string.Join(',', (registered.Transports ?? []).Select(x => x.ToString().ToLower()));
        var credential = new PasskeyCredential(Base64Url.EncodeToString(registered.Id), account.Id) {
            UserHandle = options.User.Id,
            PublicKey = registered.PublicKey,
            SignCount = registered.SignCount,
            Aaguid = registered.AaGuid,
            Transports = transports,
            IsBackupEligible = registered.IsBackupEligible,
            IsBackedUp = registered.IsBackedUp,
            Name = pending.Name ?? PasskeyNames.GetDefault(registered.AaGuid, transports),
            CreatedAt = Clocks.SystemClock.Now,
        };
        var changeCommand = new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential));
        var stored = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        return stored!.ToPasskey();
    }

    // [CommandHandler]
    public virtual async Task<string> OnBeginSignIn(
        PasskeyAuth_BeginSignIn command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnBeginSignIn), session, cancellationToken).ConfigureAwait(false);
        var options = Fido2.GetAssertionOptions(new GetAssertionOptionsParams {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var json = options.ToJson();
        await StoreChallenge(GetChallengeKeyPrefix, session, json, cancellationToken).ConfigureAwait(false);
        return json;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnCompleteSignIn(
        PasskeyAuth_CompleteSignIn command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return false;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnCompleteSignIn), session, cancellationToken).ConfigureAwait(false);

        var optionsJson = await ConsumeChallenge(GetChallengeKeyPrefix, session, cancellationToken)
            .ConfigureAwait(false);
        var options = AssertionOptions.FromJson(optionsJson);
        var assertion = Deserialize<AuthenticatorAssertionRawResponse>(command.AssertionJson);
        var credentialId = Base64Url.EncodeToString(assertion.RawId);
        var identity = UserIdentityExt.NewPasskeyIdentity(credentialId);
        var userId = await AccountsBackend.GetIdByUserIdentity(identity, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<Passkey>("This passkey isn't linked to an account.");
        var stored = await PasskeysBackend.Get(userId, credentialId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<Passkey>("This passkey isn't linked to an account.");

        VerifyAssertionResult verified;
        try {
            verified = await Fido2.MakeAssertionAsync(new MakeAssertionParams {
                AssertionResponse = assertion,
                OriginalOptions = options,
                StoredPublicKey = stored.PublicKey,
                StoredSignatureCounter = stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _)
                    => Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(stored.UserHandle)),
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Fido2VerificationException e) {
            Log.LogWarning(e, "Passkey sign-in failed: {Code}", e.Code);
            throw StandardError.Unauthorized(VerificationFailedMessage);
        }
        // Fido2NetLib skips the counter check when the assertion carries 0, but WebAuthn §7.2 step 21
        // rejects that once the stored counter is nonzero - and 0 must never overwrite a real counter
        if (stored.SignCount > 0 && verified.SignCount == 0) {
            Log.LogWarning("Passkey sign-in failed: zero counter for {CredentialId}", credentialId);
            throw StandardError.Unauthorized(VerificationFailedMessage);
        }

        var used = stored with {
            SignCount = Math.Max(stored.SignCount, verified.SignCount),
            IsBackedUp = verified.IsBackedUp,
            LastUsedAt = Clocks.SystemClock.Now,
        };
        var changeCommand = new PasskeysBackend_Change(userId, credentialId, Change.Update(used));
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        var identities = new ApiMap<UserIdentity, string>().With(identity, "");
        var signInCommand = new AccountsBackend_SignIn(session, identity, identities, new ApiMap<string, string>());
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // [CommandHandler]
    public virtual async Task OnRename(PasskeyAuth_Rename command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, id, name) = (command.Session, command.Id, command.Name.Trim());
        if (name.IsNullOrEmpty() || name.Length > 64)
            throw StandardError.Constraint("Passkey name must be 1 to 64 characters long.");

        var (account, stored) = await GetOwnPasskey(session, id, cancellationToken).ConfigureAwait(false);
        var changeCommand = new PasskeysBackend_Change(account.Id, id, Change.Update(stored with { Name = name }));
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDelete(PasskeyAuth_Delete command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, id) = (command.Session, command.Id);
        var (account, _) = await GetOwnPasskey(session, id, cancellationToken).ConfigureAwait(false);
        var passkeys = await PasskeysBackend.List(account.Id, cancellationToken).ConfigureAwait(false);
        var hasOtherIdentity = account.Identities.Keys.Any(x => x.Schema != AuthSchema.Passkey);
        if (passkeys.Count == 1 && !hasOtherIdentity)
            throw StandardError.Constraint(
                "This is the only way to sign in to your account. Add a phone or email first.");

        var changeCommand = new PasskeysBackend_Change(account.Id, id, Change.Remove<PasskeyCredential>());
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<(AccountFull Account, PasskeyCredential Passkey)> GetOwnPasskey(
        Session session, string id, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var stored = await PasskeysBackend.Get(account.Id, id, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<Passkey>();
        return (account, stored);
    }

    private async Task RequireEnabled(CancellationToken cancellationToken)
    {
        if (!await IsEnabled(cancellationToken).ConfigureAwait(false))
            throw StandardError.NotSupported("Passkeys aren't enabled.");
    }

    private async Task CheckRateLimit(string method, Session session, CancellationToken cancellationToken)
    {
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(RateLimitIdentityKind.Session, Hash(session.Id));
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check(
                $"{nameof(PasskeyAuth)}.{method}",
                RateLimitClass.Auth,
                identities.AsSpan(0, identityCount),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StoreChallenge(
        string prefix,
        Session session,
        string value,
        CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await db.StringSetAsync(prefix + Hash(session.Id), value, Settings.PasskeyChallengeLifetime)
            .ConfigureAwait(false);
    }

    private async Task<string> ConsumeChallenge(string prefix, Session session, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var value = await db.StringGetDeleteAsync(prefix + Hash(session.Id)).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
            throw StandardError.Constraint("This passkey request has expired. Please try again.");

        return (string)value!;
    }

    private static T Deserialize<T>(string json)
    {
        try {
            return JsonSerializer.Deserialize<T>(json) ?? throw StandardError.Constraint("Invalid passkey response.");
        }
        catch (JsonException) {
            throw StandardError.Constraint("Invalid passkey response.");
        }
    }

    private static string Hash(string value)
        => value.Hash().SHA256().ToBase64HashString(Hashing.HashAlgorithm.SHA256);

    // Nested types

    // The account and name are fixed at BeginRegistration and ride along with the challenge: the session
    // may sign in as someone else before completion, and the options were built for the original account
    private sealed record PendingRegistration(UserId AccountId, string OptionsJson, string? Name);
}
