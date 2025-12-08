using ActualChat.UI.Blazor.App.Services;
using ActualLab.Locking;

namespace ActualChat.App.Maui.Services;

public abstract class MauiAudioFocusService(AppUIHub hub) : AudioFocusService
{
    private readonly List<RestoreFocusHandler> _restoreFocusHandlers = new ();
    private readonly AsyncLock _asyncLock = new();
    private AudioFocusHolder? _lastAudioFocusHolder;

    protected AppUIHub Hub => hub;
    protected ILogger Log => field ??= Hub.LogFor(GetType());

    public override async Task<IAudioFocusActivation?> TryGainAudioFocus(AudioFocusConsumer consumer)
    {
        using var releaser = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        Log.LogInformation("Trying to gain audio focus: {Kind}", consumer.Kind);
        if (_lastAudioFocusHolder is not null && !_lastAudioFocusHolder.IsSuspended) {
            if (_lastAudioFocusHolder.Activations.TryGetValue(consumer, out var activation)) {
                Log.LogInformation("Already have audio focus");
                return activation;
            }

            if (!FocusModeHasChanged(_lastAudioFocusHolder, consumer)) {
                var audioFocusActivation = new AudioFocusActivation(this, consumer);
                _lastAudioFocusHolder.Activations.Add(consumer, audioFocusActivation);
                return audioFocusActivation;
            }
        }

        var holder = await RenewAudioFocus(consumer).ConfigureAwait(false);
        if (holder is null)
            return null;

        return holder.Activations[consumer];
    }

    public async Task ReleaseAudioFocus(AudioFocusConsumer consumer)
    {
        if (_lastAudioFocusHolder is null)
            return;

        if (!_lastAudioFocusHolder.Activations.Remove(consumer))
            return;

        if (_lastAudioFocusHolder.Activations.Count == 0) {
            _lastAudioFocusHolder.Handle.Release();
            _restoreFocusHandlers.Clear();
            _lastAudioFocusHolder = null;
            return;
        }

        if (!FocusModeHasChanged(_lastAudioFocusHolder, consumer))
            return;

        await RenewAudioFocus(null).ConfigureAwait(false);
    }

    protected abstract Task<AudioFocusHandle?> RequestAudioFocus(AudioMode mode);

    private async Task<AudioFocusHolder?> RenewAudioFocus(AudioFocusConsumer? consumer)
    {
        AudioFocusHolder? temp = _lastAudioFocusHolder;
        if (temp is null && consumer is null)
            return null;

        var desiredMode = CalculateAudioFocusMode(temp?.Activations.Keys, consumer);
        var audioFocusHandle = await RequestAudioFocus(desiredMode).ConfigureAwait(false);
        if (audioFocusHandle is null) {
            Log.LogInformation("Failed to get/update audio focus");
            _restoreFocusHandlers.Clear();
            _lastAudioFocusHolder = null;
            InvokeLostFocus(temp, false);
            return null;
        }

        var holder = new AudioFocusHolder(desiredMode, audioFocusHandle);
        if (temp is not null) {
            foreach (var (k, v) in temp.Activations) {
                v.Suspend(false);
                holder.Activations.Add(k, v);
            }
        }
        if (consumer is not null && !holder.Activations.ContainsKey(consumer)) {
            var focusActivation = new AudioFocusActivation(this, consumer);
            holder.Activations.Add(consumer, focusActivation);
        }
        audioFocusHandle.LostFocus += mayRecover => {
            holder.Suspend(true);
            InvokeLostFocus(holder, mayRecover);
        };
        audioFocusHandle.RecoverFocus += () => {
            holder.Suspend(false);
            foreach (var handler in _restoreFocusHandlers) {
                handler.Invoke();
            }
        };
        _lastAudioFocusHolder = holder;
        return holder;
    }

    private void InvokeLostFocus(AudioFocusHolder? holder, bool mayRecover)
    {
        if (holder is null)
            return;

        foreach (var (consumer, activation) in holder.Activations) {
            activation.Suspend(true);
            var restoreFocusHandler = consumer.LostFocusCallback(mayRecover);
            if (restoreFocusHandler is not null) {
                _restoreFocusHandlers.Add(() => {
                    activation.Suspend(false);
                    restoreFocusHandler.Invoke();
                });
            }
        }
    }

    private AudioMode CalculateAudioFocusMode(IReadOnlyCollection<AudioFocusConsumer>? consumers, AudioFocusConsumer? newConsumer)
    {
        var list = new List<AudioFocusConsumer>();
        if (consumers is not null)
            list.AddRange(consumers);
        if (newConsumer is not null)
            list.Add(newConsumer);
        return CalculateAudioFocusMode(list);
    }

    protected virtual AudioMode CalculateAudioFocusMode(IReadOnlyCollection<AudioFocusConsumer> consumers)
    {
        if (consumers.Count == 0)
            throw new ArgumentException("No consumers provided", nameof(consumers));

        return consumers.Select(c => c.Kind).Max();
    }

    private bool FocusModeHasChanged(AudioFocusHolder audioFocusHolder, AudioFocusConsumer consumer)
    {
        var mode1 = CalculateAudioFocusMode(audioFocusHolder.Activations.Keys);
        var mode2 = CalculateAudioFocusMode(audioFocusHolder.Activations.Keys, consumer);
        return mode1 != mode2;
    }

    // Nested types
    private class AudioFocusHolder(AudioMode mode, AudioFocusHandle handle)
    {
        public AudioMode Mode => mode;
        public bool IsSuspended { get; private set;}
        public AudioFocusHandle Handle => handle;
        public Dictionary<AudioFocusConsumer, AudioFocusActivation> Activations { get; } = new();

        public void Suspend(bool isSuspended)
            => IsSuspended = isSuspended;
    }

    private class AudioFocusActivation : IAudioFocusActivation
    {
        private static long _nextId;

        private readonly MauiAudioFocusService _owner;
        private readonly AudioFocusConsumer _consumer;

        public string Id { get; }
        public bool IsSuspended { get; private set; }

        public void Suspend(bool isSuspended)
            => IsSuspended = isSuspended;

        public AudioFocusActivation(MauiAudioFocusService owner, AudioFocusConsumer consumer)
        {
            _owner = owner;
            _consumer = consumer;
            Id = Interlocked.Increment(ref _nextId).ToInvariantString();
        }

        public void Release()
            => _ = _owner.ReleaseAudioFocus(_consumer);
    }
}

public class AudioFocusHandle(long id, Action<AudioFocusHandle>? release = null)
{
    public long Id => id;
    private readonly Action<AudioFocusHandle> _onRelease = release ?? (_ => { });

    public event Action<bool>? LostFocus;
    public event Action? RecoverFocus;

    public void RaiseLostFocus(bool mayRestore)
        => LostFocus?.Invoke(mayRestore);

    public void RaiseRecoverFocus()
        => RecoverFocus?.Invoke();

    public void Release()
        => _onRelease.Invoke(this);
}
