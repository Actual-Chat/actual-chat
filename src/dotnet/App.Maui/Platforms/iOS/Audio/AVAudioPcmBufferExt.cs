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

    public static ReadOnlySpan<float> AsReadOnlySpan(this AVAudioPcmBuffer pcm)
    {
        unsafe {
            return new ReadOnlySpan<float>(pcm.AudioBufferList[0].Data.ToPointer(), (int)pcm.FrameLength);
        }
    }
}
