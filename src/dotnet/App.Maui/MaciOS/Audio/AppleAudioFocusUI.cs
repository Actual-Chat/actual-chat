using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Locking;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class AppleAudioFocusUI : AudioFocusUI
{
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.2, 3);

    private readonly AsyncLock _lock = new(LockReentryMode.CheckedFail);
    private readonly ActiveScopes _activeScopes;
    private readonly Disposable<NSObject> _interruptionSubscription;
    private readonly Disposable<NSObject> _configurationChangeSubscription;
    private readonly Disposable<NSObject> _mediaServicesResetSubscription;
    private readonly Disposable<NSObject> _routeChangeSubscription;
    private readonly TaskSerializer _interruptionQueue = new();
    private bool _isInterrupted;
    private bool _isSuspended;
    private bool _isSessionConfigured;
    private bool _isSessionActivated;

    private AppUIHub Hub { get; }
    private AudioSession AudioSession => field ??= Hub.Services.GetRequiredService<AudioSession>();
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public AppleAudioFocusUI(AppUIHub hub)
    {
        Hub = hub;
        _activeScopes = new ActiveScopes(Hub.LogFor(GetType()));
        _interruptionSubscription = Disposable.New(
            AVAudioSession.Notifications.ObserveInterruption(OnInterruption),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
        _configurationChangeSubscription = Disposable.New(
            AVAudioEngine.Notifications.ObserveConfigurationChange(OnConfigurationChange),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
        _mediaServicesResetSubscription = Disposable.New(
            AVAudioSession.Notifications.ObserveMediaServicesWereReset(OnMediaServicesReset),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
        _routeChangeSubscription = Disposable.New(
            AVAudioSession.Notifications.ObserveRouteChange(OnRouteChange),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
    }

    protected override async Task DisposeAsyncCore()
    {
        _interruptionSubscription.DisposeSilently();
        _configurationChangeSubscription.DisposeSilently();
        _mediaServicesResetSubscription.DisposeSilently();
        _routeChangeSubscription.DisposeSilently();
        await _interruptionQueue.Abort().ConfigureAwait(false);

        using var cts = new CancellationTokenSource(CoreConstants.DisposeTimeout);
        try {
            using var _1 = await _lock.Lock(cts.Token).ConfigureAwait(false);
            _activeScopes.Dispose();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            Log.LogWarning("{Type}: lock wasn't acquired in {Timeout}; releasing without it",
                GetType().GetName(), CoreConstants.DisposeTimeout);
        }
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    public override async Task<AudioFocusScope?> TryAcquire(AudioFocusRequester requester)
    {
        using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
        var scope = _activeScopes.Get(requester);
        if (scope is not null) {
            Log.LogInformation("Returning existing scope {Scope} ({Mode})", scope, requester.Kind);
            return scope;
        }

        var needsReconfigure = !_isSessionConfigured
            || !_isSessionActivated
            || _activeScopes.GetMode() < requester.Kind;
        scope = _activeScopes.Add(requester, new Scope(this, requester));
        if (!needsReconfigure)
            return scope;

        try {
            await SetModeUnsafe(_activeScopes.GetMode()).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            _activeScopes.TryRemove(requester, scope);
            Log.LogError(e, "Failed to acquire scope for {Mode}", requester.Kind);
            throw;
        }

        return scope;
    }

    public override async Task TryRecover(CancellationToken cancellationToken = default)
    {
        using var cts = cancellationToken.LinkWith(StopToken);
        // Persist across a longer window: 'Session activation failed' (!act) often lasts
        // several seconds after an interruption ends. Route-change notifications re-arm
        // recovery too (see OnRouteChange), so this isn't the only chance to recover.
        await AsyncChain.From(RecoverInternal)
            .Retry(RetryDelays, 10, Log)
            .LogError(Log)
            .Run(cts.Token)
            .ConfigureAwait(false);
    }

    public override AudioFocusDiagnostics GetDiagnostics()
        => new (
            IsSupported: true,
            ActiveMode: _activeScopes.GetMode(),
            IsInterrupted: Volatile.Read(ref _isInterrupted),
            IsSuspended: _isSuspended,
            IsSessionConfigured: _isSessionConfigured,
            Scopes: _activeScopes.GetScopeInfos(),
            Session: AudioSession.GetDiagnostics());

    // Private methods

    private async Task Release(AudioFocusRequester requester, Scope scope)
    {
        Log.LogInformation("Scope {Scope} releasing for {Mode}", scope, requester.Kind);
        try {
            using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
            var modeBefore = _activeScopes.GetMode();
            if (!_activeScopes.TryRemove(requester, scope))
                return;

            var modeAfter = _activeScopes.GetMode();
            Log.LogInformation("Release {Mode}: state {Before} -> {After}", requester.Kind, modeBefore, modeAfter);
            if (requester.Kind is AudioFocusMode.Recording && modeAfter < AudioFocusMode.Recording)
                AudioEngines.Recording.Release();
            if (modeAfter != modeBefore)
                await SetModeUnsafe(modeAfter).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(StopToken))
                Log.LogError(e, "Failed to release scope {Scope} for {Mode}", scope, requester.Kind);
        }
    }

    private async Task SetModeUnsafe(AudioFocusMode mode)
    {
        Log.LogInformation("SetMode: {Mode}", mode);
        if (Volatile.Read(ref _isInterrupted)) {
            // iOS rejects SetActive(true) while an interruption is in progress; RecoverInternal
            // reactivates in the latest mode once the interruption ends.
            Log.LogInformation("SetMode: {Mode} deferred - interruption in progress", mode);
            return;
        }

        // GetMode() reports Tune both for a live tune scope and for no scopes at all, so the
        // idle case has to be told apart here rather than from the mode.
        if (_activeScopes.IsEmpty) {
            AudioEngines.Pause();
            await AudioSession.Deactivate().ConfigureAwait(false);
            (_isSessionConfigured, _isSessionActivated) = (false, false);
            return;
        }

        // Bouncing the engines is only owed to the deactivate/activate pair Reconfigure skips
        // under a PTT owner - without this it restarts a live capture on every acquire.
        var mustBounceEngines = AudioSession.MayActivateNow;
        if (mustBounceEngines)
            AudioEngines.Pause();

        var setup = await AudioSession.Reconfigure(mode).ConfigureAwait(false);
        // setup.IsActivated covers an owner flip between the snapshot above and ReconfigureUnsafe
        // running: the session went through a deactivate/activate pair with the engines never paused.
        if (mustBounceEngines || setup.IsActivated)
            AudioEngines.Resume(mode);

        (_isSessionConfigured, _isSessionActivated) = (setup.IsConfigured, setup.IsActivated);
    }

    private async Task RecoverInternal(CancellationToken cancellationToken)
    {
        using var _1 = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        if (_activeScopes.IsEmpty) {
            // Nothing wants the session, so recovering it would just re-acquire the cost
            // SetModeUnsafe released - the next acquire reactivates anyway.
            Log.LogInformation("Recover: no active scopes, leaving the session deactivated");
            return;
        }

        var mode = _activeScopes.GetMode();
        Log.LogInformation("Recover: reactivating session in {Mode}", mode);
        var setup = await AudioSession.Reactivate(mode).ConfigureAwait(false);
        (_isSessionConfigured, _isSessionActivated) = (setup.IsConfigured, setup.IsActivated);
        AudioEngines.Resume(mode);
        InvokeRestoreUnsafe();
        _isSuspended = false;
    }

    private void InvokeLostFocusUnsafe(bool mayRecover, bool canDuck)
    {
        _isSuspended = true;
        foreach (var scope in _activeScopes.All()) {
            scope.Suspend(true);
            scope.PendingRestore = scope.Requester.AudioFocusLostHandler(mayRecover, canDuck);
        }
    }

    private void InvokeRestoreUnsafe()
    {
        foreach (var scope in _activeScopes.All().Where(x => x.PendingRestore is not null).ToArray()) {
            var handler = scope.PendingRestore!;
            scope.PendingRestore = null;
            try {
                scope.Suspend(false);
                handler();
            }
            catch (Exception e) {
                Log.LogError(e, "Audio focus restore handler failed");
            }
        }
    }

    private void OnInterruption(object? sender, AVAudioSessionInterruptionEventArgs e)
    {
        // IMPORTANT: event args must be captured by value otherwise they will change!
        var type = e.InterruptionType;
        var reason = e.Reason;
        var wasSuspended = e.WasSuspended;
        var option = e.Option;
        _ = _interruptionQueue.Enqueue(_ => HandleInterruption(type, reason, wasSuspended, option));
    }

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
    {
        Log.LogInformation("Audio engine configuration change detected");
        _ = BackgroundTask.Run(async () => {
            using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
            if (!_activeScopes.IsEmpty && !_isSuspended)
                AudioEngines.Resume(_activeScopes.GetMode());
        }, Log, "Failed to handle configuration change", StopToken);
    }

    private void OnMediaServicesReset(object? sender, NSNotificationEventArgs e)
    {
        // mediaserverd reset invalidates the session AND all engines - nothing else
        // observes this, so without a full rebuild audio stays dead while the UI still
        // thinks it's listening (headphones button stays on).
        Log.LogWarning("Media services were reset - rebuilding audio session");
        Volatile.Write(ref _isInterrupted, false);
        _ = _interruptionQueue.Enqueue(_ => RebuildAfterMediaServicesReset());
    }

    private async Task RebuildAfterMediaServicesReset()
    {
        try {
            await AsyncChain.From(RebuildInternal)
                .Retry(RetryDelays, 10, Log)
                .LogError(Log)
                .Run(StopToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            Log.LogError(e, "Failed to rebuild audio session after media services reset");
        }
    }

    private async Task RebuildInternal(CancellationToken cancellationToken)
    {
        using var _1 = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        (_isSessionConfigured, _isSessionActivated) = (false, false);
        if (_activeScopes.IsEmpty)
            return;

        var mode = _activeScopes.GetMode();
        Log.LogWarning("Rebuild: reconfiguring session in {Mode}", mode);
        AudioEngines.Pause();
        var setup = await AudioSession.Reconfigure(mode).ConfigureAwait(false);
        (_isSessionConfigured, _isSessionActivated) = (setup.IsConfigured, setup.IsActivated);
        AudioEngines.Resume(mode);
        InvokeRestoreUnsafe();
        _isSuspended = false;
    }

    private void OnRouteChange(object? sender, AVAudioSessionRouteChangeEventArgs e)
    {
        var reason = e.Reason;
        _ = _interruptionQueue.Enqueue(_ => MaybeRecoverOnRouteChange(reason));
    }

    private async Task MaybeRecoverOnRouteChange(AVAudioSessionRouteChangeReason reason)
    {
        bool shouldRecover;
        using (await _lock.Lock(StopToken).ConfigureAwait(false))
            shouldRecover = _isSuspended && !Volatile.Read(ref _isInterrupted) && !_activeScopes.IsEmpty;
        if (!shouldRecover)
            return;

        Log.LogInformation("Route change ({Reason}) while suspended - attempting recovery", reason);
        await TryRecover().ConfigureAwait(false);
    }

    private async Task HandleInterruption(
        AVAudioSessionInterruptionType type,
        AVAudioSessionInterruptionReason reason,
        bool? wasSuspended,
        AVAudioSessionInterruptionOptions option)
    {
        Log.LogInformation(
            "Interruption type={Type}, reason={Reason}, wasSuspended={WasSuspended}, option={Option}",
            type, reason, wasSuspended, option);
        try {
            switch (type) {
            case AVAudioSessionInterruptionType.Began:
                using (await _lock.Lock(StopToken).ConfigureAwait(false)) {
                    Volatile.Write(ref _isInterrupted, true);
                    InvokeLostFocusUnsafe(true, false);
                }
                break;
            case AVAudioSessionInterruptionType.Ended:
                Volatile.Write(ref _isInterrupted, false);
                // ShouldResume is unreliable for phone calls, so always recover.
                await TryRecover().ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid interruption type");
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            Log.LogError(e, "Failed to handle interruption type={Type}", type);
        }
    }

    // Nested types

    private sealed class Scope(AppleAudioFocusUI owner, AudioFocusRequester requester) : AudioFocusScope
    {
        public AudioFocusRequester Requester => requester;
        public AudioFocusRestoreHandler? PendingRestore { get; set; }

        public override void Dispose()
            => _ = owner.Release(requester, this);
    }

    private sealed class ActiveScopes(ILogger log) : IDisposable
    {
        private readonly Dictionary<AudioFocusMode, Dictionary<AudioFocusRequester, Scope>> _byMode = new() {
            [AudioFocusMode.Tune] = new(),
            [AudioFocusMode.Playback] = new(),
            [AudioFocusMode.Recording] = new(),
        };

        public bool IsEmpty => _byMode.All(x => x.Value.Count == 0);

        public Scope? Get(AudioFocusRequester requester)
            => _byMode.GetValueOrDefault(requester.Kind)?.GetValueOrDefault(requester);

        public Scope Add(AudioFocusRequester requester, Scope scope)
        {
            _byMode[requester.Kind].Add(requester, scope);
            return scope;
        }

        public AudioFocusMode GetMode()
            => _byMode.Where(x => x.Value.Count > 0)
                .Select(x => x.Key)
                .DefaultIfEmpty(AudioFocusMode.Tune)
                .Max();

        public bool TryRemove(AudioFocusRequester requester, Scope scope)
        {
            if (!_byMode[requester.Kind].Remove(requester, out var existing)) {
                log.LogWarning("Requester {Requester} not found in active scopes", requester);
                return false;
            }

            if (existing != scope) {
                log.LogError("Scope {Scope} doesn't match existing scope {Existing} for {Mode}",
                    scope,
                    existing,
                    requester.Kind);
                return false;
            }

            return true;
        }

        public IEnumerable<Scope> All()
            => _byMode.SelectMany(x => x.Value.Values);

        public IReadOnlyList<AudioFocusScopeInfo> GetScopeInfos()
            => _byMode.Select(x => new AudioFocusScopeInfo(x.Key, x.Value.Count)).ToList();

        public void Dispose()
        {
            // Notify each active requester that focus is permanently lost (no recovery).
            foreach (var scope in All()) {
                scope.Suspend(true);
                try {
                    scope.Requester.AudioFocusLostHandler(false, false);
                }
                catch (Exception e) {
                    log.LogError(e, "FocusLost handler threw for {Mode} on dispose", scope.Requester.Kind);
                }
            }
            foreach (var scopes in _byMode.Values)
                scopes.Clear();
        }
    }
}
