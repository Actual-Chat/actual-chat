using ActualChat.UI.Blazor.Services;
using CoreHaptics;

namespace ActualChat.App.Maui;

public class IosTuneUI(IServiceProvider services) : TuneUI(services), IDisposable
{
    private const float Intensity = 0.5f;
    private const float Sharpness = 0.5f;
    private readonly object _lock = new ();
    private readonly Dictionary<Tune, ICHHapticPatternPlayer> _players = new ();
    private CHHapticEngine? _hapticEngine;
    private CHHapticEngine HapticEngine => _hapticEngine ??= CreateHapticEngine();

    protected override bool UseJsVibration => false;
    private ILogger Log { get; } = services.LogFor<IosTuneUI>();

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

    // Protected methods

    protected override async ValueTask Vibrate(Tune tune)
    {
        await Task.Yield();
        try {
            if (HapticEngine.IsMutedForHaptics)
                return;

            await HapticEngine.StartAsync().ConfigureAwait(true);
            var player = GetPlayer(tune);
            player.Start(0, out var error);
            error.ThrowIfError();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to vibrate '{Tune}'", tune);
        }
    }

    // Private methods

    private CHHapticEngine CreateHapticEngine()
    {
        lock (_lock)
            try {
                var engine = new CHHapticEngine(out var error);
                error.ThrowIfError();

                engine.Start(out error);
                error.ThrowIfError();
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
            error.ThrowIfError();

            _players.Add(tune, player!);
            return player!;
        }
    }

    private static CHHapticPattern BuildPattern(int[] vibration)
    {
        var curve = BuildIntensityCurve(vibration);
        var hapticEvent = BuildHapticEvent(vibration);
        var pattern = new CHHapticPattern(new[] { hapticEvent }, new[] { curve, }, out var error);
        error.ThrowIfError();
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
