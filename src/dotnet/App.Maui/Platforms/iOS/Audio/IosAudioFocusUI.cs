using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Locking;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

// iOS allows exactly one AVAudioSession category at a time, and Tunes is always implicitly
// active. The four reachable states and the corresponding session category are:
//   Tunes onlyΩ                     -> Ambient
//   Tunes + Playback               -> Playback
//   Tunes + Recording              -> PlayAndRecord
//   Tunes + Playback + Recording   -> PlayAndRecord
// _modes is the set of currently-active focus kinds (always seeded with Tune so Tune is
// the implicit baseline and is never removed). _scopes tracks per-requester scopes on top
// of _modes so we can invoke each requester's FocusLost handler independently.

public sealed class IosAudioFocusUI : AudioFocusUI
{
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.2, 3);

    private readonly AsyncLock _lock = new(LockReentryMode.CheckedFail);
    private readonly Scopes _scopes;
    private readonly Disposable<NSObject> _interruptionSubscription;
    private readonly Disposable<NSObject> _configurationChangeSubscription;
    private readonly TaskSerializer _interruptions = new();
    private bool _isSuspended;

    private AppUIHub Hub { get; }
    private AudioSession AudioSession => field ??= Hub.Services.GetRequiredService<AudioSession>();
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public IosAudioFocusUI(AppUIHub hub)
    {
        Hub = hub;
        _scopes = new Scopes(Hub.LogFor(GetType()));
        _interruptionSubscription = Disposable.New(
            AVAudioSession.Notifications.ObserveInterruption(OnInterruption),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
        _configurationChangeSubscription = Disposable.New(
            AVAudioEngine.Notifications.ObserveConfigurationChange(OnConfigurationChange),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
    }

    protected override async Task DisposeAsyncCore()
    {
        _interruptionSubscription.DisposeSilently();
        _configurationChangeSubscription.DisposeSilently();
        await _interruptions.Abort().ConfigureAwait(false);

        using var cts = new CancellationTokenSource(CoreConstants.DisposeTimeout);
        try {
            using var _1 = await _lock.Lock(cts.Token).ConfigureAwait(false);
            _scopes.Dispose();
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
        var scope = _scopes.Get(requester);
        if (scope is not null) {
            Log.LogInformation("Returning existing scope {Scope} ({Mode})", scope, requester.Kind);
            return scope;
        }

        var needsReconfigure = _scopes.GetMode() < requester.Kind;
        scope = _scopes.Add(requester, new Scope(this, requester));
        if (!needsReconfigure)
            return scope;

        try {
            await SetModeUnsafe(_scopes.GetMode()).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            _scopes.Remove(requester, out _);
            Log.LogError(e, "Failed to acquire scope for {Mode}", requester.Kind);
            throw;
        }
        return scope;
    }

    public override async Task TryRecover(CancellationToken cancellationToken = default)
    {
        using var cts = cancellationToken.LinkWith(StopToken);
        await AsyncChain.From(RecoverInternal)
            .Retry(RetryDelays, 3, Log)
            .LogError(Log)
            .Run(cts.Token)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task Release(AudioFocusRequester requester, Scope scope)
    {
        Log.LogInformation("Scope {Scope} releasing for {Mode}", scope, requester.Kind);
        try {
            using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
            var modeBefore = _scopes.GetMode();
            // TODO: move logging to Scopes
            if (!_scopes.Remove(requester, out var existing)) {
                Log.LogWarning("Requester {Requester} not found in _scopes", requester);
                return;
            }

            if (existing != scope) {
                Log.LogError("Scope {Scope} doesn't match existing scope {Existing} for {Mode}",
                    scope,
                    existing,
                    requester.Kind);
                return;
            }

            var modeAfter = _scopes.GetMode();
            Log.LogInformation("Release {Mode}: state {Before} -> {After}", requester.Kind, modeBefore, modeAfter);
            if (requester.Kind is AudioFocusMode.Recording && modeAfter < AudioFocusMode.Recording)
                AudioEngines.Recording.StopRecording();
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
        AudioEngines.Pause();
        await AudioSession.Reconfigure(mode).ConfigureAwait(false);
        AudioEngines.Resume(mode);
    }

    private async Task RecoverInternal(CancellationToken cancellationToken)
    {
        using var _1 = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var mode = _scopes.GetMode();
        Log.LogInformation("Recover: reactivating session in {Mode}", mode);
        await AudioSession.Reactivate(mode).ConfigureAwait(false);
        AudioEngines.Resume(mode);
        InvokeRestoreUnsafe();
        _isSuspended = false;
    }

    private void InvokeLostFocusUnsafe(bool mayRecover, bool canDuck)
    {
        _isSuspended = true;
        foreach (var scope in _scopes.All()) {
            scope.Suspend(true);
            scope.PendingRestore = scope.Requester.AudioFocusLostHandler(mayRecover, canDuck);
        }
    }

    private void InvokeRestoreUnsafe()
    {
        foreach (var scope in _scopes.All().Where(x => x.PendingRestore is not null).ToArray()) {
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
        _ = _interruptions.Enqueue(_ => HandleInterruption(type, reason, wasSuspended, option));
    }

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
    {
        Log.LogInformation("Audio engine configuration change detected");
        _ = BackgroundTask.Run(async () => {
            using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
            if (!_scopes.IsEmpty && !_isSuspended)
                AudioEngines.Resume(_scopes.GetMode());
        }, Log, "Failed to handle configuration change", StopToken);
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
                using (await _lock.Lock(StopToken).ConfigureAwait(false))
                    InvokeLostFocusUnsafe(true, false);
                break;
            case AVAudioSessionInterruptionType.Ended:
                // Always attempt recovery - ShouldResume flag is unreliable for phone calls
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

    private sealed class Scope(IosAudioFocusUI owner, AudioFocusRequester requester) : AudioFocusScope
    {
        public AudioFocusRequester Requester => requester;
        public AudioFocusRestoreHandler? PendingRestore { get; set; }

        public override void Dispose()
            => _ = owner.Release(requester, this);
    }

    private sealed class Scopes(ILogger log) : IDisposable
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

        public bool Remove(AudioFocusRequester requester, out Scope? scope)
            => _byMode[requester.Kind].Remove(requester, out scope);

        public IEnumerable<Scope> All()
            => _byMode.SelectMany(x => x.Value.Values);

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
