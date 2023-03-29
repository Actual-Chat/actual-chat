using ActualChat.Chat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui;

public class IosAudioOutputController : IAudioOutputController
{
    private readonly object _lock = new ();
    private readonly IMutableState<bool> _isAudioOn;
    private readonly IMutableState<bool> _isSpeakerphoneOn;

    private ILogger Log { get; }
    public IState<bool> IsAudioOn => _isAudioOn;
    public IState<bool> IsSpeakerphoneOn => _isSpeakerphoneOn;


    public IosAudioOutputController(IServiceProvider services)
    {
        var type = GetType();
        var stateFactory = services.GetRequiredService<IStateFactory>();
        Log = services.LogFor(type);
        _isAudioOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsAudioOn)));
        _isSpeakerphoneOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsSpeakerphoneOn)));
    }

    public ValueTask SetAudioEnabled(bool mustEnable)
    {
        lock (_lock) {
            if (_isAudioOn.Value == mustEnable)
                return ValueTask.CompletedTask;

            _isAudioOn.Value = mustEnable;
            ConfigureAudioSession();
        }

        return ValueTask.CompletedTask;

        void ConfigureAudioSession()
        {
            try
            {
                var avAudioSession = AVAudioSession.SharedInstance();
                if (mustEnable)
                {
                    avAudioSession
                        .SetCategory(AVAudioSessionCategory.PlayAndRecord,
                            AVAudioSessionCategoryOptions.AllowBluetooth
                            // | AVAudioSessionCategoryOptions.AllowBluetoothA2DP TODO: only for audio playback
                            | AVAudioSessionCategoryOptions.DuckOthers
                            | AVAudioSessionCategoryOptions.DefaultToSpeaker
                        )
                        .Assert();
                    if (!avAudioSession.OverrideOutputAudioPort(AVAudioSessionPortOverride.Speaker, out var error))
                        error.Assert();
                }

                avAudioSession.SetActive(mustEnable, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation).Assert();
            }
            catch (Exception e)
            {
                DefaultLog.LogError(e, "Failed to configure audio session");
            }
        }
    }

    public ValueTask SetSpeakerphoneEnabled(bool mustEnable)
        => ValueTaskExt.CompletedTask;
}
