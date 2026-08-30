using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Locking;

namespace ActualChat.App.Maui.Services;

public abstract class MauiAudioFocusUI(AppUIHub hub) : AudioFocusUI
{
    private readonly List<AudioFocusRestoreHandler> _restoreAudioFocusHandlers = new();
    private AudioFocusHolder? _lastAudioFocusHolder;

    protected readonly AsyncLock OperationLock = new();

    protected AppUIHub Hub { get; } = hub;
    protected ILogger Log => field ??= Hub.LogFor(GetType());
    public override AudioFocusMode ActiveMode => _lastAudioFocusHolder?.Mode ?? AudioFocusMode.Tune;

    public override async Task<AudioFocusScope?> TryAcquire(AudioFocusRequester requester)
    {
        using var releaser = await OperationLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        Log.LogInformation("Trying to acquire audio focus: {Kind}", requester.Kind);
        if (_lastAudioFocusHolder is not null && !_lastAudioFocusHolder.IsSuspended) {
            if (_lastAudioFocusHolder.Scopes.TryGetValue(requester, out var scope)) {
                Log.LogInformation("Already have audio focus");
                return scope;
            }

            if (!FocusModeHasChanged(_lastAudioFocusHolder, requester)) {
                var newScope = new Scope(this, requester);
                _lastAudioFocusHolder.Scopes.Add(requester, newScope);
                return newScope;
            }
        }

        var holder = await RenewAudioFocus(requester).ConfigureAwait(false);
        if (holder is null)
            return null;

        return holder.Scopes[requester];
    }

    public override AudioFocusDiagnostics GetDiagnostics()
    {
        var holder = _lastAudioFocusHolder;
        return holder is null
            ? new AudioFocusDiagnostics(IsSupported: true,
                ActiveMode: AudioFocusMode.Tune,
                IsInterrupted: false,
                IsSuspended: false,
                IsSessionConfigured: false,
                Scopes: [],
                Session: null)
            : new AudioFocusDiagnostics(
                IsSupported: true,
                ActiveMode: holder.Mode,
                IsInterrupted: false,
                IsSuspended: holder.IsSuspended,
                IsSessionConfigured: true,
                Scopes: [new AudioFocusScopeInfo(holder.Mode, holder.Scopes.Count)],
                Session: null);
    }

    // Protected methods

    protected abstract Task<MauiAudioFocusHandle?> RequestAudioFocus(AudioFocusMode mode);

    protected virtual AudioFocusMode CalculateAudioFocusMode(IReadOnlyCollection<AudioFocusRequester> requesters)
    {
        if (requesters.Count == 0)
            throw new ArgumentException("No requesters provided", nameof(requesters));

        return requesters.Select(c => c.Kind).Max();
    }

    // Private methods

    private async Task ReleaseAudioFocus(AudioFocusRequester requester)
    {
        using var releaser = await OperationLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_lastAudioFocusHolder is null)
            return;

        if (!_lastAudioFocusHolder.Scopes.Remove(requester))
            return;

        if (_lastAudioFocusHolder.Scopes.Count == 0) {
            _lastAudioFocusHolder.Handle.Release();
            lock (_restoreAudioFocusHandlers)
                _restoreAudioFocusHandlers.Clear();
            _lastAudioFocusHolder = null;
            return;
        }

        if (!FocusModeHasChanged(_lastAudioFocusHolder, requester))
            return;

        await RenewAudioFocus(null).ConfigureAwait(false);
    }

    private async Task<AudioFocusHolder?> RenewAudioFocus(AudioFocusRequester? requester)
    {
        var temp = _lastAudioFocusHolder;
        if (temp is null && requester is null)
            return null;

        var desiredMode = CalculateAudioFocusMode(temp?.Scopes.Keys, requester);
        var audioFocusHandle = await RequestAudioFocus(desiredMode).ConfigureAwait(false);
        if (audioFocusHandle is null) {
            Log.LogInformation("Failed to get/update audio focus");
            lock (_restoreAudioFocusHandlers)
                _restoreAudioFocusHandlers.Clear();
            _lastAudioFocusHolder = null;
            InvokeLostFocus(temp, false, false);
            return null;
        }

        var holder = new AudioFocusHolder(desiredMode, audioFocusHandle);
        if (temp is not null) {
            foreach (var (k, v) in temp.Scopes) {
                v.Suspend(false);
                holder.Scopes.Add(k, v);
            }
        }
        if (requester is { } req && !holder.Scopes.ContainsKey(req)) {
            var focusScope = new Scope(this, req);
            holder.Scopes.Add(req, focusScope);
        }
        audioFocusHandle.FocusLost += (mayRecover, canDuck) => {
            holder.Suspend(true);
            InvokeLostFocus(holder, mayRecover, canDuck);
        };
        audioFocusHandle.FocusRecover += () => {
            holder.Suspend(false);
            // Taken and cleared, because they were only ever added: the Nth recover replayed every
            // restore from all N cycles, racing StartReplay against itself and growing unbounded
            // over a long session. The snapshot also keeps this off the live list, which these
            // callbacks touch from the Android main thread while other threads mutate it.
            AudioFocusRestoreHandler[] handlers;
            lock (_restoreAudioFocusHandlers) {
                handlers = [.. _restoreAudioFocusHandlers];
                _restoreAudioFocusHandlers.Clear();
            }
            foreach (var handler in handlers) {
                handler.Invoke();
            }
        };
        _lastAudioFocusHolder = holder;
        return holder;
    }

    private void InvokeLostFocus(AudioFocusHolder? holder, bool mayRecover, bool canDuck)
    {
        if (holder is null)
            return;

        foreach (var (requester, scope) in holder.Scopes) {
            scope.Suspend(true);
            var restoreHandler = requester.AudioFocusLostHandler(mayRecover, canDuck);
            if (restoreHandler is not null) {
                lock (_restoreAudioFocusHandlers) {
                    _restoreAudioFocusHandlers.Add(() => {
                        scope.Suspend(false);
                        restoreHandler.Invoke();
                    });
                }
            }
        }
    }

    private AudioFocusMode CalculateAudioFocusMode(
        IReadOnlyCollection<AudioFocusRequester>? requesters,
        AudioFocusRequester? newRequester)
    {
        var list = new List<AudioFocusRequester>();
        if (requesters is not null)
            list.AddRange(requesters);
        if (newRequester is not null)
            list.Add(newRequester.Value);

        return CalculateAudioFocusMode(list);
    }

    private bool FocusModeHasChanged(AudioFocusHolder audioFocusHolder, AudioFocusRequester requester)
    {
        var mode1 = CalculateAudioFocusMode(audioFocusHolder.Scopes.Keys);
        var mode2 = CalculateAudioFocusMode(audioFocusHolder.Scopes.Keys, requester);

        return mode1 != mode2;
    }

    // Nested types

    private sealed class AudioFocusHolder(AudioFocusMode mode, MauiAudioFocusHandle handle)
    {
        public AudioFocusMode Mode => mode;
        public bool IsSuspended { get; private set; }
        public MauiAudioFocusHandle Handle => handle;
        public Dictionary<AudioFocusRequester, Scope> Scopes { get; } = new();

        public void Suspend(bool isSuspended)
            => IsSuspended = isSuspended;
    }

    private sealed class Scope(MauiAudioFocusUI owner, AudioFocusRequester requester) : AudioFocusScope
    {
        public override void Dispose()
            => _ = owner.ReleaseAudioFocus(requester);
    }
}
