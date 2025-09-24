using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using CoreHaptics;

namespace ActualChat.App.Maui.Playback;

public class IosTuneUI(UIHub hub) : MauiTunes(hub)
{
    private const float Intensity = 0.5f;
    private const float Sharpness = 0.5f;
    private readonly Lock _lock = new ();
    private readonly Dictionary<Tune, ICHHapticPatternPlayer> _players = new ();
    [field: AllowNull, MaybeNull]
    private CHHapticEngine HapticEngine => field ??= CreateHapticEngine();
    [field: AllowNull, MaybeNull]
    private AudioNodes AudioNodes => field ??= Hub.Services.GetRequiredService<AudioNodes>();

    public override void Dispose()
    {
        base.Dispose();
        List<ICHHapticPatternPlayer> toDispose;
        lock (_lock) {
            if (_players.Count == 0)
                return;

            toDispose = _players.Values.ToList();
            _players.Clear();
        }
        foreach (var player in toDispose)
            player.DisposeSilently();
    }

    public override Task Play(Tune tune, CancellationToken cancellationToken = default)
        => ForegroundTask.Run(() => {
            var (_, sound) = Tunes[tune];
            _ = Vibrate(tune);
            return PlaySound(sound);
        },
        CancellationToken.None);

    public override Task PlayAndWait(Tune tune, CancellationToken cancellationToken = default)
    {
        var (_, sound) = Tunes[tune];
        return Task.WhenAll(Vibrate(tune), PlaySound(sound));
    }

    // Protected methods

    protected override async Task Vibrate(Tune tune)
    {
        await Task.Yield();
        try {
            if (HapticEngine.IsMutedForHaptics)
                return;

            await HapticEngine.StartAsync().ConfigureAwait(true);
            var player = GetPlayer(tune);
            player.Start(0, out var error);
            error.Assert();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to vibrate '{Tune}'", tune);
        }
    }

    // Private methods

    private async Task PlaySound(string soundName)
    {
        if (soundName.IsNullOrEmpty())
            return;

        try {
            using var playerNode = await AudioNodes.CreateSoundNode().ConfigureAwait(false);
            await playerNode.PlayResourceFile(soundName).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to play sound {SoundName}", soundName);
        }
    }

    private CHHapticEngine CreateHapticEngine()
    {
        lock (_lock)
            try {
                var engine = new CHHapticEngine(out var error);
                error.Assert();

                engine.Start(out error);
                error.Assert();
                return engine;
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to create haptic engine");
                throw;
            }
    }

    private ICHHapticPatternPlayer GetPlayer(Tune tune)
    {
        lock (_lock) {
            if (_players.TryGetValue(tune, out var player))
                return player;

            var vibration = Tunes[tune].Vibration;
            var pattern = BuildPattern(vibration);
            player = HapticEngine.CreatePlayer(pattern, out var error);
            error.Assert();

            _players.Add(tune, player!);
            return player!;
        }
    }

    private static CHHapticPattern BuildPattern(int[] vibration)
    {
        var curve = BuildIntensityCurve(vibration);
        var hapticEvent = BuildHapticEvent(vibration);
        var pattern = new CHHapticPattern([hapticEvent], [curve], out var error);
        error.Assert();
        return pattern;
    }

    private static CHHapticEvent BuildHapticEvent(int[] vibration)
    {
        var totalDuration = TimeSpan.FromMilliseconds(vibration.Sum());
        return new CHHapticEvent(CHHapticEventType.HapticContinuous,
            new[] { new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, Sharpness) },
            0,
            totalDuration.TotalSeconds);
    }

    private static CHHapticParameterCurve BuildIntensityCurve(int[] vibration)
    {
        var startTime = TimeSpan.Zero;
        var curvePoints = new CHHapticParameterCurveControlPoint[vibration.Length];
        for (int i = 0; i < vibration.Length; i++)
        {
            var intensity = Intensity * ((i + 1) % 2); // every even item is silence
            curvePoints[i] = new CHHapticParameterCurveControlPoint(startTime.TotalSeconds, intensity);
            startTime += TimeSpan.FromMilliseconds(vibration[i]);
        }

        return new CHHapticParameterCurve(CHHapticDynamicParameterId.HapticIntensityControl, curvePoints, 0);
    }
}
