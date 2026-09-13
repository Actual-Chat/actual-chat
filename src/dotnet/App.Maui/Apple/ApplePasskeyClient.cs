using ActualChat.UI.Blazor.Services;
using AuthenticationServices;
using Foundation;
#if MACOS
using AppKit;
#else
using UIKit;
#endif

namespace ActualChat.App.Maui;

public sealed class ApplePasskeyClient : IPasskeyClient
{
    public Task<bool> IsAvailable(CancellationToken cancellationToken)
        // The RP id comes from the server, and only the webcredentials: domains in the entitlements
        // pass the association check - a host override points elsewhere
        => Task.FromResult(!MauiSettings.IsHostOverridden);

    public async Task<string> Create(string optionsJson, CancellationToken cancellationToken)
    {
        var options = PasskeyJson.ParseCreationOptions(optionsJson);
        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(options.RpId);
        var request = provider.CreateCredentialRegistrationRequest(
            NSData.FromArray(options.Challenge), options.UserName, NSData.FromArray(options.UserId));
        request.UserVerificationPreference = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required;
        var authorization = await Run(request, cancellationToken).ConfigureAwait(false);
        var registration = authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialRegistration>()
            ?? throw StandardError.External("Passkey ceremony returned an unexpected credential.");
        var attestationObject = registration.RawAttestationObject
            ?? throw StandardError.External("Passkey ceremony returned no attestation.");
        return PasskeyJson.Attestation(
            registration.CredentialId.ToArray(),
            registration.RawClientDataJson.ToArray(),
            attestationObject.ToArray());
    }

    public async Task<string> Get(string optionsJson, CancellationToken cancellationToken)
    {
        var options = PasskeyJson.ParseRequestOptions(optionsJson);
        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(options.RpId);
        var request = provider.CreateCredentialAssertionRequest(NSData.FromArray(options.Challenge));
        request.UserVerificationPreference = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required;
        var authorization = await Run(request, cancellationToken).ConfigureAwait(false);
        var assertion = authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialAssertion>()
            ?? throw StandardError.External("Passkey ceremony returned an unexpected credential.");
        var userHandle = assertion.UserId?.ToArray();
        return PasskeyJson.Assertion(
            assertion.CredentialId.ToArray(),
            assertion.RawClientDataJson.ToArray(),
            assertion.RawAuthenticatorData.ToArray(),
            assertion.Signature.ToArray(),
            userHandle is { Length: > 0 } ? userHandle : null);
    }

    // Private methods

    private static async Task<ASAuthorization> Run(ASAuthorizationRequest request, CancellationToken cancellationToken)
    {
        // The controller only weakly references its delegate, so the handler stays alive here
        var handler = new Handler();
        var controller = new ASAuthorizationController([request]) {
            Delegate = handler,
            PresentationContextProvider = handler,
        };
        using var _ = cancellationToken.Register(
            () => AppServicesAccessor.BeginDispatchToMainThread(() => controller.Cancel()));
        await AppServicesAccessor.DispatchToMainThread(() => controller.PerformRequests()).ConfigureAwait(false);
        return await handler.WhenCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Nested types

    private sealed class Handler
        : NSObject, IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        private readonly TaskCompletionSource<ASAuthorization> _source = TaskCompletionSourceExt.New<ASAuthorization>();

        public Task<ASAuthorization> WhenCompleted => _source.Task;

        [Export("authorizationController:didCompleteWithAuthorization:")]
        public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
            => _source.TrySetResult(authorization);

        [Export("authorizationController:didCompleteWithError:")]
        public void DidComplete(ASAuthorizationController controller, NSError error)
        {
            var isCancelled = error.Domain == ASAuthorizationError.Canceled.GetDomain()
                && error.Code == (long)ASAuthorizationError.Canceled;
            _source.TrySetException(isCancelled
                ? new PasskeyCancelledException()
                : StandardError.External(error.LocalizedDescription));
        }

#if MACOS
        [Export("presentationAnchorForAuthorizationController:")]
        public NSWindow GetPresentationAnchor(ASAuthorizationController controller)
        {
            var app = NSApplication.SharedApplication;
            return app.KeyWindow ?? app.MainWindow;
        }
#else
        [Export("presentationAnchorForAuthorizationController:")]
        public UIWindow GetPresentationAnchor(ASAuthorizationController controller)
            => WindowStateManager.Default.GetCurrentUIWindow()!;
#endif
    }
}
