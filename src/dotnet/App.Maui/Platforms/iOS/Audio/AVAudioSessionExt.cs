using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public static class AVAudioSessionExt
{
    extension(AVAudioSession session)
    {
        // Null for a category or mode the enum doesn't know: the generated GetValue throws on one,
        // and a value iOS grows later must leave the route alone rather than take the session down.
        public AVAudioSessionCategory? GetCategory()
        {
            try {
                return AVAudioSessionCategoryExtensions.GetValue(session.Category);
            }
            catch (NotSupportedException) {
                return null;
            }
        }

        public AVAudioSessionMode? GetMode()
        {
            try {
                return AVAudioSessionModeExtensions.GetValue(session.Mode);
            }
            catch (NotSupportedException) {
                return null;
            }
        }

        public string Describe()
        {
            var route = session.CurrentRoute;
            return $"category={session.Category}, mode={session.Mode}, rate={session.SampleRate}, "
                + $"inputChannels={session.InputNumberOfChannels}, inputs={route.Inputs.Describe()}, "
                + $"outputs={route.Outputs.Describe()}, otherAudio={session.OtherAudioPlaying}";
        }

        // Everything the engine's I/O unit watches: a change to the rate or the channel count is
        // what makes AVAudioEngine stop itself, and the preferred values say what iOS granted us.
        public string DescribeFormat()
            => $"{session.SampleRate:F0} Hz (preferred {session.PreferredSampleRate:F0}), "
                + $"out {session.OutputNumberOfChannels}ch (preferred {session.GetPreferredOutputNumberOfChannels()}, "
                + $"max {session.MaximumOutputNumberOfChannels}), in {session.InputNumberOfChannels}ch, "
                + $"buffer {session.IOBufferDuration * 1000:F1}ms, outputs={session.CurrentRoute.Outputs.Describe()}";
    }

    extension(AVAudioSessionPortDescription[] ports)
    {
        public string Describe()
            => string.Join(", ", ports.Select(x => x.PortType));
    }
}
