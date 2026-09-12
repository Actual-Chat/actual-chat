namespace ActualChat.UI.Blazor.Services;

public class PasskeyUI(UIHub hub) : UIServiceBase<UIHub>(hub), IComputeService
{
    private Task<bool>? _whenClientAvailable;

    private IPasskeyAuth PasskeyAuth => field ??= Services.GetRequiredService<IPasskeyAuth>();
    private IPasskeyClient Client => field ??= Services.GetRequiredService<IPasskeyClient>();

    [ComputeMethod]
    public virtual async Task<bool> CanUse(CancellationToken cancellationToken)
    {
        if (!await PasskeyAuth.IsEnabled(cancellationToken).ConfigureAwait(false))
            return false;

        return await IsClientAvailable(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual Task<ApiArray<Passkey>> ListOwn(CancellationToken cancellationToken)
        => PasskeyAuth.ListOwn(Session, cancellationToken);

    public async Task<bool> SignIn(CancellationToken cancellationToken = default)
    {
        var (optionsJson, beginError) = await UICommander
            .Run(new PasskeyAuth_BeginSignIn { Session = Session }, cancellationToken)
            .ConfigureAwait(false);
        if (beginError != null || optionsJson.IsNullOrEmpty())
            return false;

        string assertionJson;
        try {
            assertionJson = await Client.Get(optionsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (PasskeyCancelledException) {
            return false;
        }

        var (_, error) = await UICommander
            .Run(new PasskeyAuth_CompleteSignIn { Session = Session, AssertionJson = assertionJson }, cancellationToken)
            .ConfigureAwait(false);
        return error == null;
    }

    public async Task<Passkey?> Register(CancellationToken cancellationToken = default)
    {
        var (optionsJson, beginError) = await UICommander
            .Run(new PasskeyAuth_BeginRegistration { Session = Session }, cancellationToken)
            .ConfigureAwait(false);
        if (beginError != null || optionsJson.IsNullOrEmpty())
            return null;

        string attestationJson;
        try {
            attestationJson = await Client.Create(optionsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (PasskeyCancelledException) {
            return null;
        }

        var command = new PasskeyAuth_CompleteRegistration { Session = Session, AttestationJson = attestationJson };
        var (passkey, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error == null ? passkey : null;
    }

    public async Task<bool> Rename(string id, string name, CancellationToken cancellationToken = default)
    {
        var command = new PasskeyAuth_Rename { Session = Session, Id = id, Name = name };
        var (_, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error == null;
    }

    public async Task<bool> Delete(string id, CancellationToken cancellationToken = default)
    {
        var command = new PasskeyAuth_Delete { Session = Session, Id = id };
        var (_, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error == null;
    }

    // Private methods

    private Task<bool> IsClientAvailable(CancellationToken cancellationToken)
    {
        // Cached per scope: the answer is a property of the device, not of the moment
        var whenAvailable = _whenClientAvailable;
        if (whenAvailable is { IsCompletedSuccessfully: true })
            return whenAvailable;

        return _whenClientAvailable = Probe();

        async Task<bool> Probe() {
            try {
                return await Client.IsAvailable(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogWarning(e, "IsClientAvailable: probe failed");
                return false;
            }
        }
    }
}
