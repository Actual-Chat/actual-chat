using ActualChat.UI.Blazor.App.Services;
using ActualLab.Internal;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class ResamplerFactory(AppUIHub hub)
{
    public Resampler Create(AVAudioFormat sourceFormat, AVAudioFormat targetFormat)
        => new(sourceFormat, targetFormat, hub.LogFor<Resampler>());
}

public class Resampler(AVAudioFormat sourceFormat, AVAudioFormat targetFormat, ILogger<Resampler> log) : IDisposable
{
    private readonly AVAudioConverter _converter = new(sourceFormat, targetFormat);

    public void Dispose()
        => _converter.Dispose();

    public AVAudioPcmBuffer Transform(AVAudioPcmBuffer input)
    {
        if (!input.Format.IsEqual(sourceFormat))
            throw new InvalidOperationException("Input buffer format does not match the resampler's target format.");

        // TODO: reuse buffer
        var estimatedFrames = (uint)Math.Ceiling(input.FrameLength * targetFormat.SampleRate / sourceFormat.SampleRate) + 16; // safety margin
        var output = new AVAudioPcmBuffer(targetFormat, estimatedFrames);
        bool consumed = false;
        var status = _converter.ConvertToBuffer(output, out var error, (_, out ioStatus) =>
        {
            if (consumed)
            {
                ioStatus = AVAudioConverterInputStatus.NoDataNow;
                return null!;
            }

            consumed = true;
            ioStatus = AVAudioConverterInputStatus.HaveData;
            return input;
        });

        error.Assert();

        return status is AVAudioConverterOutputStatus.HaveData or AVAudioConverterOutputStatus.InputRanDry
            ? output
            : throw Errors.InternalError($"AVAudioConverter returned status {status}.");
    }
}
