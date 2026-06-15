using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using CoreHaptics;

namespace ActualChat.App.Maui.Audio;

public class Haptics(AppUIHub hub) : IDisposable
{
    private const float Intensity = 0.5f;
    private const float Sharpness = 0.5f;
    private readonly Lock _lock = new ();
    private readonly Dictionary<Tune, ICHHapticPatternPlayer> _players = new ();

    private CHHapticEngine HapticEngine => field ??= CreateHapticEngine();
    protected ILogger Log => field ??= hub.LogFor(GetType());

    public void Dispose()
    {
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

    public async Task Vibrate(Tune tune, int[] vibration)
    {
        if (HapticEngine.IsMutedForHaptics)
            return;

        await HapticEngine.StartAsync().ConfigureAwait(false);
        var player = GetPlayer(tune, vibration);
        player.Start(0, out var error);
        error.Assert();
        await Task.Delay(vibration.Sum(), hub.StopToken).ConfigureAwait(false);
        player.Cancel(out error);
        error.Assert();
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

    private ICHHapticPatternPlayer GetPlayer(Tune tune, int[] vibration)
    {
        lock (_lock) {
            if (_players.TryGetValue(tune, out var player))
                return player;

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
            [new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, Sharpness)],
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
