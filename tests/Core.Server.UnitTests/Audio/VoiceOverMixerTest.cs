using ActualChat.Audio;

namespace ActualChat.Core.Server.UnitTests.Audio;

public class VoiceOverMixerTest
{
    private const int FrameLength = VoiceOverMixer.FrameLength;
    private const float DuckGain = 0.25f;
    private const int HoldFrameCount = 3;
    private const int RampSampleCount = 2 * FrameLength; // two frames, so the ramp is observable per frame

    [Fact]
    public void OriginalShouldPassThroughAtFullGainWithoutDub()
    {
        // arrange
        var mixer = NewMixer();
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act
        mixer.Mix(original, output, isDubSpeakingElsewhere: false);

        // assert
        output.Should().AllBeEquivalentTo((short)1000);
        mixer.IsDubSpeaking.Should().BeFalse();
    }

    [Fact]
    public void DubShouldBeSummedAndTheOriginalDuckedOverTheRamp()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000, 3 * FrameLength)));
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act - three frames of dub: the ramp spans the first two
        mixer.Mix(original, output, false);
        var first = output.ToArray();
        mixer.Mix(original, output, false);
        var second = output.ToArray();
        mixer.Mix(original, output, false);
        var third = output.ToArray();

        // assert - the original's gain falls from 1.0 towards 0.25 sample by sample, then holds
        first[0].Should().BeCloseTo((short)(2000 + 1000), 20);
        first[^1].Should().BeLessThan(first[0]);
        second[^1].Should().BeCloseTo((short)(2000 + 250), 20);
        third.Should().AllSatisfy(x => x.Should().BeCloseTo((short)2250, 5));
        mixer.IsDubSpeaking.Should().BeTrue();
        mixer.HasDubAudio.Should().BeFalse();
    }

    [Fact]
    public void DuckShouldHoldOverAShortGapAndReleaseAfterALongOne()
    {
        // arrange - one frame of dub, then silence
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000)));
        var original = Constant(1000);
        var output = new short[FrameLength];
        mixer.Mix(original, output, false);
        mixer.Mix(original, output, false); // ramp completes here (2 frames)

        // act - HoldFrameCount frames without dub keep the duck, the next one starts the release
        var heldGains = new List<short>();
        for (var i = 0; i < HoldFrameCount; i++) {
            mixer.Mix(original, output, false);
            heldGains.Add(output[^1]);
        }
        var isSpeakingAfterHold = mixer.IsDubSpeaking;
        mixer.Mix(original, output, false);
        var releasing = output[^1];
        mixer.Mix(original, output, false);
        var released = output[^1];

        // assert
        heldGains.Should().AllSatisfy(x => x.Should().BeCloseTo((short)250, 5));
        isSpeakingAfterHold.Should().BeTrue();
        releasing.Should().BeGreaterThan((short)250).And.BeLessThan((short)1000);
        released.Should().BeCloseTo((short)1000, 5);
        mixer.IsDubSpeaking.Should().BeFalse();
    }

    [Fact]
    public void SpeakingElsewhereShouldDuckWithoutDubSamples()
    {
        // arrange
        var mixer = NewMixer();
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act
        mixer.Mix(original, output, isDubSpeakingElsewhere: true);
        mixer.Mix(original, output, isDubSpeakingElsewhere: true);

        // assert
        output[^1].Should().BeCloseTo((short)250, 5);
        mixer.IsDubSpeaking.Should().BeTrue();
    }

    [Fact]
    public void EndedOriginalShouldGiveTheDubAlone()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000)));
        var output = new short[FrameLength];

        // act
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);

        // assert
        output.Should().AllBeEquivalentTo((short)2000);
    }

    [Fact]
    public void SumShouldClampToShortRange()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(30000)));
        var output = new short[FrameLength];

        // act - the ramp starts at full gain, so the first sample is 30000 + 30000
        mixer.Mix(Constant(30000), output, false);

        // assert
        output[0].Should().Be(short.MaxValue);
    }

    [Fact]
    public void DubPcmShouldBeSlicedAcrossFrames()
    {
        // arrange - one and a half frames of dub, delivered as odd-sized chunks
        var mixer = NewMixer();
        var pcm = Bytes(Constant(2000, FrameLength * 3 / 2));
        mixer.AddDubPcm(pcm.AsSpan(0, 1000));
        mixer.AddDubPcm(pcm.AsSpan(1000));
        var output = new short[FrameLength];

        // act
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);
        var first = output.ToArray();
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);
        var second = output.ToArray();

        // assert - the second frame is half dub, half silence
        first.Should().AllBeEquivalentTo((short)2000);
        second[..(FrameLength / 2)].Should().AllBeEquivalentTo((short)2000);
        second[(FrameLength / 2)..].Should().AllBeEquivalentTo((short)0);
        mixer.HasDubAudio.Should().BeFalse();
    }

    // Private methods

    private static VoiceOverMixer NewMixer()
        => new(DuckGain, HoldFrameCount, RampSampleCount);

    private static short[] Constant(short value, int length = FrameLength)
    {
        var samples = new short[length];
        Array.Fill(samples, value);
        return samples;
    }

    private static byte[] Bytes(short[] samples)
        => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
}
