using ActualChat.UI.Blazor.Services;
using Android.Gms.Common;
using Android.OS;
using Android.Runtime;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using AndroidX.Credentials.Exceptions.DomErrors;
using AndroidX.Credentials.Exceptions.PublicKeyCredential;
using Java.Util.Concurrent;

namespace ActualChat.App.Maui;

public sealed class AndroidPasskeyClient(IServiceProvider services) : IPasskeyClient
{
    private ICredentialManager Manager { get; } = CredentialManager.Create(Platform.AppContext);
    private IExecutorService Executor { get; } = services.GetRequiredService<IExecutorService>();

    public Task<bool> IsAvailable(CancellationToken cancellationToken)
    {
        // Native flows don't work with a host override, see NativeGoogleAuth
        if (MauiSettings.IsHostOverridden)
            return Task.FromResult(false);

        var status = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Platform.AppContext);
        return Task.FromResult(status == ConnectionResult.Success);
    }

    public async Task<string> Create(string optionsJson, CancellationToken cancellationToken)
    {
        var request = new CreatePublicKeyCredentialRequest(optionsJson);
        var callback = new Callback<CreatePublicKeyCredentialResponse>();
        using var signal = new CancellationSignal();
        using var _ = cancellationToken.Register(signal.Cancel);
        Manager.CreateCredentialAsync(MainActivity.Current, request, signal, Executor, callback);
        // A cancelled signal makes the provider return without calling back, hence WaitAsync
        var response = await callback.WhenCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
        return response.RegistrationResponseJson;
    }

    public async Task<string> Get(string optionsJson, CancellationToken cancellationToken)
    {
        var request = new GetCredentialRequest.Builder()
            .AddCredentialOption(new GetPublicKeyCredentialOption(optionsJson))
            .Build();
        var callback = new Callback<GetCredentialResponse>();
        using var signal = new CancellationSignal();
        using var _ = cancellationToken.Register(signal.Cancel);
        Manager.GetCredentialAsync(MainActivity.Current, request, signal, Executor, callback);
        var response = await callback.WhenCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (response.Credential is not PublicKeyCredential credential)
            throw StandardError.External($"Unexpected credential type: {response.Credential.Type}.");

        return credential.AuthenticationResponseJson;
    }

    // Nested types

    private sealed class Callback<TResponse> : Java.Lang.Object, ICredentialManagerCallback
        where TResponse : Java.Lang.Object
    {
        private readonly TaskCompletionSource<TResponse> _source = TaskCompletionSourceExt.New<TResponse>();

        public Task<TResponse> WhenCompleted => _source.Task;

        public void OnResult(Java.Lang.Object? result)
        {
            try {
                if (result?.JavaCast<TResponse>() is { } response)
                    _source.TrySetResult(response);
                else
                    _source.TrySetException(StandardError.External("Passkey ceremony returned nothing."));
            }
            catch (Exception e) {
                _source.TrySetException(e);
            }
        }

        public void OnError(Java.Lang.Object? error)
        {
            try {
                _source.TrySetException(ToException(error));
            }
            catch (Exception e) {
                _source.TrySetException(e);
            }
        }

        // Private methods

        private static Exception ToException(Java.Lang.Object? error)
            // Throwable isn't a Java.Lang.Object on the .NET side, so the callback's argument
            // arrives as a plain wrapper and has to be re-cast to reach the bound exception type.
            // The DOM errors are what a cancelled biometric prompt surfaces as, like on the web.
            => error?.JavaCast<Java.Lang.Throwable>() switch {
                GetCredentialCancellationException or CreateCredentialCancellationException or NoCredentialException
                    => new PasskeyCancelledException(),
                GetPublicKeyCredentialDomException { DomError: NotAllowedError or AbortError }
                    => new PasskeyCancelledException(),
                CreatePublicKeyCredentialDomException { DomError: NotAllowedError or AbortError }
                    => new PasskeyCancelledException(),
                { } throwable => StandardError.External(throwable.Message.NullIfEmpty() ?? throwable.ToString()),
                null => StandardError.External("Passkey ceremony failed."),
            };
    }
}
