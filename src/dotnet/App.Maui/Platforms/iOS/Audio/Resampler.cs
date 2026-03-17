using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class Resampler(AVAudioFormat sourceFormat, AVAudioFormat targetFormat, ILogger<Resampler> log) : IDisposable
{
    private readonly AVAudioConverter _converter = new(sourceFormat, targetFormat);
    private readonly AVAudioPcmBuffer _resampledBuffer = new (targetFormat, (uint)sourceFormat.SampleRate); // up to 1 second

    public void Dispose()
        => _converter.Dispose();

    public void Transform(AVAudioPcmBuffer input, BlockRingBuffer<float> output)
    {
        if (!input.Format.IsEqual(sourceFormat))
            throw new InvalidOperationException("Input buffer format does not match the resampler's target format.");

        bool consumed = false;
        var status = _converter.ConvertToBuffer(_resampledBuffer, out var error, HandleInput);

        error.Assert();
        if (status is not AVAudioConverterOutputStatus.HaveData and not AVAudioConverterOutputStatus.InputRanDry)
            throw StandardError.Internal($"AVAudioConverter returned status {status}.");

        output.TryWrite(_resampledBuffer.AsReadOnlySpan());
        return;

        AVAudioBuffer HandleInput(uint numberOfPackets, out AVAudioConverterInputStatus outStatus)
        {
            if (consumed) {
                outStatus = AVAudioConverterInputStatus.NoDataNow;
                return null!;
            }

            consumed = true;
            outStatus = AVAudioConverterInputStatus.HaveData;
            return input;
        }
    }
}
