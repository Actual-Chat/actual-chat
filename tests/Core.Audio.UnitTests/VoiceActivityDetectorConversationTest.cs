using ActualChat.Audio;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Core.Audio.UnitTests;

public class VoiceActivityDetectorConversationTest
{
    private const int WindowSamples = 512;
    private const int SampleRate = VoiceActivityDetector.SampleRate;

    [Fact]
    public void ShortPauseDoesNotEndMonologue()
    {
        var vad = CreateVad();

        var events = Feed(vad, speechS: 3, pauseS: 1.2);

        events.Select(e => e.Kind).Should().Equal(VoiceActivityKind.Start);
    }

    [Fact]
    public void ShortPauseEndsUtteranceAfterConversationSignal()
    {
        var vad = CreateVad();

        var events = Feed(vad, speechS: 3, pauseS: 1.2, signalConversation: true);

        events.Select(e => e.Kind).Should().Equal(VoiceActivityKind.Start, VoiceActivityKind.End);
    }

    private static List<VoiceActivityChange> Feed(
        ScriptedVoiceActivityDetector vad, double speechS, double pauseS, bool signalConversation = false)
    {
        var speechFrames = (int)(speechS * SampleRate / WindowSamples);
        var pauseFrames = (int)(pauseS * SampleRate / WindowSamples);
        var events = new List<VoiceActivityChange>();
        for (var i = 0; i < speechFrames + pauseFrames; i++) {
            if (signalConversation && i == speechFrames / 2)
                vad.ConversationSignal();
            vad.SpeechProb = i < speechFrames ? 0.9f : 0f;
            foreach (var result in vad.AppendChunk(Tone()))
                if (result.HasEvent)
                    events.Add(result.Change!.Value);
        }
        return events;
    }

    private static float[] Tone()
    {
        var pcm = new float[WindowSamples];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = 0.1f * MathF.Sin(i * 0.1f);
        return pcm;
    }

    private static ScriptedVoiceActivityDetector CreateVad()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HostInfo());
        return new ScriptedVoiceActivityDetector(services.BuildServiceProvider());
    }

    // Replaces the model with a probability the test dictates per window, keeping the base pause logic intact.
    private sealed class ScriptedVoiceActivityDetector(IServiceProvider services) : VoiceActivityDetector(services)
    {
        public float SpeechProb { get; set; }
        public override bool IsInitialized => true;

        public override Task EnsureInitialized(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        protected internal override float[]? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
        {
            var probs = new float[monoPcm.Length / WindowSamples];
            Array.Fill(probs, SpeechProb);
            return probs;
        }

        protected override void Dispose(bool disposing)
        { }
    }
}
