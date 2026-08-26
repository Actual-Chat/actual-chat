using System.Buffers;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.App.Maui.Services.Recording;

public interface IAudioCapture
{
    Task<AudioCaptureResult> Capture(CancellationToken cancellationToken);
}

/// <summary>
/// Either a capture stream or the reason there isn't one. Platforms that only learn a stream is
/// broken while enumerating it - AVAudioEngine - report success here and fail later instead.
/// </summary>
public readonly record struct AudioCaptureResult(
    IAsyncEnumerable<IMemoryOwner<float>>? Stream,
    RecorderStartResult Result = RecorderStartResult.Started,
    string? Code = null)
{
    public static AudioCaptureResult Ok(IAsyncEnumerable<IMemoryOwner<float>> stream) => new(stream);
    public static AudioCaptureResult Failed(RecorderStartResult result, string? code = null) => new(null, result, code);
    public RecorderStartOutcome ToOutcome() => new(Result, Code);
}
