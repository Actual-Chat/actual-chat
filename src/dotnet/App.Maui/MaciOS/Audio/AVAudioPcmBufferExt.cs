using System.Buffers;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public static class AVAudioPcmBufferExt
{
    public static float[] ToFloats(this AVAudioPcmBuffer pcm)
    {
        var data = new float[pcm.FrameLength];
        Marshal.Copy(pcm.AudioBufferList[0].Data, data, 0, (int)pcm.FrameLength);
        return data;
    }

    public static AVAudioPcmBuffer ToPcmBuffer(this IMemoryOwner<float> frame, AVAudioFormat format)
        => frame.Memory.Span.ToPcmBuffer(format);

    public static AVAudioPcmBuffer ToPcmBuffer(this ReadOnlySpan<float> data, AVAudioFormat format)
    {
        var buffer = new AVAudioPcmBuffer(format, (uint)data.Length);
        buffer.SetData(data);
        return buffer;
    }

    public static unsafe ReadOnlySpan<float> AsReadOnlySpan(this AVAudioPcmBuffer pcm)
        => new (pcm.AudioBufferList[0].Data.ToPointer(), (int)pcm.FrameLength);

    public static void SetData(this AVAudioPcmBuffer buffer, ReadOnlySpan<float> data)
    {
        unsafe
        {
            var dstSpan = new Span<float>(buffer.AudioBufferList[0].Data.ToPointer(), data.Length);
            data.CopyTo(dstSpan);
        }

        buffer.FrameLength = (uint)data.Length;
    }
}
